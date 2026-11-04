namespace ServiceLib.Handler;

/// <summary>
/// Brings the servers, subscriptions and settings of another v2rayN installation into this one.
/// The copy is staged next to guiConfigs and swapped in on the next start, because the running
/// app saves its own config when it exits and would overwrite an in-place copy.
/// </summary>
public static class ConfigImportHandler
{
    private static readonly string _tag = "ConfigImportHandler";
    private const string ConfigDirName = "guiConfigs";
    private const string StagingDirName = "guiConfigs.import";
    private const string DismissedFileName = "import-offer-dismissed";
    private const int MaxScannedDirs = 4000;

    /// <summary>
    /// Finds the guiConfigs folder of another v2rayN installation. A running copy wins;
    /// otherwise the most recently used one found in the usual places.
    /// </summary>
    public static string? FindOriginalConfigDir()
    {
        try
        {
            var running = RunningInstallDirs()
                .Select(GetConfigDirFromFolder)
                .FirstOrDefault(t => t != null);
            if (running != null)
            {
                return running;
            }

            return CommonInstallDirs()
                .Select(GetConfigDirFromFolder)
                .Where(t => t != null)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(t => File.GetLastWriteTimeUtc(Path.Combine(t!, Global.ConfigFileName)))
                .FirstOrDefault();
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            return null;
        }
    }

    /// <summary>
    /// Returns the guiConfigs folder for a v2rayN folder (or the guiConfigs folder itself),
    /// or null when it has no settings or they are this installation's own.
    /// </summary>
    public static string? GetConfigDirFromFolder(string? folder)
    {
        try
        {
            if (folder.IsNullOrEmpty() || !Directory.Exists(folder))
            {
                return null;
            }
            var full = NormalizeDir(folder);
            List<string> candidates =
            [
                Path.GetFileName(full).Equals(ConfigDirName, StringComparison.OrdinalIgnoreCase) ? full : Path.Combine(full, ConfigDirName),
            ];
            // Installs without write access keep their settings in LocalAppData.
            if (File.Exists(Path.Combine(full, "NotStoreConfigHere.txt")) || IsUnderProgramFiles(full))
            {
                candidates.Add(Path.Combine(LocalAppDataDir(), ConfigDirName));
            }

            var own = NormalizeDir(OwnConfigDir());
            return candidates.FirstOrDefault(t =>
                File.Exists(Path.Combine(t, Global.ConfigFileName))
                && !NormalizeDir(t).Equals(own, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            return null;
        }
    }

    /// <summary>
    /// Copies the other installation's settings to the staging folder, and its cores too when this
    /// installation has none. The caller restarts the app to finish the import.
    /// </summary>
    public static bool StageImport(string sourceConfigDir)
    {
        try
        {
            var staging = StagingDir();
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, true);
            }
            FileUtils.CopyDirectory(sourceConfigDir, staging, true, true);
            if (!File.Exists(Path.Combine(staging, Global.ConfigFileName)))
            {
                Directory.Delete(staging, true);
                return false;
            }

            var sourceInstallDir = Path.GetDirectoryName(NormalizeDir(sourceConfigDir));
            CopyCoresIfMissing(sourceInstallDir);
            CopyRegionalGeoLists(sourceInstallDir, staging);
            return true;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            return false;
        }
    }

