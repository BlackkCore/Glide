using v2rayN.Desktop.Converters;

namespace ServiceLib.Tests.Common;

public class ServerNameDisplayTests
{
    [Test]
    [Arguments("🇹🇷 Turkey 🇹🇷 | 📊 9.72GB |⏳24D | OkmS7WWCP8", "Turkey")]
    [Arguments("DE | 12 days left", "DE")]
    [Arguments("NL | 500 MiB", "NL")]
    // F-06: real name parts that used to be stripped.
    [Arguments("DE | Node0001", "DE · Node0001")]
    [Arguments("US | 5G", "US · 5G")]
    [Arguments("HK 4K | 9GB", "HK 4K")]
    [Arguments("9.72GB | Turkey", "9.72GB · Turkey")]
    public async Task Clean_KeepsNamesDropsPlanDetails(string remarks, string expected)
    {
        await ServerNameDisplay.Clean(remarks).Should().BeEqualTo(expected);
    }

    [Test]
    public async Task Clean_DifferentServersStayDifferent()
    {
        var names = new[] { "DE | Node0001", "DE | Node0002", "US | 5G", "US | 4G" }.Select(ServerNameDisplay.Clean).ToList();
        await names.Distinct().Count().Should().BeEqualTo(names.Count);
    }
}
