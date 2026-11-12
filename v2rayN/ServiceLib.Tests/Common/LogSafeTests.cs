namespace ServiceLib.Tests.Common;

public class LogSafeTests
{
    [Test]
    [Arguments("https://sub.example.com/api/v1/client/subscribe?token=SECRET", "https://sub.example.com/…")]
    [Arguments("https://user:pass@example.org/SECRETPATH", "https://example.org/…")]
    [Arguments("not a url", "…")]
    [Arguments("", "")]
    public async Task Url_KeepsOnlySchemeAndHost(string input, string expected)
    {
        await LogSafe.Url(input).Should().BeEqualTo(expected);
    }

    [Test]
    public async Task Text_RemovesTokensFromEveryAddress()
    {
        var text = LogSafe.Text("failed: https://a.example/x?token=SECRET1 then http://b.example/SECRET2/sub done");
        await text.Contains("SECRET").Should().BeFalse();
        await text.Should().BeEqualTo("failed: https://a.example/… then http://b.example/… done");
    }

    [Test]
    [Arguments("vless://0f3c1d2e-aaaa-bbbb-cccc-0123456789ab@host.example:443?security=reality#DE", "vless://…")]
    [Arguments("trojan://PASSWORD@host.example:443#x", "trojan://…")]
    [Arguments("ss://YWVzLTI1Ni1nY206cGFzc3dvcmQ@1.2.3.4:8388#y", "ss://…")]
    [Arguments("hysteria2://pw@h:1?sni=a#z", "hysteria2://…")]
    public async Task Text_ShareLinks_KeepOnlyScheme(string link, string expected)
    {
        await LogSafe.Text($"bad: {link} end").Should().BeEqualTo($"bad: {expected} end");
    }

    [Test]
    public async Task Text_UuidsAndBase64Runs_AreHidden()
    {
        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("vmess://{\"id\":\"secret-user\",\"add\":\"host.example\"}"));
        var text = LogSafe.Text($"id 0f3c1d2e-aaaa-bbbb-cccc-0123456789ab body {b64} done");
        await text.Should().BeEqualTo("id ••• body ••• done");
    }

    [Test]
    public async Task Text_OrdinaryLogLines_StayReadable()
    {
        const string line = "Core started: xray 25.1.1, port 10808, 127.0.0.1, C:\\Glide\\bin\\xray\\xray.exe";
        await LogSafe.Text(line).Should().BeEqualTo(line);
    }
}
