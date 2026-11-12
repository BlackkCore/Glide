using System.Globalization;

namespace ServiceLib.Tests.Common;

/// <summary>
/// The UI offers English and Persian only. Every other language's resources, option and any saved choice stay in
/// place; they just are not offered or applied.
/// </summary>
public class LanguageSelectionTests
{
    [Test]
    public async Task Selector_OffersOnlyEnglishAndPersian_InThatOrder()
    {
        var shown = Global.SelectableLanguageOptions;
        await shown.Count.Should().BeEqualTo(2);
        await shown[0].Should().BeEqualTo(new LanguageOption("en", "English"));
        await shown[1].Should().BeEqualTo(new LanguageOption("fa", "فارسی"));
    }

    [Test]
    public async Task EveryLanguageOption_StaysDefined_NothingIsRemoved()
    {
        var values = Global.LanguageOptions.Select(t => t.Value).ToList();
        foreach (var v in new[] { "zh-Hans", "zh-Hant", "en", "fa", "fr", "ru", "hu", "id", "az" })
        {
            await values.Contains(v).Should().BeTrue();
        }
        await Global.Languages.Count.Should().BeEqualTo(9);
    }

    [Test]
    [Arguments("en", "en")]
    [Arguments("fa", "fa")]
    [Arguments("FA", "fa")]
    [Arguments(" en ", "en")]
    public async Task Resolve_KeepsAnOfferedLanguage(string saved, string used)
    {
        await Global.ResolveUiLanguage(saved).Should().BeEqualTo(used);
    }

    [Test]
    [Arguments("zh-Hans")]
    [Arguments("zh-Hant")]
    [Arguments("fr")]
    [Arguments("ru")]
    [Arguments("hu")]
    [Arguments("id")]
    [Arguments("az")]
    [Arguments("")]
    [Arguments("xx-unknown")]
    public async Task Resolve_FallsBackToEnglish_ForAnythingNotOffered(string saved)
    {
        await Global.ResolveUiLanguage(saved).Should().BeEqualTo("en");
    }

    [Test]
    public async Task Resolve_NullFallsBackToEnglish()
    {
        await Global.ResolveUiLanguage(null).Should().BeEqualTo("en");
    }

    [Test]
    public async Task Fallback_NeverRewritesTheSavedChoice()
    {
        // The saved value survives a save and load untouched, so it works again if the language is offered again.
        var ui = new UIItem { CurrentLanguage = "fr" };
        var json = JsonUtils.Serialize(ui);
        var back = JsonUtils.Deserialize<UIItem>(json)!;
        await back.CurrentLanguage.Should().BeEqualTo("fr");
        await Global.ResolveUiLanguage(back.CurrentLanguage).Should().BeEqualTo("en");
        await back.CurrentLanguage.Should().BeEqualTo("fr");
    }

    [Test]
    public async Task ChoosingAnOfferedLanguage_IsPersisted()
    {
        var ui = new UIItem { CurrentLanguage = "fr" };
        ui.CurrentLanguage = "fa";
        var back = JsonUtils.Deserialize<UIItem>(JsonUtils.Serialize(ui))!;
        await back.CurrentLanguage.Should().BeEqualTo("fa");
        await Global.ResolveUiLanguage(back.CurrentLanguage).Should().BeEqualTo("fa");
    }

    [Test]
    public async Task Persian_StaysRightToLeft_AndEnglishLeftToRight()
    {
        await new CultureInfo(Global.ResolveUiLanguage("fa")).TextInfo.IsRightToLeft.Should().BeTrue();
        await new CultureInfo(Global.ResolveUiLanguage("fr")).TextInfo.IsRightToLeft.Should().BeFalse();
        await new CultureInfo(Global.ResolveUiLanguage("en")).TextInfo.IsRightToLeft.Should().BeFalse();
    }

    [Test]
    public async Task BothOfferedLanguages_HaveResources_AndPersianDiffersFromEnglish()
    {
        var en = ResUI.ResourceManager.GetString(nameof(ResUI.NeedRebootTips), new CultureInfo("en"));
        var fa = ResUI.ResourceManager.GetString(nameof(ResUI.NeedRebootTips), new CultureInfo("fa"));
        await string.IsNullOrWhiteSpace(en).Should().BeFalse();
        await string.IsNullOrWhiteSpace(fa).Should().BeFalse();
        await fa.Should().NotBeEqualTo(en);
    }

    [Test]
    public async Task NewConfig_DefaultsToAnOfferedLanguage()
    {
        await Global.ResolveUiLanguage(Global.DefaultUiLanguage).Should().BeEqualTo(Global.DefaultUiLanguage);
        await Global.SelectableLanguageOptions.Any(t => t.Value == Global.DefaultUiLanguage).Should().BeTrue();
    }
}
