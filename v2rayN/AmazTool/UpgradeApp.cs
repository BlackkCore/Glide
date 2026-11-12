using System.Diagnostics;
using System.IO.Compression;
using System.Text;

namespace AmazTool;

internal class UpgradeApp
{
    public static void Upgrade(string fileName)
    {
        Console.WriteLine($"{Resx.Resource.StartUnzipping}\n{fileName}");

        Utils.Waiting(5);

        if (!File.Exists(fileName))
        {
            Console.WriteLine(Resx.Resource.UpgradeFileNotFound);
            // Glide (S-09): Glide quit itself to let the update run; start it again.
            Utils.StartV2RayN();
            return;
        }

        // Glide (S-01/S-02): open the zip once with writes and deletes denied, check its signature on that
        // handle, and extract from the same handle, so it can't be swapped after the check.
        using var zipStream = OpenLocked(fileName);
        var signaturePath = fileName + ".sig256";
        var signature = File.Exists(signaturePath) ? File.ReadAllText(signaturePath) : null;
        if (zipStream == null || !ServiceLib.Common.UpdateSignatureKey.VerifyStream(zipStream, signature, ServiceLib.Common.UpdateSignatureKey.PublicKeyPem))
        {
            Console.WriteLine($"{Resx.Resource.FailedUpgrade} the update's signature did not verify");
            Utils.StartV2RayN();
            return;
        }
        zipStream.Position = 0;
        using var archive = TryOpenArchive(zipStream);

        // Glide (S-09): check the whole archive before touching anything; Glide has already quit
        // itself for the update, so a bad archive starts the old Glide again instead of extracting.
        if (!IsSafeArchive(archive))
        {
            Utils.StartV2RayN();
            return;
        }

        Console.WriteLine(Resx.Resource.TryTerminateProcess);
        try
        {
            var existing = Process.GetProcessesByName(Utils.V2rayN);
            foreach (var pp in existing)
            {
                var path = pp.MainModule?.FileName ?? "";
                if (path.StartsWith(Utils.GetPath(Utils.V2rayN)))
                {
                    pp?.Kill();
                    pp?.WaitForExit(1000);
                }
            }
        }
        catch (Exception ex)
        {
            // Access may be denied without admin right. The user may not be an administrator.
            Console.WriteLine(Resx.Resource.FailedTerminateProcess + $"{ex.GetType().Name}: {ex.Message}");
        }

        Console.WriteLine(Resx.Resource.StartUnzipping);
        StringBuilder sb = new();
        try
        {
            var thisAppOldFile = $"{Utils.GetExePath()}.tmp";
            File.Delete(thisAppOldFile);
            var splitKey = "/";
            var allowedBaseDir = Utils.StartupPath();
            var pathComparison = Utils.GetPathComparison();

            foreach (var entry in EntriesSelfLast(archive))
            {
                try
                {
                    if (entry.Length == 0)
                    {
                        continue;
                    }

                    Console.WriteLine(entry.FullName);

                    var lst = entry.FullName.Split(splitKey);
                    if (lst.Length == 1)
                    {
                        continue;
                    }

                    var fullName = string.Join(splitKey, lst[1..lst.Length]);
                    var entryOutputPath = Utils.GetPath(fullName);
                    if (!Utils.IsPathUnderDirectory(allowedBaseDir, entryOutputPath))
                    {
                        // Already rejected by IsSafeArchive; kept as a second line of defence.
                        Console.WriteLine($"{Resx.Resource.FailedUpgrade} blocked potential path traversal: {entry.FullName}");
                        break;
                    }

                    var isSelf = string.Equals(Utils.GetExePath(), entryOutputPath, pathComparison);
                    if (isSelf)
                    {
                        File.Move(Utils.GetExePath(), thisAppOldFile);
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(entryOutputPath)!);
                    //In the bin folder, if the file already exists, it will be skipped
                    // Glide (S-01): an installed Glide gets new cores only with app updates, so they are replaced.
                    if (fullName.StartsWith("bin", pathComparison) && File.Exists(entryOutputPath) && !Utils.IsInstalled)
                    {
                        continue;
                    }

                    if (!TryExtractToFile(entry, entryOutputPath) && isSelf)
                    {
                        // Glide: the updater moved itself aside; without the new copy, put the old one back
                        // so the next update can still run.
                        File.Move(thisAppOldFile, entryOutputPath, true);
                        sb.Append($"could not replace {entry.FullName}; kept the old copy");
                        continue;
                    }

                    Console.WriteLine(entryOutputPath);
                }
                catch (Exception ex)
                {
                    sb.Append($"{entry.FullName}: {ex.GetType().Name}: {ex.Message}{Environment.NewLine}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(Resx.Resource.FailedUpgrade + $"{ex.GetType().Name}: {ex.Message}");
            //return;
        }
        if (sb.Length > 0)
        {
            Console.WriteLine(Resx.Resource.FailedUpgrade + sb.ToString());
            //return;
        }

        Console.WriteLine(Resx.Resource.Restartv2rayN);
        Utils.Waiting(2);

        Utils.StartV2RayN();
    }

    /// <summary>
    /// The entries with the updater's own exe last. Once the running exe is renamed aside (to be replaced), the runtime
    /// can no longer load what it loads lazily from that path, and every extraction after it threw FileNotFoundException:
    /// an update whose zip held AmazTool.exe extracted nothing (found on a Windows runner, installed Glide 0.0.1).
    /// </summary>
    private static IEnumerable<ZipArchiveEntry> EntriesSelfLast(ZipArchive archive)
    {
        var self = Utils.GetExePath();
        var comparison = Utils.GetPathComparison();
        bool IsSelf(ZipArchiveEntry e)
        {
            var parts = e.FullName.Split('/');
            return parts.Length > 1 && string.Equals(self, Utils.GetPath(string.Join('/', parts[1..])), comparison);
        }
        return archive.Entries.OrderBy(IsSelf);
    }

    /// <summary>The zip, open for reading with writes and deletes denied to everyone else; null when it can't be.</summary>
    private static FileStream? OpenLocked(string fileName)
    {
        try
        {
            return new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (Exception ex)
        {
            Console.WriteLine(Resx.Resource.FailedUpgrade + ex.Message);
            return null;
        }
    }

    private static ZipArchive? TryOpenArchive(Stream stream)
    {
        try
        {
            return new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (Exception ex)
        {
            Console.WriteLine(Resx.Resource.FailedUpgrade + ex.Message);
            return null;
        }
    }

    /// <summary>The zip opens and every entry stays inside the app folder.</summary>
    internal static bool IsSafeArchive(ZipArchive? archive)
    {
        if (archive == null)
        {
            return false;
        }
        try
        {
            var allowedBaseDir = Utils.StartupPath();
            foreach (var entry in archive.Entries)
            {
                var lst = entry.FullName.Split('/');
                if (entry.Length == 0 || lst.Length == 1)
                {
                    continue;
                }
                if (!Utils.IsPathUnderDirectory(allowedBaseDir, Utils.GetPath(string.Join('/', lst[1..]))))
                {
                    Console.WriteLine($"{Resx.Resource.FailedUpgrade} blocked potential path traversal: {entry.FullName}");
                    return false;
                }
            }
            return archive.Entries.Count > 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine(Resx.Resource.FailedUpgrade + ex.Message);
            return false;
        }
    }

    private static bool TryExtractToFile(ZipArchiveEntry entry, string outputPath)
    {
        var retryCount = 5;
        var delayMs = 1000;

        for (var i = 1; i <= retryCount; i++)
        {
            try
            {
                entry.ExtractToFile(outputPath, true);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"extract attempt {i} of {entry.FullName} failed: {ex.GetType().Name}: {ex.Message}");
                Thread.Sleep(delayMs * i);
            }
        }
        return false;
    }
}
