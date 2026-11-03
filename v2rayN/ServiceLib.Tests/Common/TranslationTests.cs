using System.Collections;
using System.Globalization;
using System.Resources;

namespace ServiceLib.Tests.Common;

public class TranslationTests
{
    // Every UI string needs a Persian version: a missing one shows up as English in the RTL UI.
    [Test]
    public async Task EveryStringHasAPersianTranslation()
    {
        var english = Keys(CultureInfo.InvariantCulture);
        var persian = Keys(new CultureInfo("fa"));
        var missing = english.Except(persian).Order().ToList();

        await english.Count.Should().BeGreaterThan(100);
        await string.Join(", ", missing).Should().BeEqualTo(string.Empty);
    }

    private static HashSet<string> Keys(CultureInfo culture)
    {
        // tryParents: false, so a missing Persian string isn't filled in from English.
        var set = ResUI.ResourceManager.GetResourceSet(culture, true, false);
        return set == null
            ? []
            : set.Cast<DictionaryEntry>().Select(e => (string)e.Key).ToHashSet();
    }
}
