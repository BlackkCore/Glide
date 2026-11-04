namespace ServiceLib.Tests.Common;

public class GeoDataCheckTests
{
    // A geosite.dat / geoip.dat is a list of entries (field 1), each starting with its code (field 1).
    private static byte[] Dat(params string[] codes)
    {
        List<byte> data = [];
        foreach (var code in codes)
        {
            var codeBytes = Encoding.ASCII.GetBytes(code);
            // Entry body: the code, plus some filler bytes like a real list of domains would have.
            List<byte> entry = [0x0A, (byte)codeBytes.Length, .. codeBytes, 0x12, 0x03, 0x01, 0x02, 0x03];
            data.Add(0x0A);
            data.Add((byte)entry.Count);
            data.AddRange(entry);
        }
        return [.. data];
    }

    [Test]
    public async Task ReadCodes_ListsEveryEntry()
    {
        var codes = GeoDataCheck.ReadCodes(Dat("CN", "CATEGORY-IR", "PRIVATE"));
        await codes.Contains("cn").Should().BeTrue();
        await codes.Contains("category-ir").Should().BeTrue();
        await codes.Contains("ir").Should().BeFalse();
        await codes.Count.Should().BeEqualTo(3);
    }

    [Test]
    public async Task ExtractRefs_FindsSiteAndIpLists()
    {
        const string rules = """
            [{"domain":["geosite:ir","geosite:category-ads-all@ads","domain:example.com"]},
             {"ip":["geoip:!cn","geoip:private"]}]
            """;
        var refs = GeoDataCheck.ExtractRefs(rules);
        await refs.Should().Contain("geosite:ir");
        await refs.Should().Contain("geosite:category-ads-all");
        await refs.Should().Contain("geoip:cn");
        await refs.Should().Contain("geoip:private");
        await refs.Count.Should().BeEqualTo(4);
    }

    [Test]
    public async Task FindMissing_NamesOnlyWhatTheFilesLack()
    {
        var site = GeoDataCheck.ReadCodes(Dat("CN", "CATEGORY-IR"));
        var ip = GeoDataCheck.ReadCodes(Dat("IR", "PRIVATE"));
        var missing = GeoDataCheck.FindMissing(["geosite:ir", "geosite:cn", "geoip:ir", "geoip:private"], site, ip);
        await missing.Count.Should().BeEqualTo(1);
        await missing[0].Should().BeEqualTo("geosite:ir");
    }

    [Test]
    public async Task FindMissing_MissingFileMeansEverythingFromItIsMissing()
    {
        var missing = GeoDataCheck.FindMissing(["geosite:cn", "geoip:cn"], null, GeoDataCheck.ReadCodes(Dat("CN")));
        await missing.Count.Should().BeEqualTo(1);
        await missing[0].Should().BeEqualTo("geosite:cn");
    }

    [Test]
    public async Task InstallDownloadedFiles_InstallsWhatArrived_KeepsTheRest()
    {
        var dir = Path.Combine(Path.GetTempPath(), "glide-geo-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var okTemp = Path.Combine(dir, "ok.tmp");
            var okTarget = Path.Combine(dir, "geosite.dat");
            var failedTarget = Path.Combine(dir, "geosite-x.srs");
            File.WriteAllText(okTemp, "new");
            File.WriteAllText(failedTarget, "old");

            var failed = UpdateService.InstallDownloadedFiles(
            [
                (okTemp, okTarget),
                (Path.Combine(dir, "missing.tmp"), failedTarget),
            ]);

            await File.ReadAllText(okTarget).Should().BeEqualTo("new");
            await File.ReadAllText(failedTarget).Should().BeEqualTo("old");
            await File.Exists(okTemp).Should().BeFalse();
            await failed.Count.Should().BeEqualTo(1);
            await failed[0].Should().BeEqualTo("geosite-x.srs");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
