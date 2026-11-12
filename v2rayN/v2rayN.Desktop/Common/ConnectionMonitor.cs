using System.Diagnostics;

namespace v2rayN.Desktop.Common;

public enum ConnState
{
    NoServer,
    NotConnected,
    Checking,
    Connected,
    NoInternet,
    PortConflict,
    EngineStopped,
}

/// <summary>
/// What the connection card shows. Keeps three facts apart: the proxy engine is listening, Windows
/// (or TUN) is sending traffic to it, and a real request through it reached the internet. "Connected"
/// is only reported after that request succeeds; it is re-checked every minute while in use, and a
/// lost connection or a recovery is announced once.
/// </summary>
public sealed class ConnectionMonitor
{
    private static readonly TimeSpan CheckEvery = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromSeconds(8);

    // After a connect, a server change or a core reload, the core may need a while to listen again
    // (TUN can take several seconds). Until then "nothing on the port" means starting, not stopped.
    private static readonly TimeSpan StartGrace = TimeSpan.FromSeconds(12);
    private long _restartAt;

    // Proxy clients whose whole install (app plus cores) may be closed together to free the port.
    private static readonly string[] ProxyClients = ["v2rayn", "glide", "xray", "sing-box", "mihomo", "clash", "nekoray", "nekobox", "hiddify"];

    private readonly DispatcherTimer _timer = new();
    private int _generation;
    private int _failures;
    private bool _announcedLoss;
    private string? _serverId;

    public ConnState State { get; private set; } = ConnState.NotConnected;
    public int DelayMs { get; private set; }
    public string? ExitIp { get; private set; }
    public PortOwner.Owner? Conflict { get; private set; }
    public bool InUse { get; private set; }
    public bool HasServer { get; private set; }
    public string ServerName { get; private set; } = string.Empty;

    public event Action? Changed;

