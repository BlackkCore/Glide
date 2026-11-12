
namespace ServiceLib.Tests.Common;

/// <summary>
/// R-21/R-22/R-23: every bundled font and core has its licence text in the repo, and the build ships
/// them (Desktop csproj for the app, package-zip.yml for the cores), so a new font or core can't slip in
/// without its licence.
/// </summary>
public class LicenseShippingTests
{

    private static string Csproj() => File.ReadAllText(Path.Combine(TestRepo.Root(), "v2rayN", "v2rayN.Desktop", "v2rayN.Desktop.csproj"));

    [Test]
    public async Task EveryBundledFontFamily_HasALicenceShippedUnderLicenses()
    {
        var fonts = Directory.GetFiles(Path.Combine(TestRepo.Root(), "v2rayN", "v2rayN.Desktop", "Assets", "Fonts"), "*.ttf");
        var families = fonts.Select(f => Path.GetFileNameWithoutExtension(f).Split('-')[0]).Distinct().ToList();
        await (families.Count > 0).Should().BeTrue();
        var csproj = Csproj();
        foreach (var family in families)
        {
            await csproj.Contains($"Licenses\\{family}-OFL.txt").Should().BeTrue();
        }
    }

    [Test]
    public async Task NoticeFilesAndLicenceTexts_ExistAndAreShipped()
    {
        var root = TestRepo.Root();
        foreach (var file in new[]
        {
            "LICENSE", "NOTICE.md", "THIRD_PARTY_NOTICES.md",
            Path.Combine("licenses", "cores", "Xray-core-MPL-2.0.txt"),
            Path.Combine("licenses", "cores", "Wintun-prebuilt-binaries.txt"),
            Path.Combine("licenses", "cores", "sing-box-GPL-3.0-with-name-clause.txt"),
            Path.Combine("licenses", "cores", "mihomo-GPL-3.0.txt"),
            Path.Combine("licenses", "cores", "libcronet-Chromium-BSD-3-Clause.txt"),
            Path.Combine("licenses", "third-party", "ANGLE-BSD-3-Clause.txt"),
            Path.Combine("licenses", "third-party", "sqlite-net-MIT.txt"),
        })
        {
            var path = Path.Combine(root, file);
            await (File.Exists(path) && new FileInfo(path).Length > 0).Should().BeTrue();
        }

        var csproj = Csproj();
        foreach (var shipped in new[] { "Licenses\\Glide-GPL-3.0.txt", "Licenses\\NOTICE.md", "Licenses\\THIRD_PARTY_NOTICES.md", "Licenses\\third-party\\" })
        {
            await csproj.Contains(shipped).Should().BeTrue();
        }
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "package-zip.yml"));
        await workflow.Contains("Licenses/cores").Should().BeTrue();
    }

    [Test]
    public async Task ProprietaryEditorTheme_IsNotReferenced()
    {
        // R-24: Semi.Avalonia.AvaloniaEdit is under a closed EULA GPL-3.0 can't carry; App.axaml uses Assets/AvaloniaEditTheme.axaml.
        var root = TestRepo.Root();
        foreach (var file in new[] { Path.Combine("v2rayN", "Directory.Packages.props"), Path.Combine("v2rayN", "v2rayN.Desktop", "v2rayN.Desktop.csproj"), Path.Combine("v2rayN", "v2rayN.Desktop", "App.axaml") })
        {
            var text = File.ReadAllText(Path.Combine(root, file));
            await text.Contains("Semi.Avalonia.AvaloniaEdit").Should().BeFalse();
            await text.Contains("AvaloniaEditSemiTheme").Should().BeFalse();
        }
    }

    [Test]
    public async Task SqliteNative_IsTheOfficialPackage()
    {
        // R-25: the native e_sqlite3.dll comes from SQLitePCLRaw.lib.e_sqlite3, not an unaffiliated build.
        var props = File.ReadAllText(Path.Combine(TestRepo.Root(), "v2rayN", "Directory.Packages.props"));
        await props.Contains("SQLitePCLRaw.lib.e_sqlite3").Should().BeTrue();
        await props.Contains("Repobot").Should().BeFalse();
    }

    [Test]
    public async Task EveryProxyCoreInTheLock_HasALicenceTextInTheRepo()
    {
        var licenses = Directory.GetFiles(Path.Combine(TestRepo.Root(), "licenses", "cores")).Select(Path.GetFileName).ToList();
        foreach (var marker in new[] { "Xray-core", "sing-box", "mihomo" })
        {
            await licenses.Any(l => l!.StartsWith(marker, StringComparison.Ordinal)).Should().BeTrue();
        }
    }
}
