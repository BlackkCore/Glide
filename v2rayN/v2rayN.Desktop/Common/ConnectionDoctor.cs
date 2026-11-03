using System.Net;
using System.Net.Sockets;

namespace v2rayN.Desktop.Common;

/// <summary>
/// "Why can't I connect?": tests the chain one link at a time (the user's own internet, DNS, Glide's
/// engine and port, the server, traffic through the server) and ends with one plain answer.
/// Read-only: it never changes the proxy, the engine or the server.
/// </summary>
public static class ConnectionDoctor
{
    public enum Outcome { Ok, Failed, Skipped }

    public sealed record Step(string Name, Outcome Outcome, string Detail);

    // Several targets, so one blocked address doesn't read as "no internet".
    private static readonly (string Host, int Port)[] InternetProbes =
    [
        ("1.1.1.1", 443), ("8.8.8.8", 443), ("9.9.9.9", 443), ("www.msftconnecttest.com", 80),
    ];

    // These carry their traffic over UDP: a TCP connect to them says nothing.
    private static readonly EConfigType[] UdpTypes =
        [EConfigType.Hysteria2, EConfigType.TUIC, EConfigType.WireGuard, EConfigType.MASQUE];

    /// <summary>Runs every step, reporting each as it finishes; returns the verdict.</summary>
    public static async Task<string> RunAsync(Action<Step> report, CancellationToken token)
    {
        // 1. The user's own internet, without Glide.
        var internet = false;
        foreach (var (host, port) in InternetProbes)
        {
            if (await CanConnectAsync(host, port, token))
            {
                internet = true;
                break;
            }
        }
        report(new(ResUI.TbDiagInternet, internet ? Outcome.Ok : Outcome.Failed, string.Empty));
        if (!internet)
        {
            return ResUI.TipDiagInternetDown;
        }

        // 2. DNS without the proxy (only noted; traffic through Glide may still work).
        var dns = await ResolvesAsync("www.google.com", token);
        report(new(ResUI.TbDiagDns, dns ? Outcome.Ok : Outcome.Failed, string.Empty));

        // 3. Glide's engine on its port.
        var localPort = AppManager.Instance.GetLocalPort(EInboundProtocol.socks);
        var owner = OperatingSystem.IsWindows() ? await Task.Run(() => PortOwner.Find(localPort), token) : null;
        if (OperatingSystem.IsWindows() && owner is not { IsOurs: true })
        {
            var why = owner == null
                ? ResUI.TipEngineStoppedHint
                : string.Format(ResUI.TipPortBusy, localPort, owner.Name);
            report(new(ResUI.TbDiagEngine, Outcome.Failed, why));
            return why;
        }
        report(new(ResUI.TbDiagEngine, Outcome.Ok, string.Empty));

        // 4. The server itself, directly.
        var server = await AppManager.Instance.GetProfileItem(AppManager.Instance.Config.IndexId);
        if (server == null)
        {
            report(new(ResUI.TbDiagServer, Outcome.Failed, ResUI.TipAddServerToConnect));
            return ResUI.TipAddServerToConnect;
        }
        bool? reachable = null;
        if (server.ConfigType is EConfigType.PolicyGroup or EConfigType.ProxyChain)
        {
            // Auto and other groups have no single address; the traffic step tests them.
            report(new(ResUI.TbDiagServer, Outcome.Skipped, ResUI.TipDiagGroupSkip));
        }
        else if (UdpTypes.Contains(server.ConfigType) || server.Address.IsNullOrEmpty() || server.Port <= 0)
        {
            report(new(ResUI.TbDiagServer, Outcome.Skipped, ResUI.TipDiagUdpSkip));
        }
        else
        {
            reachable = await CanConnectAsync(server.Address, server.Port, token);
            report(new(ResUI.TbDiagServer, reachable == true ? Outcome.Ok : Outcome.Failed, string.Empty));
        }

        // 5. Real traffic through the server (the same check as the status pill).
        var result = await Task.Run(ConnectionHandler.RunAvailabilityCheck, token);
        var through = result.Time > 0;
        report(new(ResUI.TbDiagThrough, through ? Outcome.Ok : Outcome.Failed,
            through ? $"‎{result.Time} ms‎" : string.Empty));

        if (!through)
        {
            return reachable == false ? ResUI.TipDiagServerDown : ResUI.TipDiagNoTraffic;
        }

        // 6. The address sites see, with its country. Only with the IP lookup service the user chose
        // in Settings (off by default): a lookup contacts that third-party site.
        if (AppManager.Instance.Config.SpeedTestItem.IPAPIUrl.IsNullOrEmpty())
        {
            report(new(ResUI.TbExitIp, Outcome.Skipped, ResUI.TipDiagExitIpOff));
        }
        else
        {
            var exit = await ConnectionHandler.GetIPInfo(new WebProxy($"socks5://{Global.Loopback}:{localPort}"), token);
            report(new(ResUI.TbExitIp, exit != null ? Outcome.Ok : Outcome.Failed,
                exit != null ? $"‎{exit}‎" : string.Empty));
        }
        return dns ? ResUI.TipDiagAllGood : ResUI.TipDiagDnsDown;
    }

    public sealed record DnsLeakResult(bool Done, IReadOnlyList<string> Servers, string? Conclusion);

    /// <summary>
    /// DNS leak test with bash.ws, all through the proxy: get a test id, make ten unique names that
    /// only the far side of the tunnel resolves, then ask bash.ws which DNS servers looked them up.
    /// Contacts bash.ws (the button says so).
    /// </summary>
    public static async Task<DnsLeakResult> CheckDnsLeakAsync(CancellationToken token)
    {
        var port = AppManager.Instance.GetLocalPort(EInboundProtocol.socks);
        using var handler = new SocketsHttpHandler { Proxy = new WebProxy($"socks5://{Global.Loopback}:{port}"), UseProxy = true };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        try
        {
            var id = (await client.GetStringAsync("https://bash.ws/id", token)).Trim();
            if (!id.All(char.IsDigit) || id.Length == 0)
            {
                return new(false, [], null);
            }
            // The requests themselves fail; looking the names up is what counts.
            await Task.WhenAll(Enumerable.Range(1, 10).Select(async i =>
            {
                try
                {
                    using var _ = await client.GetAsync($"https://{i}.{id}.bash.ws", token);
                }
                catch (Exception) when (!token.IsCancellationRequested)
                {
                }
            }));
            var nodes = JsonUtils.ParseJson(await client.GetStringAsync($"https://bash.ws/dnsleak/test/{id}?json", token))?.AsArray();
            var servers = new List<string>();
            string? conclusion = null;
            foreach (var node in nodes ?? [])
            {
                var type = node?["type"]?.ToString();
                if (type == "dns")
                {
                    var asn = node?["asn"]?.ToString();
                    servers.Add($"‎{node?["ip"]}‎ · {node?["country_name"]}{(asn.IsNotEmpty() ? $" · {asn}" : "")}");
                }
                else if (type == "conclusion")
                {
                    conclusion = node?["ip"]?.ToString();
                }
            }
            return new(servers.Count > 0, servers, conclusion);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new(false, [], null);
        }
    }

    private static async Task<bool> CanConnectAsync(string host, int port, CancellationToken token)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, timeout.Token);
            return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> ResolvesAsync(string host, CancellationToken token)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            return (await Dns.GetHostAddressesAsync(host, timeout.Token)).Length > 0;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }
}
