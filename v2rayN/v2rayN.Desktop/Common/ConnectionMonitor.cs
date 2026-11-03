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

    // Proxy clients whose whole install (app plus cores) may be closed together to free the port.
    private static readonly string[] ProxyClients = ["v2rayn", "glide", "xray", "sing-box", "mihomo", "clash", "nekoray", "nekobox", "hiddify"];

    private readonly DispatcherTimer _timer = new();
    private int _generation;
    private int _failures;
    private bool _announcedLoss;

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
            _ = CheckAsync();
        };
    }

    /// <summary>Called whenever the server, the proxy mode or TUN changes.</summary>
    public void Update(bool hasServer, bool inUse, string serverName)
    {
        var becameUsed = inUse && !InUse;
        var serverChanged = serverName != ServerName;
        HasServer = hasServer;
        InUse = inUse;
        ServerName = serverName;

        if (!hasServer)
        {
            _timer.Stop();
            _generation++;
            SetState(ConnState.NoServer);
            return;
        }
        if (becameUsed || serverChanged)
        {
            _failures = 0;
            _announcedLoss = false;
        }
        CheckSoon(becameUsed || serverChanged ? 1500 : 300);
    }

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

    private static bool IsProxyClient(string name)
    {
        return ProxyClients.Any(c => name.StartsWith(c, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Closes the program holding Glide's port. For a known proxy client this includes everything
    /// started from its install folder (its cores keep the port otherwise); anything else is closed alone.
    /// </summary>
    public static int CloseOwner(PortOwner.Owner owner)
    {
        if (!CanClose(owner))
        {
            return 0;
        }
        var root = IsProxyClient(owner.Name) ? InstallRoot(owner.Path) : null;
        var closed = 0;
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.Id == Environment.ProcessId)
                {
                    continue;
                }
                var match = process.Id == owner.Pid;
                if (!match && root != null)
                {
                    var path = process.MainModule?.FileName;
                    match = path != null && path.StartsWith(root, StringComparison.OrdinalIgnoreCase);
                }
                if (match)
                {
                    process.Kill();
                    process.WaitForExit(2000);
                    closed++;
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
        return closed;
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
