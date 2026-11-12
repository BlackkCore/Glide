using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ServiceLib.Services;

/// <summary>One Store (AppContainer) app and whether Windows lets it reach this computer's loopback address.</summary>
public sealed record LoopbackApp(string FamilyName, string DisplayName, string Sid, bool Exempt);

/// <summary>
/// Lets Windows Store (UWP) apps use the proxy on this computer. Windows blocks those apps from the loopback address
/// unless they are exempted; the exemption is made with Windows' own CheckNetIsolation tool, which Glide drives here.
/// This replaces the third-party EnableLoopback.exe, which Glide may not redistribute.
/// </summary>
public static partial class LoopbackExempt
{
    private const string MappingsKey = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppContainer\Mappings";

    // A package family name: a name, an underscore and the 13-character publisher id. Only these characters ever reach
    // a command line (the administrator-level call below joins several), so anything else is rejected.
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,127}_[a-z0-9]{13}$")]
    private static partial Regex FamilyNameRegex();

    [GeneratedRegex(@"S-1-15-2(?:-\d+)+", RegexOptions.IgnoreCase)]
    private static partial Regex AppContainerSidRegex();

    public static bool IsValidFamilyName(string? name) => name != null && FamilyNameRegex().IsMatch(name);

    /// <summary>
    /// The AppContainer SIDs in the output of "CheckNetIsolation LoopbackExempt -s". The labels in that output are
    /// translated on other Windows languages; the SIDs are not, so they are what is read.
    /// </summary>
    public static HashSet<string> ParseExemptSids(string? output)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(output))
        {
            return set;
        }
        foreach (Match m in AppContainerSidRegex().Matches(output))
        {
            set.Add(m.Value);
        }
        return set;
    }

    /// <summary>A name to show: the app's display name, or, when Windows gives only a resource reference, the part of the family name before the underscore.</summary>
    public static string FriendlyName(string? displayName, string familyName)
    {
        if (!string.IsNullOrWhiteSpace(displayName) && !displayName.StartsWith('@') && !displayName.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
        {
            return displayName.Trim();
        }
        var i = familyName.IndexOf('_');
        return i > 0 ? familyName[..i] : familyName;
    }

    /// <summary>
    /// The one command line (for cmd.exe /d /s /c) that applies all changes, so Windows shows a single administrator
    /// prompt. Null when there is nothing to do. Throws for a name that is not a valid package family name.
    /// </summary>
    public static string? BuildCommandLine(IEnumerable<string> add, IEnumerable<string> remove, string checkNetIsolationPath)
    {
        var commands = new List<string>();
        foreach (var n in add)
        {
            commands.Add(Command(checkNetIsolationPath, "-a", n));
        }
        foreach (var n in remove)
        {
            commands.Add(Command(checkNetIsolationPath, "-d", n));
        }
        return commands.Count == 0 ? null : $"/d /s /c \"{string.Join(" & ", commands)}\"";

        static string Command(string exe, string verb, string name)
        {
            if (!IsValidFamilyName(name))
            {
                throw new ArgumentException($"Not a package family name: {name}", nameof(name));
            }
            return $"\"{exe}\" LoopbackExempt {verb} -n={name}";
        }
    }

    [SupportedOSPlatform("windows")]
    private static string CheckNetIsolationPath => Path.Combine(Environment.SystemDirectory, "CheckNetIsolation.exe");

    /// <summary>The installed Store apps, with their current exemption state. Empty when not on Windows or when the tool is missing.</summary>
    public static List<LoopbackApp> ListApps()
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(CheckNetIsolationPath))
        {
            return [];
        }
        var exempt = ParseExemptSids(RunListing());
        var apps = new List<LoopbackApp>();
        using var mappings = Registry.CurrentUser.OpenSubKey(MappingsKey);
        if (mappings == null)
        {
            return apps;
        }
        foreach (var sid in mappings.GetSubKeyNames())
        {
            using var key = mappings.OpenSubKey(sid);
            var family = key?.GetValue("Moniker") as string;
            if (!IsValidFamilyName(family))
            {
                continue;
            }
            var display = FriendlyName(ResolveIndirect(key?.GetValue("DisplayName") as string), family!);
            apps.Add(new LoopbackApp(family!, display, sid, exempt.Contains(sid)));
        }
        return [.. apps.OrderBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase).ThenBy(a => a.FamilyName, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// Applies the changes (one administrator prompt unless Glide already runs as administrator) and returns how many of
    /// them took effect, read back from Windows afterwards. Zero when the prompt was declined.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static async Task<int> ApplyAsync(IReadOnlyCollection<string> add, IReadOnlyCollection<string> remove, CancellationToken cancellationToken = default)
    {
        var args = BuildCommandLine(add, remove, CheckNetIsolationPath);
        if (args == null)
        {
            return 0;
        }
        var before = ListApps().ToDictionary(a => a.FamilyName, a => a.Exempt, StringComparer.OrdinalIgnoreCase);
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), args)
        {
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        if (!Utils.IsAdministrator())
        {
            start.Verb = "runas";
        }
        try
        {
            using var process = Process.Start(start);
            if (process == null)
            {
                return 0;
            }
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return 0; // the administrator prompt was declined
        }
        var after = ListApps().ToDictionary(a => a.FamilyName, a => a.Exempt, StringComparer.OrdinalIgnoreCase);
        return add.Count(n => after.GetValueOrDefault(n) && !before.GetValueOrDefault(n))
               + remove.Count(n => !after.GetValueOrDefault(n, true) && before.GetValueOrDefault(n));
    }

    [SupportedOSPlatform("windows")]
    private static string? RunListing()
    {
        try
        {
            var start = new ProcessStartInfo(CheckNetIsolationPath, "LoopbackExempt -s")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(start);
            if (process == null)
            {
                return null;
            }
            var text = process.StandardOutput.ReadToEnd();
            process.WaitForExit(10_000);
            return text;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(LoopbackExempt), ex);
            return null;
        }
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHLoadIndirectString(string pszSource, StringBuilder pszOutBuf, int cchOutBuf, IntPtr ppvReserved);

    // Display names are often "@{Package?ms-resource://...}" references; Windows turns them into the text the Start menu shows.
    private static string? ResolveIndirect(string? value)
    {
        if (string.IsNullOrEmpty(value) || !value.StartsWith('@'))
        {
            return value;
        }
        try
        {
            var sb = new StringBuilder(260);
            return SHLoadIndirectString(value, sb, sb.Capacity, IntPtr.Zero) == 0 && sb.Length > 0 ? sb.ToString() : value;
        }
        catch (Exception)
        {
            return value;
        }
    }
}
