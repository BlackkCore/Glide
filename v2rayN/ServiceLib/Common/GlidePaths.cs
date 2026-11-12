namespace ServiceLib.Common;

/// <summary>
/// Glide (S-01): where programs and data live.
/// The per-machine installer puts Glide in Program Files and writes the marker file "glide-installed"
/// next to Glide.exe. In that installed mode, everything Glide writes (settings, logs, temp, geo
/// data) lives in %LOCALAPPDATA%\Glide, and executables (cores, helpers) are only ever taken from
/// the protected program folder, so nothing that may run as administrator can be swapped by an
/// ordinary program. Without the marker (portable zip, old per-user installs) nothing changes.
/// </summary>
public static class GlidePaths
{
    public const string InstalledMarker = "glide-installed";
    public const string DataFolderName = "Glide";

    /// <summary>The folder Glide.exe runs from.</summary>
    public static string ProgramRoot => AppDomain.CurrentDomain.BaseDirectory;

    private static readonly Lazy<bool> Installed = new(() => File.Exists(Path.Combine(ProgramRoot, InstalledMarker)));

    public static bool IsInstalled => Installed.Value;

    /// <summary>Where Glide writes its data.</summary>
    public static string DataRoot => GetDataRoot(ProgramRoot, IsInstalled, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    public static string GetDataRoot(string programRoot, bool installed, string localAppData)
        => installed ? Path.Combine(localAppData, DataFolderName) : programRoot;

    /// <summary>
    /// Where settings, logs and temp files go. Installed, the data folder wins over the "no write access" switch:
    /// Program Files is never writable for an ordinary user, so that switch is always on for them, and without this it
    /// sent an installed Glide's data to %LOCALAPPDATA%\v2rayN (the original v2rayN's folder). Only portable
    /// copies that cannot write beside themselves fall back to %LOCALAPPDATA%\v2rayN.
    /// </summary>
    public static string GetStartupPath(bool installed, string dataRoot, bool noWriteAccessSwitch, string localAppData, string baseDir)
        => installed ? dataRoot : noWriteAccessSwitch ? Path.Combine(localAppData, "v2rayN") : baseDir;

    /// <summary>
    /// The folder holding a core's executables (coreType set) or the shared bin folder (geo data,
    /// rule sets, cache). Installed, executables come from the program folder; data stays in the data folder.
    /// </summary>
    public static string GetBinFolder(string programRoot, string dataRoot, bool installed, string? coreType)
    {
        if (coreType == null)
        {
            return Path.Combine(dataRoot, "bin");
        }
        return Path.Combine(installed ? programRoot : dataRoot, "bin", coreType.ToLowerInvariant());
    }

    /// <summary>A helper executable shipped in bin (e.g. EnableLoopback.exe), from the protected folder when installed.</summary>
    public static string ProgramBinFile(string fileName)
        => Path.Combine(IsInstalled ? ProgramRoot : Utils.StartupPath(), "bin", fileName);

    private static readonly HashSet<string> ExecutableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".com", ".bat", ".cmd", ".ps1", ".psm1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh",
        ".msi", ".msp", ".scr", ".sys", ".lnk", ".url", ".pif", ".cpl", ".hta", ".reg", ".appref-ms",
    };

    /// <summary>A file Windows would run (or open as code) rather than open as a document.</summary>
    public static bool IsExecutableFile(string path) => ExecutableExtensions.Contains(Path.GetExtension(path));

    /// <summary>The folder whose processes count as Glide's own (its cores).</summary>
    public static string OwnProcessRoot => IsInstalled ? ProgramRoot : Utils.StartupPath();
}
