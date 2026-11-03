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
    public async Task KeyFor_NeverContainsTheUrl()
    {
        var key = SubscriptionInfoStore.KeyFor("https://example.com/sub?token=SECRET123");
        await key.Contains("SECRET", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        await key.Length.Should().BeEqualTo(32);
    }
}
