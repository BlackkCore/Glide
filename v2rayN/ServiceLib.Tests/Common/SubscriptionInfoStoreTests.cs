namespace ServiceLib.Tests.Common;

public class SubscriptionInfoStoreTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);

    [Test]
    public async Task Parse_ReadsAllFields()
    {
        var info = SubscriptionInfoStore.Parse("upload=1073741824; download=2147483648; total=10737418240; expire=1900000000", Now);
        await (info?.Used ?? 0).Should().BeEqualTo(3221225472L);
        await (info?.Left ?? 0).Should().BeEqualTo(7516192768L);
        await (info?.Expire ?? 0).Should().BeEqualTo(1900000000L);
        await (info?.UpdatedUnix ?? 0).Should().BeEqualTo(1_800_000_000L);
    }

    [Test]
    [Arguments("")]
    [Arguments("upload=0; download=0; total=0; expire=0")]
    [Arguments("garbage")]
    public async Task Parse_ReturnsNullWithoutUsefulData(string header)
    {
        await (SubscriptionInfoStore.Parse(header, Now) == null).Should().BeTrue();
    }

    [Test]
    public async Task Parse_ToleratesSpacingAndOrder()
    {
        var info = SubscriptionInfoStore.Parse("total = 100;expire=5, download=40 ; upload=10", Now);
        await (info?.Left ?? -1).Should().BeEqualTo(50L);
        await (info?.Expire ?? 0).Should().BeEqualTo(5L);
    }

    [Test]
    public async Task Left_IsNeverNegative()
    {
        var info = SubscriptionInfoStore.Parse("upload=80; download=80; total=100", Now);
        await (info?.Left ?? -1).Should().BeEqualTo(0L);
    }

    [Test]
    [NotInParallel(nameof(SubscriptionInfoStore))]
    public async Task Record_SurvivesAReloadFromTheFile()
    {
        var file = Path.Combine(Path.GetTempPath(), $"glide-subinfo-{Guid.NewGuid():N}.json");
        var original = SubscriptionInfoStore.FilePath;
        SubscriptionInfoStore.FilePath = file;
        SubscriptionInfoStore.ResetCache();
        try
        {
            SubscriptionInfoStore.Record("https://example.com/sub", "upload=1; download=2; total=10; expire=99");
            SubscriptionInfoStore.ResetCache();
            var info = SubscriptionInfoStore.Get("https://example.com/sub");
            await (info?.Left ?? -1).Should().BeEqualTo(7L);
            await (info?.Expire ?? 0).Should().BeEqualTo(99L);
            await File.ReadAllText(file).Contains("example.com").Should().BeFalse();
        }
        finally
        {
            SubscriptionInfoStore.FilePath = original;
            SubscriptionInfoStore.ResetCache();
            File.Delete(file);
        }
    }

    [Test]
    [Arguments(0L, 100L, -60L, SubscriptionInfoStore.WarningKind.Expired)]
    [Arguments(0L, 100L, 2 * 86400L, SubscriptionInfoStore.WarningKind.ExpiresSoon)]
    [Arguments(100L, 100L, 30 * 86400L, SubscriptionInfoStore.WarningKind.NoDataLeft)]
    [Arguments(95L, 100L, 30 * 86400L, SubscriptionInfoStore.WarningKind.LowData)]
    [Arguments(50L, 100L, 30 * 86400L, SubscriptionInfoStore.WarningKind.None)]
    [Arguments(50L, 0L, 0L, SubscriptionInfoStore.WarningKind.None)]
    public async Task Warning_PicksTheRightKind(long used, long total, long expiresIn, SubscriptionInfoStore.WarningKind expected)
    {
        var expire = expiresIn == 0 ? 0 : Now.ToUnixTimeSeconds() + expiresIn;
        var info = new SubscriptionInfoStore.Info(0, used, total, expire, Now.ToUnixTimeSeconds());
        await SubscriptionInfoStore.Warning(info, Now).Should().BeEqualTo(expected);
    }

    [Test]
    public async Task KeyFor_NeverContainsTheUrl()
    {
        var key = SubscriptionInfoStore.KeyFor("https://example.com/sub?token=SECRET123");
        await key.Contains("SECRET", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        await key.Length.Should().BeEqualTo(32);
    }
}
