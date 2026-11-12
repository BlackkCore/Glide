namespace ServiceLib.Common;

/// <summary>
/// Glide: installs Xray, sing-box or mihomo into the protected program folder (installed version) from a short-lived
/// administrator process. Glide itself never runs as administrator for this.
/// The process trusts nothing it is handed: it re-checks the signed engine list against the embedded release key, picks
/// the build itself, and installs only a zip that is exactly that build. The target folder is derived from the program's
/// own location, never from an argument. Files are opened once, with writes and deletes denied, and read from that handle.
/// Exit code: 0 = all installed; otherwise bit n is set when job n failed; <see cref="IndexRejected"/> and
/// <see cref="BadArguments"/> when nothing was attempted.
/// </summary>
public static class ElevatedCoreInstall
{
    public const string Verb = "--install-cores";
    public const int IndexRejected = 100;
    public const int BadArguments = 101;
    public const int MaxJobs = 3;
    public const string StateFileName = "cores-index-state.json";

    private static readonly ECoreType[] Engines = [ECoreType.Xray, ECoreType.sing_box, ECoreType.mihomo];

    /// <param name="args">After the verb: index file, its ".sig256" file, then one or more Core=zip pairs.</param>
    /// <param name="programRoot">Folder Glide.exe lives in; engines go to its bin folder.</param>
    public static int Run(string[] args, string programRoot, string? publicKeyPem, DateTimeOffset now, SemanticVersion glideVersion, string? arch = null)
    {
        arch ??= CoreIndexKeys.CurrentArch();
        if (args.Length < 3 || args.Length > 2 + MaxJobs || arch == null)
        {
            return BadArguments;
        }

        var jobs = new List<(ECoreType Type, string Zip)>();
        foreach (var pair in args.Skip(2))
        {
            var eq = pair.IndexOf('=');
            var type = eq > 0 ? Engines.Cast<ECoreType?>().FirstOrDefault(t => t.ToString() == pair[..eq]) : null;
            if (type == null || jobs.Any(j => j.Type == type) || eq == pair.Length - 1)
            {
                return BadArguments;
            }
            jobs.Add((type.Value, pair[(eq + 1)..]));
        }

        var statePath = Path.Combine(programRoot, "bin", StateFileName);
        var index = ReadLocked(args[0], CoreIndexVerifier.MaxIndexBytes);
        var signature = ReadLocked(args[1], 4096);
        if (index == null || signature == null)
        {
            return IndexRejected;
        }
        var verified = CoreIndexVerifier.Verify(index, Encoding.UTF8.GetString(signature), publicKeyPem, now, CoreIndexState.Load(statePath));
        if (!verified.Ok)
        {
            Logging.SaveLog($"{nameof(ElevatedCoreInstall)}: engine list refused: {verified.Error}");
            return IndexRejected;
        }

        var code = 0;
        for (var i = 0; i < jobs.Count; i++)
        {
            var (type, zip) = jobs[i];
            var pick = CoreIndexVerifier.Pick(verified.Document!, CoreIndexKeys.For(type)!, arch, glideVersion);
            var binDir = GlidePaths.GetBinFolder(programRoot, programRoot, true, type.ToString());
            var result = pick == null
                ? new CoreInstallResult(false, CoreInstallError.NotVerified, "No listed build.")
                : CoreInstaller.Install(zip, pick.Asset, binDir);
            if (!result.Ok)
            {
                Logging.SaveLog($"{nameof(ElevatedCoreInstall)}: {type} refused: {result.Error}: {result.Message}");
                code |= 1 << i;
            }
        }

        // The replay floor lives in the protected folder, out of reach of ordinary programs.
        if (code != (1 << jobs.Count) - 1)
        {
            CoreIndexState.Save(statePath, verified.Document!.Sequence);
        }
        return code;
    }

    private static byte[]? ReadLocked(string path, int maxBytes)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length == 0 || stream.Length > maxBytes)
            {
                return null;
            }
            var bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
            return bytes;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>What one engine's install through the administrator process came to.</summary>
public enum ElevatedInstallOutcome
{
    Installed,
    Failed,
    Declined,
}

/// <summary>Starts the administrator process once for every selected engine, so there is a single Windows permission prompt.</summary>
public static class ElevatedCoreInstallLauncher
{
    /// <summary>ERROR_CANCELLED: the user said no in the permission prompt.</summary>
    private const int UacDeclined = 1223;

    /// <summary>Runs the process with these arguments and returns its exit code; throws <see cref="Win32Exception"/> when it cannot start.</summary>
    public delegate Task<int> Starter(string exePath, string arguments);

    public static async Task<Dictionary<ECoreType, ElevatedInstallOutcome>> RunAsync(
        IReadOnlyList<(ECoreType Type, string Zip)> jobs, string indexJson, string indexSignature, string tempFolder, Starter? starter = null)
    {
        var outcomes = new Dictionary<ECoreType, ElevatedInstallOutcome>();
        var indexPath = Path.Combine(tempFolder, Guid.NewGuid().ToString("N") + ".json");
        var sigPath = indexPath + UpdateSignature.Extension;
        try
        {
            await File.WriteAllTextAsync(indexPath, indexJson);
            await File.WriteAllTextAsync(sigPath, indexSignature);
            var arguments = string.Join(' ', new[] { ElevatedCoreInstall.Verb, indexPath, sigPath }
                .Concat(jobs.Select(j => $"{j.Type}={j.Zip}"))
                .Select(a => a.Contains(' ') ? $"\"{a}\"" : a));

            int exit;
            try
            {
                exit = await (starter ?? StartElevated)(Utils.GetExePath(), arguments);
            }
            catch (Win32Exception ex)
            {
                var declined = ex.NativeErrorCode == UacDeclined;
                Logging.SaveLog(nameof(ElevatedCoreInstallLauncher), ex);
                foreach (var (type, _) in jobs)
                {
                    outcomes[type] = declined ? ElevatedInstallOutcome.Declined : ElevatedInstallOutcome.Failed;
                }
                return outcomes;
            }

            for (var i = 0; i < jobs.Count; i++)
            {
                outcomes[jobs[i].Type] = exit == 0 || (exit < ElevatedCoreInstall.IndexRejected && (exit & (1 << i)) == 0)
                    ? ElevatedInstallOutcome.Installed
                    : ElevatedInstallOutcome.Failed;
            }
            return outcomes;
        }
        finally
        {
            TryDelete(indexPath);
            TryDelete(sigPath);
        }
    }

    private static async Task<int> StartElevated(string exePath, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = arguments,
            WorkingDirectory = GlidePaths.ProgramRoot,
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        }) ?? throw new Win32Exception(UacDeclined);
        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logging.SaveLog(nameof(ElevatedCoreInstallLauncher), ex);
        }
    }
}