    /// <summary>
    /// Runs at startup before the config is loaded: swaps a staged import in and keeps the
    /// previous settings as guiConfigs.backup-yyyyMMdd-HHmmss.
    /// </summary>
    public static void ApplyPendingImport()
    {
        var staging = StagingDir();
        if (!Directory.Exists(staging))
        {
            return;
        }

        try
        {
            // The instance that staged the import may still be saving its config on the way out.
            WaitForOtherInstancesToExit(TimeSpan.FromSeconds(15));

            var configDir = OwnConfigDir();
            if (Directory.Exists(configDir))
            {
                Directory.Move(configDir, $"{configDir}.backup-{DateTime.Now:yyyyMMdd-HHmmss}");
            }
            Directory.Move(staging, configDir);
            Logging.SaveLog("Imported settings from another v2rayN installation");
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
    }

    public static bool IsOfferDismissed()
    {
        return File.Exists(Path.Combine(OwnConfigDir(), DismissedFileName));
    }

    public static void DismissOffer()
    {
        try
        {
            File.WriteAllText(Utils.GetConfigPath(DismissedFileName), DateTime.Now.ToString("O"));
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
    }

    private static string OwnConfigDir()
    {
        // Not Utils.GetConfigPath(): that creates the folder, and the import moves it.
        return Path.Combine(Utils.StartupPath(), ConfigDirName);
    }

    private static string StagingDir()
    {
        return Path.Combine(Utils.StartupPath(), StagingDirName);
    }

    /// <summary>
    /// The imported settings may use a regional geo source (Iran, Russia) whose routing and DNS ask
    /// for lists like "geosite:ir" that Glide's bundled world lists don't have; without them the
    /// engine can't start. Bring the other installation's lists along in that case.
    /// </summary>
    private static void CopyRegionalGeoLists(string? sourceInstallDir, string stagedConfigDir)
    {
        try
        {
            if (sourceInstallDir.IsNullOrEmpty())
            {
                return;
            }
            var node = JsonUtils.ParseJson(File.ReadAllText(Path.Combine(stagedConfigDir, Global.ConfigFileName)));
            var geoSource = node?["ConstItem"]?["GeoSourceUrl"]?.GetValue<string>();
            if (geoSource.IsNullOrEmpty())
            {
                return;
            }
            var sourceBin = Path.Combine(sourceInstallDir, "bin");
            foreach (var name in new[] { "geosite.dat", "geoip.dat" })
            {
                var from = Path.Combine(sourceBin, name);
                if (File.Exists(from))
                {
                    File.Copy(from, Utils.GetBinPath(name), true);
                }
            }
            var fromSrs = Path.Combine(sourceBin, "srss");
            if (Directory.Exists(fromSrs))
            {
                FileUtils.CopyDirectory(fromSrs, Utils.GetBinPath("srss"), true, true);
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
    }

    private static void CopyCoresIfMissing(string? sourceInstallDir)
    {
        if (sourceInstallDir.IsNullOrEmpty())
        {
            return;
        }
        var sourceBin = Path.Combine(sourceInstallDir, "bin");
        var ownBin = Utils.GetBinPath("");
        var hasCores = File.Exists(Path.Combine(ownBin, "xray", Utils.GetExeName("xray")))
                       || File.Exists(Path.Combine(ownBin, "sing_box", Utils.GetExeName("sing-box")));
        if (hasCores || !Directory.Exists(sourceBin))
        {
            return;
        }
        // cache.db is sing-box runtime state, not part of the cores.
        FileUtils.CopyDirectory(sourceBin, ownBin, true, false, "cache.db");
    }

    private static List<string> RunningInstallDirs()
    {
        var ownExe = Utils.GetExePath();
        List<string> dirs = [];
        foreach (var process in Process.GetProcessesByName(Global.AppName))
        {
            try
            {
                var path = process.MainModule?.FileName;
                if (path.IsNotEmpty() && !path.Equals(ownExe, StringComparison.OrdinalIgnoreCase))
                {
                    dirs.Add(Path.GetDirectoryName(path)!);
                }
            }
            catch
            {
                // Processes of other users or elevated ones can't be inspected.
            }
        }
        return dirs;
    }

    private static IEnumerable<string> CommonInstallDirs()
    {
        var exeName = Utils.GetExeName(Global.AppName);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        List<string> roots =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Path.Combine(home, "Downloads"),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            home,
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
            Path.GetPathRoot(home) ?? string.Empty,
        ];

        // Settings of installs that can't write next to their exe.
        yield return LocalAppDataDir();

        var scanned = 0;
        foreach (var root in roots.Where(t => t.IsNotEmpty()).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            // Two levels deep covers layouts like Desktop\v2rayN-windows-64 and Desktop\tools\v2rayN.
            foreach (var dir in SubDirs(root).SelectMany(t => SubDirs(t).Prepend(t)))
            {
                if (++scanned > MaxScannedDirs)
                {
                    yield break;
                }
                if (File.Exists(Path.Combine(dir, exeName)))
                {
                    yield return dir;
                }
            }
        }
    }

    private static readonly HashSet<string> SkippedDirNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Windows", "AppData", "$Recycle.Bin", "System Volume Information", "ProgramData", "node_modules", ".git",
    };

    private static IEnumerable<string> SubDirs(string dir)
    {
        try
        {
            return Directory.Exists(dir)
                ? Directory.GetDirectories(dir).Where(t => !SkippedDirNames.Contains(Path.GetFileName(t)))
                : [];
        }
        catch
        {
            return [];
        }
    }

    private static string LocalAppDataDir()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Global.AppName);
    }

    private static bool IsUnderProgramFiles(string dir)
    {
        return new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            }
            .Where(t => t.IsNotEmpty())
            .Any(t => dir.StartsWith(t, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeDir(string dir)
    {
        return Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static void WaitForOtherInstancesToExit(TimeSpan timeout)
    {
        var ownExe = Utils.GetExePath();
        var deadline = DateTime.UtcNow + timeout;
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ownExe)))
        {
            if (process.Id == Environment.ProcessId)
            {
                continue;
            }
            try
            {
                if (!string.Equals(process.MainModule?.FileName, ownExe, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                var left = deadline - DateTime.UtcNow;
                if (left > TimeSpan.Zero)
                {
                    process.WaitForExit(left);
                }
            }
            catch
            {
                // Already gone, or not inspectable.
            }
        }
    }
}
