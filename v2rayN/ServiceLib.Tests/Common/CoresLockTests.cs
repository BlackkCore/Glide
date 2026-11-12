using System.Text.Json;
using System.Text.RegularExpressions;

namespace ServiceLib.Tests.Common;

/// <summary>
/// S-03: cores.lock.json is what package-zip.yml pins and hash-checks. These tests keep it complete and
/// consistent with the workflow, so a release can't fail late (or ship an unpinned core) because of it.
/// </summary>
public class CoresLockTests
{
    private static readonly string[] Cores = ["xray", "singBox", "mihomo"];
    private static readonly string[] Arches = ["x64", "x86"];

    // Xray releases their maintainers withdrew: v26.10.10 / v26.10.17 shipped CA roots that had to be
    // removed, v26.10.29 was pulled for the MPTCP default. The core-bin bundle shipped v26.10.10.
    private static readonly string[] WithdrawnXray = ["26.10.10", "26.10.17", "26.10.29"];

    private static JsonElement Lock()
        => JsonDocument.Parse(File.ReadAllText(Path.Combine(TestRepo.Root(), "cores.lock.json"))).RootElement;

    [Test]
    public async Task Lock_BundleIsPinnedByCommitAndHash()
    {
        var bundle = Lock().GetProperty("coreBin");
        await Regex.IsMatch(bundle.GetProperty("commit").GetString() ?? "", "^[0-9a-f]{40}$").Should().BeTrue();
        await Regex.IsMatch(bundle.GetProperty("sha256").GetString() ?? "", "^[0-9a-f]{64}$").Should().BeTrue();
    }

    [Test]
    public async Task Lock_EveryCoreHasAVersionAndHash_ForEveryArchitecture()
    {
        var root = Lock();
        foreach (var arch in Arches)
        {
            foreach (var core in Cores)
            {
                var entry = root.GetProperty(arch).GetProperty(core);
                await Regex.IsMatch(entry.GetProperty("version").GetString() ?? "", @"^\d+\.\d+\.\d+$").Should().BeTrue();
                await Regex.IsMatch(entry.GetProperty("sha256").GetString() ?? "", "^[0-9a-f]{64}$").Should().BeTrue();
            }
        }
    }

    [Test]
    public async Task Lock_VersionsMatchAcrossArchitectures_AndHashesDiffer()
    {
        var root = Lock();
        foreach (var core in Cores)
        {
            var x64 = root.GetProperty("x64").GetProperty(core);
            var x86 = root.GetProperty("x86").GetProperty(core);
            await x86.GetProperty("version").GetString().Should().BeEqualTo(x64.GetProperty("version").GetString());
            // Different architectures are different files; the same hash twice means a copy-paste slip.
            await x86.GetProperty("sha256").GetString().Should().NotBeEqualTo(x64.GetProperty("sha256").GetString());
        }
    }

    [Test]
    public async Task Lock_DoesNotPinAWithdrawnXray()
    {
        var root = Lock();
        foreach (var arch in Arches)
        {
            await WithdrawnXray.Contains(root.GetProperty(arch).GetProperty("xray").GetProperty("version").GetString()!).Should().BeFalse();
        }
    }

    [Test]
    public async Task Workflow_UsesOnlyPinnedSources()
    {
        var workflow = File.ReadAllText(Path.Combine(TestRepo.Root(), ".github", "workflows", "package-zip.yml"));
        // Never a moving branch, and never the bundle's own cores.
        await workflow.Contains("refs/heads").Should().BeFalse();
        await workflow.Contains("releases/latest").Should().BeFalse();
        await workflow.Contains("sha256sum -c").Should().BeTrue();
        // It reads exactly the keys the lock has, for both architectures.
        foreach (var core in Cores)
        {
            await workflow.Contains($"fetch {core} ").Should().BeTrue();
        }
        await workflow.Contains("$ARCH.$name.version").Should().BeTrue();
        await workflow.Contains("$ARCH.$name.sha256").Should().BeTrue();
    }

    // Tripwire only (it reads text): the srss removal and the "no .srs anywhere" backstop were executed
    // for real against a synthetic bundle.
    [Test]
    public async Task Workflow_DropsTheSrssRuleSetsAndChecksNoneRemain()
    {
        var workflow = File.ReadAllText(Path.Combine(TestRepo.Root(), ".github", "workflows", "package-zip.yml"));
        await workflow.Contains("rm -rf \"$PACKAGE_DIR/bin/srss\"").Should().BeTrue();
        await workflow.Contains("-iname '*.srs'").Should().BeTrue();
        var backstop = workflow.IndexOf("No unlicensed rule sets in the package", StringComparison.Ordinal);
        await (backstop > 0 && backstop < workflow.IndexOf("Package zip archive", StringComparison.Ordinal)).Should().BeTrue();
    }
}
