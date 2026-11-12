namespace ServiceLib.Common;

/// <summary>
/// Glide (S-01): first start of an installed (Program Files) Glide.
/// 1. Once: copy the settings of an old per-user install (%LOCALAPPDATA%\Programs\Glide) into the new
///    data folder. Nothing is deleted; the old install stays for the user to uninstall.
/// 2. Every start: the geo files and rule sets shipped in Program Files\Glide\bin are copied into the
///    data folder's bin when missing (the cores read them there, and updates write them there).
/// Executables are never copied: in installed mode they run only from the protected program folder.
/// Done here rather than in the installer, which runs elevated and may see another user's LocalAppData.
/// </summary>
public static class InstallMigration
{
    public const string MigratedMarker = "glide-migrated";
    public const string PendingMarker = "glide-migration-pending";

    /// <summary>The old per-user install folder.</summary>
    public static string OldPerUserRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Glide");

    /// <summary>
    /// Runs the migration for this installed Glide; failures are logged and never stop the start.
    /// True when an old install's settings were copied just now.
    /// </summary>
    public static bool RunInstalled()
    {
        try
        {
            return Run(GlidePaths.ProgramRoot, GlidePaths.DataRoot, OldPerUserRoot);
        }
        catch (Exception ex)
        {
            // Runs before InitApp sets logging up; do it here so the failure is recorded.
            Logging.Setup();
            Logging.SaveLog(nameof(InstallMigration), ex);
            return false;
        }
    }

    /// <summary>
    /// After settings were copied with "start with Windows" on: the old install's per-user autostart entry
    /// would keep starting the old copy (two Glides on one port), and the new one isn't registered until
    /// Settings are saved. Moves the entry to this install; the old program itself is left alone.
    /// </summary>
    public static void MoveAutostart(Config config)
    {
        if (!OperatingSystem.IsWindows() || !config.GuiItem.AutoRun)
        {
            return;
        }
        try
        {
            // The old install named its entry after its own folder (with the trailing separator, as
            // AppDomain.BaseDirectory gives it). An elevated scheduled task of the old copy isn't removed.
            var oldName = $"{Global.AutoRunName}_{Utils.GetMd5(OldPerUserRoot + Path.DirectorySeparatorChar)}";
            WindowsUtils.RegWriteValue(Global.AutoRunRegPath, oldName, "");
            AutoStartupHandler.UpdateTask(config).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"{nameof(InstallMigration)}.{nameof(MoveAutostart)}", ex);
        }
    }

    /// <returns>True when an old install's settings were copied.</returns>
    public static bool Run(string programRoot, string dataRoot, string oldRoot)
    {
        var copied = false;
        Directory.CreateDirectory(dataRoot);
        var marker = Path.Combine(dataRoot, MigratedMarker);
        if (!File.Exists(marker))
        {
            // Only into an empty new data folder: never mix two sets of settings.
            var newConfigs = Path.Combine(dataRoot, "guiConfigs");
            var oldConfigs = Path.Combine(oldRoot, "guiConfigs");
            // A first attempt that failed leaves this file. The app then started with fresh settings, which
            // would make the next start skip the migration for good, so the next start sets those aside
            // and tries once more (then gives up either way).
            var pending = Path.Combine(dataRoot, PendingMarker);
            var retry = File.Exists(pending);
            string? setAside = null;
            if (retry)
            {
                File.Delete(pending);
                if (Directory.Exists(newConfigs) && Directory.Exists(oldConfigs))
                {
                    setAside = $"{newConfigs}.backup-{DateTime.Now:yyyyMMdd-HHmmss}";
                    Directory.Move(newConfigs, setAside);
                }
            }
            if (!Directory.Exists(newConfigs) && Directory.Exists(oldConfigs))
            {
                // Copied beside its destination and renamed in one step: a failure part-way (the old Glide
                // may hold a file open) must not leave half the settings that the next start would accept.
                var staging = newConfigs + ".migrating";
                try
                {
                    if (!retry)
                    {
                        File.WriteAllText(pending, DateTime.UtcNow.ToString("O"));
                    }
                    if (Directory.Exists(staging))
                    {
                        Directory.Delete(staging, true);
                    }
                    Directory.CreateDirectory(staging);
                    CopyData(oldConfigs, staging);
                    Directory.Move(staging, newConfigs);
                    File.Delete(pending);
                }
                catch (Exception ex)
                {
                    try
                    {
                        if (Directory.Exists(staging))
                        {
                            Directory.Delete(staging, true);
                        }
                    }
                    catch
                    {
                        // The next attempt removes it first.
                    }
                    if (!retry)
                    {
                        throw; // the marker stays and the next start retries once
                    }
                    // Second failure: give up, and put back what was set aside.
                    if (setAside != null && !Directory.Exists(newConfigs))
                    {
                        Directory.Move(setAside, newConfigs);
                    }
                    try
                    {
                        Logging.SaveLog($"{nameof(InstallMigration)}: gave up copying the old settings", ex);
                    }
                    catch
                    {
                        // Nothing more to do.
                    }
                    File.WriteAllText(marker, DateTime.UtcNow.ToString("O"));
                    CopyData(Path.Combine(programRoot, "bin"), Path.Combine(dataRoot, "bin"));
                    return false;
                }
                // The settings are what matters; backups and the geo files the old install had already
                // updated are nice to have and are never allowed to fail the migration.
                TryCopyData(Path.Combine(oldRoot, "guiBackups"), Path.Combine(dataRoot, "guiBackups"));
                TryCopyData(Path.Combine(oldRoot, "bin"), Path.Combine(dataRoot, "bin"));
                copied = true;
            }
            File.WriteAllText(marker, DateTime.UtcNow.ToString("O"));
        }
        CopyData(Path.Combine(programRoot, "bin"), Path.Combine(dataRoot, "bin"));
        return copied;
    }

    private static void TryCopyData(string from, string to)
    {
        try
        {
            CopyData(from, to);
        }
        catch (Exception ex)
        {
            // Runs before the log is set up on a first start, so a failure here may not be recordable.
            try
            {
                Logging.SaveLog($"{nameof(InstallMigration)}: not everything in {Path.GetFileName(from)} was copied", ex);
            }
            catch
            {
                // Nothing more to do.
            }
        }
    }

    /// <summary>Copies non-executable files that are missing at the destination; never overwrites or deletes.</summary>
    private static void CopyData(string from, string to)
    {
        if (!Directory.Exists(from))
        {
            return;
        }
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            if (GlidePaths.IsExecutableFile(file))
            {
                continue;
            }
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            if (File.Exists(target))
            {
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: false);
        }
    }
}
