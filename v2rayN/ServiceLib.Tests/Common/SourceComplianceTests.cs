using System.Text.Json;
using System.Text.RegularExpressions;

namespace ServiceLib.Tests.Common;

/// <summary>
/// GPL-3.0 section 6 / MPL-2.0 section 3.2: a release that ships binaries must also publish their corresponding
/// source. cores.source.json pins the exact upstream commit of every core in cores.lock.json; the workflows build
/// the source archives from those pins and refuse a release without them. These tests keep the pieces wired
/// together, so a core bump or a workflow edit can't quietly drop the source (see docs/SOURCE_AND_LICENSES.md).
/// The scripts themselves are tested by `node --test scripts/source/`, which package-source.yml runs.
/// </summary>
public class SourceComplianceTests
{
    private static readonly string[] Cores = ["xray", "singBox", "mihomo"];

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([TestRepo.Root(), .. parts]));

    private static JsonElement Json(string file) => JsonDocument.Parse(Read(file)).RootElement;

    [Test]
    public async Task Pins_NameTheTagAndCommitOfEveryShippedCoreVersion()
    {
        var lock_ = Json("cores.lock.json");
        var pins = Json("cores.source.json");
        foreach (var core in Cores)
        {
            var version = lock_.GetProperty("x64").GetProperty(core).GetProperty("version").GetString();
            var pin = pins.GetProperty("cores").GetProperty(core);
            await pin.GetProperty("tag").GetString().Should().BeEqualTo($"v{version}");
            await Regex.IsMatch(pin.GetProperty("commit").GetString()!, "^[0-9a-f]{40}$").Should().BeTrue();
            await Regex.IsMatch(pin.GetProperty("repo").GetString()!, @"^[\w.-]+/[\w.-]+$").Should().BeTrue();
        }
    }

    [Test]
    public async Task Pins_RecordWhereLibcronetComesFrom()
    {
        var cronet = Json("cores.source.json").GetProperty("libcronet");
        foreach (var key in new[] { "cronetGoCommit", "naiveproxyCommit", "windowsAmd64GitBlob" })
        {
            await Regex.IsMatch(cronet.GetProperty(key).GetString()!, "^[0-9a-f]{40}$").Should().BeTrue();
        }
    }

    [Test]
    public async Task ReleaseWorkflow_UploadsSourceBeforeBinaries_AndChecksTheFinalAssetList()
    {
        var release = Read(".github", "workflows", "build-windows-desktop.yml");
        await Regex.IsMatch(release, @"release-zip:\s+if:[^\n]*\n\s+needs: \[build, release-source\]").Should().BeTrue();
        await release.Contains("package-source.yml").Should().BeTrue();
        await release.Contains("check-release-assets.mjs").Should().BeTrue();
        await release.Contains("--require-binaries").Should().BeTrue();
        await Regex.IsMatch(release, @"verify-release-sources:[\s\S]*needs: \[draft-release, public-key, release-source, release-zip, release-installer\]").Should().BeTrue();
        // Draft until verified: the release is created once as a draft, uploads never create it, only publish makes it public.
        await Regex.IsMatch(release, @"publish:\s+if:[^\n]*\n\s+needs: verify-release-sources").Should().BeTrue();
        await Read(".github", "workflows", "upload-sign.yml").Contains("gh release create").Should().BeFalse();
        await release.Contains("secrets: inherit").Should().BeFalse();
        await release.Contains("targetCommitish").Should().BeTrue();
    }

    [Test]
    public async Task SourceWorkflow_BuildsFromThePinnedCommit_InReleaseMode_AfterTheSelfTests()
    {
        var wf = Read(".github", "workflows", "package-source.yml");
        await wf.Contains("node --test scripts/source/").Should().BeTrue();
        await wf.Contains("make-source-bundle.mjs --release").Should().BeTrue();
        await wf.Contains("verify-source-bundle.mjs --release").Should().BeTrue();
        await wf.Contains("github.sha").Should().BeTrue();
        await wf.Contains("--no-vendor").Should().BeFalse();
    }

    [Test]
    public async Task PackagingWorkflow_TiesTheShippedBinariesToThePinnedSource_AndShipsSourceTxt()
    {
        var wf = Read(".github", "workflows", "package-zip.yml");
        await wf.Contains("cores.source.json").Should().BeTrue();
        await wf.Contains("gh attestation verify").Should().BeTrue();
        await wf.Contains("windowsAmd64GitBlob").Should().BeTrue();
        await wf.Contains("Licenses/SOURCE.txt").Should().BeTrue();
        await wf.Contains("write-source-notice.mjs").Should().BeTrue();
    }

    [Test]
    public async Task GeoLiteData_IsLeftOutOfPublicPackages_ByDefault_WithAnOverrideSwitch()
    {
        var release = Read(".github", "workflows", "build-windows-desktop.yml");
        var zip = Read(".github", "workflows", "package-zip.yml");
        await Regex.IsMatch(release, @"bundle_geolite_data:[\s\S]*?type: boolean\s+default: false").Should().BeTrue();
        await Regex.IsMatch(zip, @"bundle_geolite_data:[^\n]*\n\s+required: false\s+type: boolean\s+default: false").Should().BeTrue();
        await zip.Contains("if: ${{ !inputs.bundle_geolite_data }}").Should().BeTrue();
        await release.Contains("bundle_geolite_data: ${{ inputs.bundle_geolite_data }}").Should().BeTrue();
    }

    [Test]
    public async Task EnableLoopback_IsRemovedFromEveryPackage_AndChecked_AndNotInTheSourceManifest()
    {
        var zip = Read(".github", "workflows", "package-zip.yml");
        await zip.Contains("-iname 'EnableLoopback.exe' -delete").Should().BeTrue();
        await zip.Contains("EnableLoopback.exe must not ship").Should().BeTrue();
        await Read(".github", "workflows", "build-windows-desktop.yml").Contains("EnableLoopback").Should().BeTrue();
        await Read(".github", "workflows", "build-windows-desktop.yml").Contains("GeoLite-derived data files must not ship").Should().BeTrue();
        await Read("scripts", "source", "make-source-bundle.mjs").Contains("id: 'EnableLoopback.exe'").Should().BeFalse();
    }

    [Test]
    public async Task GpgSigning_PinsOneFingerprint_EverywhereAndKeepsThePassphraseOffTheCommandLine()
    {
        const string fpr = "218B01F25642A7A391BFCC867D63C285D10463CB";
        const string spaced = "218B 01F2 5642 A7A3 91BF  CC86 7D63 C285 D104 63CB";
        foreach (var wf in new[] { "upload-sign.yml", "pub-key.yml", "build-windows-desktop.yml" })
        {
            await Regex.Match(Read(".github", "workflows", wf), @"GLIDE_GPG_FINGERPRINT:\s*([0-9A-F]{40})\b").Groups[1].Value.Should().BeEqualTo(fpr);
        }
        foreach (var doc in new[] { new[] { "README.md" }, new[] { "README.fa.md" }, new[] { "SECURITY.md" } })
        {
            await Read(doc).Contains(spaced).Should().BeTrue();
        }
        // The retired 1.0.0 key must not be what a reader is told to verify against.
        await Read("README.md").Contains("D62D C3BC").Should().BeFalse();
        await Read("README.fa.md").Contains("D62D C3BC").Should().BeFalse();

        var upload = Read(".github", "workflows", "upload-sign.yml");
        await upload.Contains("--pinentry-mode loopback --passphrase-file").Should().BeTrue();
        await Regex.IsMatch(upload, @"--passphrase[ =]").Should().BeFalse(); // never on the command line
        await Regex.IsMatch(upload, @"GPG_PASSPHRASE:[^\n]*\n\s+required: true").Should().BeTrue();
        // Declared, required and passed by every workflow between the release run and the signing job; the public-key job never gets it.
        foreach (var wf in new[] { "package-source.yml", "package-zip.yml", "package-installer.yml" })
        {
            var text = Read(".github", "workflows", wf);
            await Regex.IsMatch(text, @"GPG_PASSPHRASE:[^\n]*\n\s+required: true").Should().BeTrue();
            await Regex.Matches(text, @"GPG_PASSPHRASE: \$\{\{ secrets\.GPG_PASSPHRASE \}\}").Count.Should().BeEqualTo(1);
        }
        await Regex.Matches(Read(".github", "workflows", "build-windows-desktop.yml"), @"GPG_PASSPHRASE: \$\{\{ secrets\.GPG_PASSPHRASE \}\}").Count.Should().BeEqualTo(3);
        await Read(".github", "workflows", "pub-key.yml").Contains("GPG_PASSPHRASE").Should().BeFalse();
    }
}
