namespace ServiceLib.Tests.Common;

public class RoutingNameTests
{
    [Test]
    [Arguments("V4-绕过大陆(Whitelist)", "Whitelist")]
    [Arguments("V4-黑名单(Blacklist)", "Blacklist")]
    [Arguments("V4-全局(Global)", "Global")]
    [Arguments("V3-全局（Global）", "Global")]
    [Arguments("My rules", "My rules")]
    [Arguments("Iran direct (custom)", "Iran direct (custom)")]
    [Arguments("ایران مستقیم", "ایران مستقیم")]
    [Arguments("全局 Global", "Global")]
    [Arguments("全局", "全局")]
    [Arguments("", "")]
    public async Task Display_RemovesChinese(string remarks, string expected)
    {
        await RoutingName.Display(remarks).Should().BeEqualTo(expected);
    }
}
