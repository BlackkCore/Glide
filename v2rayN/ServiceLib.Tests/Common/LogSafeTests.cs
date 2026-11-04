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
}
