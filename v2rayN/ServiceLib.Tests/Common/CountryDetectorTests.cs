namespace ServiceLib.Tests.Common;

public class CountryDetectorTests
{
    [Test]
    [Arguments("🇩🇪 Germany 01", "DE")]
    [Arguments("Fast 🇯🇵 node", "JP")]
    [Arguments("Frankfurt 02", "DE")]
    [Arguments("Amsterdam edge", "NL")]
    [Arguments("hong kong-03", "HK")]
    [Arguments("United States | Los Angeles", "US")]
    [Arguments("NL-ams-2", "NL")]
    [Arguments("UK London", "GB")]
    [Arguments("UK-premium", "GB")]
    [Arguments("سرور آلمان ۲", "DE")]
    [Arguments("ترکيه 1", "TR")]
    [Arguments("هنگ کنگ", "HK")]
    public async Task Detect_FindsCountry(string remarks, string expected)
    {
        await CountryDetector.Detect(remarks).Should().BeEqualTo(expected);
    }

    [Test]
    [Arguments("")]
    [Arguments("My server")]
    [Arguments("Go to IN-house")]
    [Arguments("vless reality 443")]
    public async Task Detect_ReturnsNullWithoutACountry(string remarks)
    {
        await CountryDetector.Detect(remarks).Should().BeNull();
    }

    [Test]
    public async Task RemoveFlags_RemovesFlagsAnywhere()
    {
        await CountryDetector.RemoveFlags("Turkey 🇹🇷 | 6.92GB").Should().BeEqualTo("Turkey | 6.92GB");
        await CountryDetector.RemoveFlags("🇩🇪 Germany 🇩🇪").Should().BeEqualTo("Germany");
        await CountryDetector.RemoveFlags("Plain name").Should().BeEqualTo("Plain name");
        await CountryDetector.RemoveFlags("🇩🇪").Should().BeEqualTo("🇩🇪");
    }

    [Test]
    public async Task StripLeadingFlag_RemovesOnlyALeadingFlag()
    {
        await CountryDetector.StripLeadingFlag("🇩🇪 Germany 01").Should().BeEqualTo("Germany 01");
        await CountryDetector.StripLeadingFlag("Germany 🇩🇪").Should().BeEqualTo("Germany 🇩🇪");
        await CountryDetector.StripLeadingFlag("🇩🇪").Should().BeEqualTo("🇩🇪");
    }
}
