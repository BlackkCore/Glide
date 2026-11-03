namespace ServiceLib.Tests.Common;

public class PortOwnerTests
{
    [Test]
    public async Task Find_ReportsThisProcessForItsOwnListener()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var owner = PortOwner.Find(port);
            await (owner?.Pid ?? -1).Should().BeEqualTo(Environment.ProcessId);
            await (owner?.IsOurs ?? false).Should().BeTrue();
        }
        finally
        {
            listener.Stop();
        }
    }

    [Test]
    public async Task Find_ReturnsNullForAFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        await (PortOwner.Find(port) == null).Should().BeTrue();
    }

    [Test]
    [Arguments("127.0.0.1:10808", 10808, true)]
    [Arguments("http=127.0.0.1:10808;https=127.0.0.1:10808", 10808, true)]
    [Arguments("http://127.0.0.1:10809/pac?t=1", 10809, true)]
    [Arguments("localhost:10808", 10808, true)]
    [Arguments("127.0.0.1:108080", 10808, false)]
    [Arguments("127.0.0.1:20808", 10808, false)]
    [Arguments("proxy.corp.example:10808", 10808, false)]
    [Arguments("", 10808, false)]
    public async Task IsLoopbackPort_MatchesOnlyThisLocalPort(string value, int port, bool expected)
    {
        await PortOwner.IsLoopbackPort(value, port).Should().BeEqualTo(expected);
    }
}
