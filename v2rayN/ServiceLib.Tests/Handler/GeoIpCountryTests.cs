namespace ServiceLib.Tests.Handler;

public class GeoIpCountryTests
{
    private static readonly byte[] Marker = [0xAB, 0xCD, 0xEF, .. "MaxMind.com"u8];

    [Test]
    public async Task LookupIn_PointerBombInMetadata_EndsQuickly()
    {
        // S-10: a map of 28 entries whose values all point back at the map itself. Without a node
        // budget this decodes 28^16 values.
        var map = new List<byte> { 0xE0 | 28 };
        for (var i = 0; i < 28; i++)
        {
            map.AddRange([0x41, (byte)'a', 0x20, 0x00]);
        }
        var db = Marker.Concat(map).ToArray();

        var sw = Stopwatch.StartNew();
        var result = GeoIpCountry.LookupIn(db, "8.8.8.8");
        sw.Stop();

        await (result == null).Should().BeTrue();
        await (sw.Elapsed < TimeSpan.FromSeconds(5)).Should().BeTrue();
    }

    [Test]
    public async Task LookupIn_TreeLargerThanFile_IsRejected()
    {
        // node_count = 0xFFFFFFFF, record_size = 24: the tree would be far bigger than the file.
        byte[] meta =
        [
            0xE0 | 2,
            0x4A, .. "node_count"u8, 0xC4, 0xFF, 0xFF, 0xFF, 0xFF,
            0x4B, .. "record_size"u8, 0xA1, 24,
        ];
        var db = new byte[64].Concat(Marker).Concat(meta).ToArray();

        await (GeoIpCountry.LookupIn(db, "8.8.8.8") == null).Should().BeTrue();
    }

    [Test]
    public async Task LookupIn_Garbage_ReturnsNull()
    {
        var db = Marker.Concat(new byte[] { 0x5F, 0xFF, 0xFF, 0xFF }).ToArray();
        await (GeoIpCountry.LookupIn(db, "1.1.1.1") == null).Should().BeTrue();
    }
}
