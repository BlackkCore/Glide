namespace ServiceLib.Handler.SysProxy;

public static class SysProxyHandler
{
    private static readonly string _tag = "SysProxyHandler";
    private static readonly Lazy<PacManager> _pacManager = new(() => new PacManager());

    public static async Task<bool> UpdateSysProxy(Config config, bool forceDisable)
    {
        var type = config.SystemProxyItem.SysProxyType;

        if (forceDisable && type != ESysProxyType.Unchanged)
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
            var allowed = !Utils.IsWindows() || IsOursToChange(type, port);
            if (!allowed)
            {
                type = ESysProxyType.Unchanged;
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
    [SupportedOSPlatform("windows")]
    private static bool IsOursToChange(ESysProxyType type, int port)
    {
        if (type == ESysProxyType.Unchanged)
        {
            return true;
        }
        var owner = PortOwner.Find(port);
        if (owner is { IsOurs: false })
        {
            Logging.SaveLog($"{_tag}: port {port} belongs to {owner.Name} (PID {owner.Pid}); leaving the Windows proxy unchanged");
            return false;
        }
        if (type == ESysProxyType.ForcedClear
            && !PortOwner.WindowsProxyPointsTo(port, AppManager.Instance.GetLocalPort(EInboundProtocol.pac)))
        {
            // Nothing of ours to clear: the Windows proxy is off or belongs to something else.
            return false;
        }
        return true;
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
