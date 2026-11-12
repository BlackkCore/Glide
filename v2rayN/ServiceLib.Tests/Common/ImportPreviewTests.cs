namespace ServiceLib.Tests.Common;

public class ImportPreviewTests
{
    private const string Vless = "vless://3f6b1c1e-1c2d-4e5f-8a9b-0c1d2e3f4a5b@example.com:443?type=ws&security=tls#First";
    private const string Trojan = "trojan://secret@example.org:443#Second";

    [Test]
    public async Task Analyze_CountsServersByProtocol()
    {
        var result = ImportPreview.Analyze($"{Vless}\n{Trojan}\r\n{Vless}");
        await result.Servers.Count.Should().BeEqualTo(3);
        await result.Subscriptions.Should().BeEqualTo(0);
        await result.Protocols.Should().BeEqualTo("VLESS ×2, Trojan");
    }

    [Test]
    public async Task Analyze_ReadsABase64Block()
    {
        var result = ImportPreview.Analyze(Utils.Base64Encode($"{Vless}\n{Trojan}"));
        await result.Servers.Count.Should().BeEqualTo(2);
    }

    [Test]
    public async Task Analyze_TreatsAnAddressAsASubscription()
    {
        var result = ImportPreview.Analyze("  https://provider.example/sub?token=abc  ");
        await result.Subscriptions.Should().BeEqualTo(1);
        await result.Servers.Count.Should().BeEqualTo(0);
    }

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments("hello there")]
    public async Task Analyze_FindsNothingInOtherText(string text)
    {
        await ImportPreview.Analyze(text).IsEmpty.Should().BeTrue();
    }
}
