using System.Globalization;

namespace ServiceLib.Tests.Common;

/// <summary>"You're gliding through {0}" became "Connected to {0}", with "the" where English needs it ("the Netherlands").</summary>
public class CountryGrammarTests
{
    [Test]
    [Arguments("Netherlands", "the Netherlands")]
    [Arguments("United States", "the United States")]
    [Arguments("United Kingdom", "the United Kingdom")]
    [Arguments("United Arab Emirates", "the United Arab Emirates")]
    [Arguments("Philippines", "the Philippines")]
    [Arguments("Bahamas", "the Bahamas")]
    [Arguments("Gambia", "the Gambia")]
    [Arguments("Maldives", "the Maldives")]
    [Arguments("Seychelles", "the Seychelles")]
    [Arguments("Czech Republic", "the Czech Republic")]
    [Arguments("Dominican Republic", "the Dominican Republic")]
    [Arguments("Central African Republic", "the Central African Republic")]
    [Arguments("Marshall Islands", "the Marshall Islands")]
    [Arguments("Cayman Islands", "the Cayman Islands")]
    [Arguments("Falkland Islands (Islas Malvinas)", "the Falkland Islands (Islas Malvinas)")]
    [Arguments("Palestinian Territories", "the Palestinian Territories")]
    [Arguments("Congo - Kinshasa", "the Democratic Republic of the Congo")]
    [Arguments("Congo - Brazzaville", "the Republic of the Congo")]
    public async Task NamesThatNeedThe_GetIt(string name, string expected)
    {
        await CountryGrammar.WithArticle(name).Should().BeEqualTo(expected);
    }

    [Test]
    [Arguments("Germany")]
    [Arguments("France")]
    [Arguments("Japan")]
    [Arguments("Singapore")]
    [Arguments("Ukraine")]
    [Arguments("Lebanon")]
    [Arguments("South Korea")]
    [Arguments("Trinidad & Tobago")]
    [Arguments("Hong Kong SAR China")]
    [Arguments("Isle of Man")]
    [Arguments("Christmas Island")]
    public async Task OtherNames_AreLeftAlone(string name)
    {
        await CountryGrammar.WithArticle(name).Should().BeEqualTo(name);
    }

    [Test]
    public async Task AlreadyHavingThe_IsNotDoubled()
    {
        await CountryGrammar.WithArticle("the Netherlands").Should().BeEqualTo("the Netherlands");
    }

    [Test]
    public async Task EmptyInput_StaysEmpty()
    {
        await CountryGrammar.WithArticle("").Should().BeEqualTo("");
        await CountryGrammar.WithArticle(null).Should().BeEqualTo("");
    }

    [Test]
    public async Task EveryRegionName_GivesASensibleResult_WithAtMostOneThe()
    {
        var names = CultureInfo.GetCultures(CultureTypes.SpecificCultures)
            .Select(c => { try { return new RegionInfo(c.Name).EnglishName; } catch (ArgumentException) { return null; } })
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct()
            .ToList();
        await names.Count.Should().BeGreaterThan(100);
        foreach (var n in names)
        {
            var r = CountryGrammar.WithArticle(n);
            await string.IsNullOrWhiteSpace(r).Should().BeFalse();
            await r.StartsWith("the the ", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
            await (r == n || r.EndsWith(n!, StringComparison.Ordinal) || n!.StartsWith("Congo", StringComparison.Ordinal)).Should().BeTrue();
        }
    }

    [Test]
    public async Task TheHeadline_ReadsConnectedTo_InEnglish_AndPersianIsUnchanged()
    {
        var en = ResUI.ResourceManager.GetString(nameof(ResUI.TbHeadlineOn), new CultureInfo("en"))!;
        await en.Should().BeEqualTo("Connected to {0}");
        await string.Format(en, CountryGrammar.WithArticle("Netherlands")).Should().BeEqualTo("Connected to the Netherlands");
        await string.Format(en, CountryGrammar.WithArticle("Germany")).Should().BeEqualTo("Connected to Germany");
        var fa = ResUI.ResourceManager.GetString(nameof(ResUI.TbHeadlineOn), new CultureInfo("fa"))!;
        await fa.Should().BeEqualTo("به {0} وصل شدید");
    }

    [Test]
    public async Task OldWording_IsGone_FromTheAppSources()
    {
        var root = TestRepo.Root();
        foreach (var rel in new[] { @"v2rayN\ServiceLib\Resx\ResUI.resx", @"v2rayN\v2rayN.Desktop\Views\StatusBarView.axaml.cs" })
        {
            await File.ReadAllText(Path.Combine(root, rel)).Contains("gliding through", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        }
    }
}