    public ConnectionMonitor()
    {
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            // A failed check still schedules the next one, so the card never stays on "Checking".
            CheckAsync().Forget(nameof(CheckAsync), _ => Schedule(TimeSpan.FromSeconds(15)));
        };
    }

    /// <summary>Called whenever the server, the proxy mode or TUN changes.</summary>
    public void Update(bool hasServer, bool inUse, string serverName, string? serverId)
    {
        var becameUsed = inUse && !InUse;
        var disconnected = !inUse && InUse;
        var serverChanged = serverId != _serverId || serverName != ServerName;
        HasServer = hasServer;
        InUse = inUse;
        ServerName = serverName;
        _serverId = serverId;

        if (!hasServer)
        {
            _timer.Stop();
            _generation++;
            ExitIp = null;
            DelayMs = 0;
            SetState(ConnState.NoServer);
            return;
        }
        if (becameUsed || serverChanged || disconnected)
        {
            ExpectRestart();
            _generation++;
            _failures = 0;
            _announcedLoss = false;
            ExitIp = null;
            DelayMs = 0;
            Conflict = null;
            SetState(inUse ? ConnState.Checking : ConnState.NotConnected);
        }
        CheckSoon(becameUsed || serverChanged ? 1500 : 300);
    }

    /// <summary>The core is being (re)started: give it time before calling it stopped.</summary>
    public void ExpectRestart()
    {
        _restartAt = Stopwatch.GetTimestamp();
    }

    private bool InStartGrace => _restartAt != 0 && Stopwatch.GetElapsedTime(_restartAt) < StartGrace;

    /// <summary>Re-check after a short pause (lets a core restart settle first).</summary>
    public void CheckSoon(int delayMs = 300)
    {
        Schedule(TimeSpan.FromMilliseconds(Math.Max(50, delayMs)));
    }

    private async Task CheckAsync()
    {
        var generation = ++_generation;
        if (!HasServer)
        {
            SetState(ConnState.NoServer);
            return;
        }

        var port = AppManager.Instance.GetLocalPort(EInboundProtocol.socks);
        var owner = await Task.Run(() => PortOwner.Find(port));
        if (generation != _generation)
        {
            return;
        }
        if (OperatingSystem.IsWindows())
        {
            if (owner is { IsOurs: false })
            {
                Conflict = owner;
                SetState(ConnState.PortConflict);
                Schedule(TimeSpan.FromSeconds(15));
                return;
            }
            Conflict = null;
            if (owner == null && InStartGrace)
            {
                // Still starting: keep showing Checking (or idle) and look again shortly.
                var waiting = InUse ? ConnState.Checking : ConnState.NotConnected;
                if (State != waiting)
                {
                    SetState(waiting);
                }
                Schedule(TimeSpan.FromSeconds(1));
                return;
            }
            if (owner == null)
            {
                SetState(ConnState.EngineStopped);
                Schedule(TimeSpan.FromSeconds(15));
                return;
            }
        }

        if (!InUse)
        {
            SetState(ConnState.NotConnected);
            return;
        }

        if (State is not ConnState.Connected and not ConnState.NoInternet)
        {
            SetState(ConnState.Checking);
        }
        var result = await Task.Run(ConnectionHandler.RunAvailabilityCheck);
        if (generation != _generation || !InUse)
        {
            return;
        }

        if (result.Time > 0)
        {
            var recovered = State == ConnState.NoInternet && _announcedLoss;
            DelayMs = result.Time;
            ExitIp = result.GetValidIp();
            _failures = 0;
            _announcedLoss = false;
            SetState(ConnState.Connected);
            if (recovered)
            {
                NoticeManager.Instance.Enqueue(ResUI.TipConnectionRestored);
            }
            Schedule(CheckEvery);
            return;
        }

        _failures++;
        if (_failures < 2)
        {
            // One miss can be a hiccup or a core still starting: try once more before alarming anyone.
            if (State != ConnState.Connected)
            {
                SetState(ConnState.Checking);
            }
            Schedule(RetryAfterFailure);
            return;
        }

        ExitIp = null;
        SetState(ConnState.NoInternet);
        if (!_announcedLoss)
        {
            _announcedLoss = true;
            NoticeManager.Instance.Enqueue(string.Format(ResUI.TipConnectionLost, ServerName));
        }
        Schedule(TimeSpan.FromSeconds(30));
    }

    private void Schedule(TimeSpan after)
    {
        _timer.Stop();
        _timer.Interval = after;
        _timer.Start();
    }

    private void SetState(ConnState state)
    {
        State = state;
        Changed?.Invoke();
    }

    /// <summary>Glide offers to close the port's owner only for normal programs, never Windows' own.</summary>
    public static bool CanClose(PortOwner.Owner owner)
    {
        if (owner.Path.IsNullOrEmpty())
        {
            return false;
        }
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        return windows.IsNullOrEmpty() || !owner.Path.StartsWith(windows, StringComparison.OrdinalIgnoreCase);
    }

    // S-07: the exact name, or the name plus a separated suffix ("clash-verge", "mihomo-windows-amd64");
    // a bare prefix also matched unrelated programs ("GlideRecorder", "xrayscan").
    private static bool IsProxyClient(string name)
    {
        return ProxyClients.Any(c => name.Equals(c, StringComparison.OrdinalIgnoreCase)
            || (name.Length > c.Length && name.StartsWith(c, StringComparison.OrdinalIgnoreCase) && name[c.Length] is '-' or '_' or ' ' or '.'));
    }

    /// <summary>
    /// The other programs <see cref="CloseOwner"/> would close with the owner: only proxy clients and their
    /// cores (xray, sing-box, mihomo…) started from the same install folder, never anything else there.
    /// </summary>
    public static List<string> CompanionNames(PortOwner.Owner owner)
    {
        var names = new List<string>();
        ForEachCompanion(owner, process => names.Add(process.ProcessName));
        return names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Closes the program holding Glide's port. For a known proxy client this also closes the proxy
    /// clients and cores started from its install folder (they keep the port otherwise). Other programs
    /// in that folder are never touched, and folders like Desktop or Downloads never count as an install.
    /// </summary>
    public static int CloseOwner(PortOwner.Owner owner)
    {
        if (!CanClose(owner))
        {
            return 0;
        }
        var closed = 0;
        void Kill(Process process)
        {
            try
            {
                process.Kill();
                process.WaitForExit(2000);
                closed++;
            }
            catch
            {
                // Already gone, or not ours to close.
            }
        }
        try
        {
            using var main = Process.GetProcessById(owner.Pid);
            Kill(main);
        }
        catch
        {
            // Already gone.
        }
        ForEachCompanion(owner, Kill);
        return closed;
    }

    private static void ForEachCompanion(PortOwner.Owner owner, Action<Process> action)
    {
        if (!CanClose(owner) || !IsProxyClient(owner.Name) || InstallRoot(owner.Path) is not { } root || IsSharedFolder(root))
        {
            return;
        }
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.Id == Environment.ProcessId || process.Id == owner.Pid || !IsProxyClient(process.ProcessName))
                {
                    continue;
                }
                var path = process.MainModule?.FileName;
                if (path != null && path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    action(process);
                }
            }
            catch
            {
                // Not ours to inspect, or already gone.
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    // Folders that hold all sorts of programs: a proxy client sitting directly in one isn't an install
    // folder, so nothing else started from there is closed with it.
    private static bool IsSharedFolder(string root)
    {
        var dir = Path.TrimEndingDirectorySeparator(root);
        if (Path.GetPathRoot(dir) is { } drive && string.Equals(Path.TrimEndingDirectorySeparator(drive), dir, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        string[] shared =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            // S-07: per-user installs (Glide's own default) all live side by side here.
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
            Path.GetTempPath(),
            downloads,
        ];
        return shared.Any(s => s.IsNotEmpty() && string.Equals(Path.TrimEndingDirectorySeparator(s), dir, StringComparison.OrdinalIgnoreCase));
    }

    // C:\x\v2rayN-windows-64\v2rayN.exe -> C:\x\v2rayN-windows-64\ ; ...\bin\xray\xray.exe -> the folder holding bin.
    public static string? InstallRoot(string? exePath)
    {
        if (exePath.IsNullOrEmpty())
        {
            return null;
        }
        var dir = Path.GetDirectoryName(exePath);
        if (dir == null)
        {
            return null;
        }
        var marker = $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}";
        var withSlash = dir + Path.DirectorySeparatorChar;
        var binIndex = withSlash.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (binIndex > 0)
        {
            withSlash = withSlash[..(binIndex + 1)];
        }
        return withSlash;
    }
}
