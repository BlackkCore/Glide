using Microsoft.Win32;

namespace ServiceLib.Common;

/// <summary>
/// Finds which process is listening on a local TCP port, and whether it belongs to this install
/// (Glide itself or a core started from its folder). Used to keep Glide from steering Windows at a
/// port another app owns, and to explain "port in use" failures. Windows only; elsewhere the
/// lookups report nothing.
/// </summary>
public static class PortOwner
{
    public sealed record Owner(int Pid, string Name, string? Path, bool IsOurs);

    public static Owner? Find(int port)
    {
        if (!OperatingSystem.IsWindows() || port <= 0)
        {
            return null;
        }
        return FindListeningPid(port) is { } pid ? Describe(pid) : null;
    }

    public static Owner Describe(int pid)
    {
        var name = $"PID {pid}";
        string? path = null;
        try
        {
            using var process = Process.GetProcessById(pid);
            name = process.ProcessName;
            path = process.MainModule?.FileName;
        }
        catch
        {
            // Elevated or already-exited processes can't be inspected; they are not ours.
        }
        return new Owner(pid, name, path, IsOurProcess(pid, path));
    }

    private static bool IsOurProcess(int pid, string? path)
    {
        if (pid == Environment.ProcessId)
        {
            return true;
        }
        if (path.IsNullOrEmpty())
        {
            return false;
        }
        // S-01: installed, Glide's cores run from Program Files, not from the data folder.
        var root = GlidePaths.OwnProcessRoot.TrimEnd('\\', '/') + System.IO.Path.DirectorySeparatorChar;
        return path.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when the Windows proxy currently points at this local port (manual) or PAC port.</summary>
    [SupportedOSPlatform("windows")]
    public static bool WindowsProxyPointsTo(int port, int pacPort)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings", false);
            if (key == null)
            {
                return false;
            }
            var enabled = key.GetValue("ProxyEnable") is int e && e == 1;
            var server = key.GetValue("ProxyServer") as string ?? string.Empty;
            var pac = key.GetValue("AutoConfigURL") as string ?? string.Empty;
            return (enabled && IsLoopbackPort(server, port)) || (pacPort > 0 && IsLoopbackPort(pac, pacPort));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>"127.0.0.1:10808", "http=127.0.0.1:10808;https=…" or "http://127.0.0.1:10809/pac" on exactly this port.</summary>
    public static bool IsLoopbackPort(string? value, int port)
    {
        return value.IsNotEmpty()
               && Regex.IsMatch(value, $@"(127\.0\.0\.1|localhost|\[::1\]):{port}(?!\d)", RegexOptions.IgnoreCase);
    }

    private const int AF_INET = 2;
    private const int AF_INET6 = 23;
    private const int TCP_TABLE_OWNER_PID_LISTENER = 3;

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr pTcpTable, ref int pdwSize, bool bOrder, int ulAf, int tableClass, uint reserved);

    private static int? FindListeningPid(int port)
    {
        return FindIn(AF_INET, port, rowSize: 24, portOffset: 8, pidOffset: 20)
               ?? FindIn(AF_INET6, port, rowSize: 56, portOffset: 20, pidOffset: 52);
    }

    // MIB_TCPROW_OWNER_PID (24 bytes): state, localAddr, localPort, remoteAddr, remotePort, pid.
    // MIB_TCP6ROW_OWNER_PID (56 bytes): localAddr[16], scope, localPort, remoteAddr[16], scope, remotePort, state, pid.
    private static int? FindIn(int family, int port, int rowSize, int portOffset, int pidOffset)
    {
        var size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, family, TCP_TABLE_OWNER_PID_LISTENER, 0);
        if (size <= 0)
        {
            return null;
        }
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(buffer, ref size, false, family, TCP_TABLE_OWNER_PID_LISTENER, 0) != 0)
            {
                return null;
            }
            var count = Marshal.ReadInt32(buffer);
            for (var i = 0; i < count; i++)
            {
                var row = buffer + 4 + (i * rowSize);
                var raw = (uint)Marshal.ReadInt32(row + portOffset);
                var localPort = (int)(((raw & 0xFF) << 8) | ((raw >> 8) & 0xFF));
                if (localPort == port)
                {
                    return Marshal.ReadInt32(row + pidOffset);
                }
            }
            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
