namespace ServiceLib.Tests.Common;

/// <summary>
/// MaxMind's GeoLite EULA (section 3, updated 12 Feb 2026) asks for attribution to MaxMind wherever the data is used.
/// Glide downloads data built from GeoLite data, so the sentence must be in the app and in every document that credits it.
/// </summary>
public class GeoLiteAttributionTests
{
    private static string Text(params string[] path) => File.ReadAllText(Path.Combine([TestRepo.Root(), .. path]));

    [Test]
    public async Task TheAttributionSentence_IsTheOneMaxMindAsksFor()
    {
        await Global.GeoLiteAttribution.Should().BeEqualTo("This product includes GeoLite Data created by MaxMind, available from https://www.maxmind.com.");
    }

    [Test]
    public async Task TheSentence_IsInTheAboutScreen_TheNotices_AndBothReadmes()
    {
        await Text("v2rayN", "v2rayN.Desktop", "Views", "AboutSheet.cs").Contains("Global.GeoLiteAttribution").Should().BeTrue();
        await Text("THIRD_PARTY_NOTICES.md").Contains(Global.GeoLiteAttribution).Should().BeTrue();
        await Text("README.md").Contains(Global.GeoLiteAttribution).Should().BeTrue();
        await Text("README.fa.md").Contains(Global.GeoLiteAttribution).Should().BeTrue();
    }
}
