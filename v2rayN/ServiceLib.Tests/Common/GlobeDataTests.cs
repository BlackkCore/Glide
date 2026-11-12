namespace ServiceLib.Tests.Common;

/// <summary>
/// The globe's land dots come from Natural Earth (public domain) through scripts/globe/gen-land.mjs, and the shipped notices
/// keep saying plainly which licensing points are NOT resolved. Only the geographic data is guarded here; the globe's code,
/// animation and design are not touched by these tests.
/// </summary>
public class GlobeDataTests
{
    private static string Root(params string[] parts) => Path.Combine([TestRepo.Root(), .. parts]);

    [Test]
    public async Task ShippedGlobeData_IsExactlyTheGeneratorOutputFromNaturalEarth()
    {
        var asset = File.ReadAllText(Root("v2rayN", "v2rayN.Desktop", "Assets", "globe-land.txt")).Trim();
        var generated = File.ReadAllText(Root("scripts", "globe", "globe-land.naturalearth.txt")).Trim();
        await asset.Should().BeEqualTo(generated);
        var script = File.ReadAllText(Root("scripts", "globe", "gen-land.mjs"));
        await script.Contains("Natural Earth").Should().BeTrue();
        await script.Contains("public domain").Should().BeTrue();
    }

    [Test]
    public async Task GlobeData_DecodesToTheGridTheGlobeReads_WithRealisticLandShare()
    {
        var bits = Convert.FromBase64String(File.ReadAllText(Root("v2rayN", "v2rayN.Desktop", "Assets", "globe-land.txt")).Trim());
        // 180 latitude rows of round(360 * cos(latitude)) cells, one bit each (the layout GlobeViewControl reads)
        var cells = 0;
        for (var row = 0; row < 180; row++)
        {
            cells += Math.Max(1, (int)Math.Floor((360 * Math.Cos((row - 89.5) * Math.PI / 180)) + 0.5));
        }
        await cells.Should().BeEqualTo(41252);
        await bits.Length.Should().BeEqualTo((cells + 7) / 8);

        var land = 0;
        for (var i = 0; i < cells; i++)
        {
            if ((bits[i >> 3] & (1 << (i % 8))) != 0)
            {
                land++;
            }
        }
        var share = land / (double)cells;
        await (share is > 0.25 and < 0.33).Should().BeTrue();
    }

    [Test]
    public async Task ShippedNotices_CreditNaturalEarth_AndStateWhatIsStillUnresolved()
    {
        var notices = File.ReadAllText(Root("THIRD_PARTY_NOTICES.md"));
        await notices.Contains("Natural Earth").Should().BeTrue();
        await notices.Contains("Unresolved licensing points").Should().BeTrue();
        // attribution is never presented as settling the open points
        await notices.Contains("do **not** by themselves resolve").Should().BeTrue();
        await notices.Contains("has **not been confirmed**").Should().BeTrue();
        foreach (var topic in new[] { "GeoLite-derived data", "PAC domain list", "sing-box rule sets" })
        {
            await notices.Contains(topic).Should().BeTrue();
        }
    }
}
