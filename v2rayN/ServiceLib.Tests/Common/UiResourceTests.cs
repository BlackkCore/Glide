using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ServiceLib.Tests.Common;

/// <summary>
/// UI/UX phase: the Avalonia project has no UI tests, so these keep the one thing a styling change most
/// often breaks quietly: a view that parses but asks for a colour or style key that is not defined,
/// or a colour that exists in one theme and not the other (shown as a missing brush at run time).
/// </summary>
public class UiResourceTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string Desktop() => Path.Combine(TestRepo.Root(), "v2rayN", "v2rayN.Desktop");

    private static IEnumerable<string> AxamlFiles()
        => Directory.EnumerateFiles(Desktop(), "*.axaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    private static HashSet<string> Keys(XElement root)
        => root.Descendants().Select(e => (string?)e.Attribute(X + "Key")).Where(k => k != null).Select(k => k!).ToHashSet();

    [Test]
    public async Task Axaml_EveryFileIsWellFormed()
    {
        var files = AxamlFiles().ToList();
        await files.Count.Should().BeGreaterThan(20);
        foreach (var f in files)
        {
            XDocument.Load(f); // throws, naming the file, when a hand edit broke the markup
        }
    }

    [Test]
    public async Task Theme_LightAndDarkDefineTheSameKeys()
    {
        var doc = XDocument.Load(Path.Combine(Desktop(), "Assets", "AppTheme.axaml"));
        var dicts = doc.Descendants()
            .Where(e => e.Name.LocalName == "ResourceDictionary" && ((string?)e.Attribute(X + "Key")) is "Light" or "Dark")
            .ToDictionary(e => (string)e.Attribute(X + "Key")!, Keys);
        await dicts.Count.Should().BeEqualTo(2);
        // Only Glide's own App* tokens must match. Overrides of Semi's keys may exist in one theme alone
        // (Semi's own default already suits the other), e.g. MenuFlyoutBackground in Dark.
        var onlyLight = dicts["Light"].Except(dicts["Dark"]).Where(k => k.StartsWith("App")).OrderBy(k => k).ToList();
        var onlyDark = dicts["Dark"].Except(dicts["Light"]).Where(k => k.StartsWith("App")).OrderBy(k => k).ToList();
        await onlyLight.Should().BeEmpty();
        await onlyDark.Should().BeEmpty();
    }

    [Test]
    public async Task Views_UseOnlyDefinedAppKeys()
    {
        var defined = new HashSet<string>();
        foreach (var f in Directory.EnumerateFiles(Path.Combine(Desktop(), "Assets"), "*.axaml"))
        {
            defined.UnionWith(Keys(XDocument.Load(f).Root!));
        }
        var use = new Regex(@"\{(?:DynamicResource|StaticResource)\s+(App\w+)\}");
        var missing = new SortedSet<string>();
        foreach (var f in AxamlFiles())
        {
            foreach (Match m in use.Matches(File.ReadAllText(f)))
            {
                if (!defined.Contains(m.Groups[1].Value))
                {
                    missing.Add($"{Path.GetFileName(f)}: {m.Groups[1].Value}");
                }
            }
        }
        await missing.Should().BeEmpty();
    }
}
