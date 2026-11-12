namespace ServiceLib.Handler.SysProxy;

public static class SysProxyHandler
{
    private static readonly string _tag = "SysProxyHandler";
    private static readonly Lazy<PacManager> _pacManager = new(() => new PacManager());

    public static async Task<bool> UpdateSysProxy(Config config, bool forceDisable)
    {
        var type = config.SystemProxyItem.SysProxyType;

        // Glide (F-02): disabling (exit) clears even under "Unchanged" — the ownership check below
        // only lets it clear a Windows proxy that points at our own port, which is dead once we quit.
        if (forceDisable && (type != ESysProxyType.Unchanged || Utils.IsWindows()))
        {
            type = ESysProxyType.ForcedClear;
        }

        try
        {
            var port = AppManager.Instance.GetLocalPort(EInboundProtocol.socks);
            if (port <= 0)
            {
                return false;
            }
            var allowed = true;
            if (OperatingSystem.IsWindows() && type != ESysProxyType.Unchanged)
            {
                (type, allowed) = Decide(type, PortIsForeign(port), PortOwner.WindowsProxyPointsTo(port, AppManager.Instance.GetLocalPort(EInboundProtocol.pac)));
            }
            switch (type)
            {
                case ESysProxyType.ForcedChange when Utils.IsWindows():
                    var (strProxy, strExceptions) = GetWindowsProxyString(config, port);
                    ProxySettingWindows.SetProxy(strProxy, strExceptions, 2);
                    break;

                case ESysProxyType.ForcedChange when Utils.IsLinux():
                    var exceptions = SanitizeExceptions(config);
                    await ProxySettingLinux.SetProxy(Global.Loopback, port, exceptions);
                    break;

                case ESysProxyType.ForcedChange when Utils.IsMacOS():
                    var exceptions2 = SanitizeExceptions(config);
                    await ProxySettingOSX.SetProxy(Global.Loopback, port, exceptions2);
                    break;

                case ESysProxyType.ForcedClear when Utils.IsWindows():
                    ProxySettingWindows.UnsetProxy();
                    break;

                case ESysProxyType.ForcedClear when Utils.IsLinux():
                    await ProxySettingLinux.UnsetProxy();
                    break;

                case ESysProxyType.ForcedClear when Utils.IsMacOS():
                    await ProxySettingOSX.UnsetProxy();
                    break;

                case ESysProxyType.Pac when Utils.IsWindows():
                    await SetWindowsProxyPac(port);
                    break;
            }

            if (type != ESysProxyType.Pac && Utils.IsWindows())
            {
                _pacManager.Value.Stop();
            }
            return allowed;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
        return true;
    }

    /// <summary>
    /// Proxy ownership (Glide): never point Windows at a port another program is listening on, and
    /// only clear the Windows proxy when it currently points at this app's own port. This keeps a
    /// second client (or another copy) from hijacking or wiping a working connection.
    /// </summary>
    /// <returns>The type to apply (Unchanged when refused) and whether the requested change was allowed.</returns>
    public static (ESysProxyType Type, bool Allowed) Decide(ESysProxyType type, bool portIsForeign, bool proxyPointsToUs)
    {
        if (type == ESysProxyType.Unchanged)
        {
            return (type, true);
        }
        if (portIsForeign)
        {
            return (ESysProxyType.Unchanged, false);
        }
        if (type == ESysProxyType.ForcedClear && !proxyPointsToUs)
        {
            // Nothing of ours to clear: the Windows proxy is off or belongs to something else.
            return (ESysProxyType.Unchanged, false);
        }
        return (type, true);
    }

    [SupportedOSPlatform("windows")]
    private static bool PortIsForeign(int port)
    {
        var owner = PortOwner.Find(port);
        if (owner is { IsOurs: false })
        {
            Logging.SaveLog($"{_tag}: port {port} belongs to {owner.Name} (PID {owner.Pid}); leaving the Windows proxy unchanged");
            return true;
        }
        return false;
    }

    /// <summary>
    /// Glide (F-04): from the crash handler, synchronously and best effort, take back a Windows proxy
    /// that points at our port, so a crash doesn't leave the PC pointed at a dead port. A forced kill
    /// or power loss can't run this; the next start clears it instead.
    /// </summary>
    public static void ClearOwnProxyNow()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        try
        {
            var port = AppManager.Instance.GetLocalPort(EInboundProtocol.socks);
            if (port > 0 && Decide(ESysProxyType.ForcedClear, PortIsForeign(port), PortOwner.WindowsProxyPointsTo(port, AppManager.Instance.GetLocalPort(EInboundProtocol.pac))).Allowed)
            {
                ProxySettingWindows.UnsetProxy();
            }
        }
        catch
        {
            // Already crashing: nothing else to do.
        }
    }

    private static string SanitizeExceptions(Config config)
    {
        var exceptions = config.SystemProxyItem.SystemProxyExceptions;
        if (exceptions.IsNullOrEmpty())
        {
            return string.Empty;
        }

        var items = exceptions
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Replace(" ", string.Empty))
            .Where(item => item.Length > 0);

        return string.Join(',', items);
    }

    private static (string strProxy, string strExceptions) GetWindowsProxyString(Config config, int port)
    {
        var strExceptions = config.SystemProxyItem.SystemProxyExceptions.Replace(" ", "");
        if (config.SystemProxyItem.NotProxyLocalAddress)
        {
            strExceptions = $"<local>;{strExceptions}";
        }

        var strProxy = string.Empty;
        if (config.SystemProxyItem.SystemProxyAdvancedProtocol.IsNullOrEmpty())
        {
            strProxy = $"{Global.Loopback}:{port}";
        }
        else
        {
            strProxy = config.SystemProxyItem.SystemProxyAdvancedProtocol
                .Replace("{ip}", Global.Loopback)
                .Replace("{http_port}", port.ToString())
                .Replace("{socks_port}", port.ToString());
        }

        return (strProxy, strExceptions);
    }

    [SupportedOSPlatform("windows")]
    private static async Task SetWindowsProxyPac(int port)
    {
        var portPac = AppManager.Instance.GetLocalPort(EInboundProtocol.pac);
        await _pacManager.Value.StartAsync(port, portPac);
        var strProxy = $"{Global.HttpProtocol}{Global.Loopback}:{portPac}/pac?t={DateTime.Now.Ticks}";
        ProxySettingWindows.SetProxy(strProxy, "", 4);
    }
}
