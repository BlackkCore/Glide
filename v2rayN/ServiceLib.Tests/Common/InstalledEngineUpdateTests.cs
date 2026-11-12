using System.ComponentModel;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ServiceLib.Tests.Common;

/// <summary>
/// Engine updates for the installed version: the short-lived administrator process installs into the protected program
/// folder, and trusts nothing it is handed (it re-checks the signed list, picks the build itself, and installs only that
/// build). Windows' permission prompt is replaced by a fake starter; a real prompt and a real Program Files are NOT TESTED here.
/// </summary>
public class InstalledEngineUpdateTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static readonly SemanticVersion Glide = new("1.1.0");

    private static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

    private static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);

    private sealed class Bench : IDisposable
    {
        public string Root { get; } = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"glide-inst-{Guid.NewGuid():N}")).FullName;
        public ECDsa Key { get; } = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        public string PublicKey => Key.ExportSubjectPublicKeyInfoPem();
        public string Bin(string core) => Path.Combine(Root, "bin", core);
        public string StatePath => Path.Combine(Root, "bin", ElevatedCoreInstall.StateFileName);

        public string MakeZip(params (string Name, byte[] Data)[] entries)
        {
            var path = Path.Combine(Root, $"dl-{Guid.NewGuid():N}.zip");
            using var fs = File.Create(path);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
            foreach (var (name, data) in entries)
            {
                using var s = zip.CreateEntry(name).Open();
                s.Write(data);
            }
            return path;
        }

        public CoreIndexAsset Asset(string zip, params (string Name, byte[] Data)[] files) => new()
        {
            Name = Path.GetFileName(zip),
            Url = "https://github.com/XTLS/Xray-core/releases/download/v1/x.zip",
            Size = new FileInfo(zip).Length,
            Sha256 = Sha(File.ReadAllBytes(zip)),
            Files = files.ToDictionary(f => f.Name, f => Sha(f.Data)),
        };

        public (string Index, string Sig) WriteIndex(CoreIndexDocument doc, ECDsa? signer = null, Func<string, string>? alterAfterSigning = null)
        {
            var json = JsonSerializer.Serialize(doc, CoreIndexJsonContext.Default.CoreIndexDocument);
            var sig = Convert.ToBase64String((signer ?? Key).SignData(Bytes(json), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));
            var index = Path.Combine(Root, $"idx-{Guid.NewGuid():N}.json");
            File.WriteAllText(index, alterAfterSigning?.Invoke(json) ?? json);
            File.WriteAllText(index + UpdateSignature.Extension, sig);
            return (index, index + UpdateSignature.Extension);
        }

        public CoreIndexDocument Doc(long sequence = 5, DateTimeOffset? expires = null) => new()
        {
            Format = 1,
            Sequence = sequence,
            Issued = Now.AddDays(-1),
            Expires = expires ?? Now.AddDays(60),
        };

        public void Dispose()
        {
            Key.Dispose();
            try
            {
                Directory.Delete(Root, true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static readonly byte[] NewXray = Bytes("xray 26.11.9");
    private static readonly byte[] NewWintun = Bytes("wintun");
    private static readonly byte[] NewSingBox = Bytes("sing-box 1.14.4");

    private static CoreIndexEntry Entry(string version, string arch, CoreIndexAsset asset)
    {
        var e = new CoreIndexEntry { Version = version, MinGlide = "1.1.0", VettedOn = "2026-11-12" };
        e.Assets[arch] = asset;
        return e;
    }

    private static int Run(Bench b, string index, string sig, string[] jobs, string arch = "win-x64", string? key = null)
        => ElevatedCoreInstall.Run([index, sig, .. jobs], b.Root, key ?? b.PublicKey, Now, Glide, arch);

    private static (string Index, string Sig, string XrayZip) OneEngineIndex(Bench b, string arch = "win-x64", long sequence = 5)
    {
        var zip = b.MakeZip(("xray.exe", NewXray), ("wintun.dll", NewWintun), ("geoip.dat", Bytes("not listed")));
        var doc = b.Doc(sequence);
        doc.Cores["xray"] = [Entry("26.11.9", arch, b.Asset(zip, ("xray.exe", NewXray), ("wintun.dll", NewWintun)))];
        var (index, sig) = b.WriteIndex(doc);
        return (index, sig, zip);
    }

    // ---- install, missing engine, several engines --------------------------------------------------------------

    [Test]
    public async Task InstalledEngine_IsReplacedInTheProgramFolder_PreviousKept_UserDataUntouched()
    {
        using var b = new Bench();
        Directory.CreateDirectory(b.Bin("xray"));
        File.WriteAllBytes(Path.Combine(b.Bin("xray"), "xray.exe"), Bytes("old xray"));
        File.WriteAllText(Path.Combine(b.Bin("xray"), "config.json"), "user's own file");
        Directory.CreateDirectory(Path.Combine(b.Root, "guiConfigs"));
        File.WriteAllText(Path.Combine(b.Root, "guiConfigs", "guiNConfig.json"), "settings");
        var (index, sig, zip) = OneEngineIndex(b);

        var code = Run(b, index, sig, [$"Xray={zip}"]);

        await code.Should().BeEqualTo(0);
        await File.ReadAllText(Path.Combine(b.Bin("xray"), "xray.exe")).Should().BeEqualTo("xray 26.11.9");
        await File.ReadAllText(Path.Combine(b.Bin("xray"), CoreInstaller.PreviousFolder, "xray.exe")).Should().BeEqualTo("old xray");
        await File.ReadAllText(Path.Combine(b.Bin("xray"), "config.json")).Should().BeEqualTo("user's own file");
        await File.ReadAllText(Path.Combine(b.Root, "guiConfigs", "guiNConfig.json")).Should().BeEqualTo("settings");
        await File.Exists(Path.Combine(b.Bin("xray"), "geoip.dat")).Should().BeFalse();
        await CoreIndexState.Load(b.StatePath).Should().BeEqualTo(5);   // the replay floor is kept in the protected folder
    }

    [Test]
    public async Task MissingEngine_IsInstalledFromTheSignedList()
    {
        using var b = new Bench();
        var (index, sig, zip) = OneEngineIndex(b);

        await Directory.Exists(b.Bin("xray")).Should().BeFalse();
        var code = Run(b, index, sig, [$"Xray={zip}"]);

        await code.Should().BeEqualTo(0);
        await File.ReadAllText(Path.Combine(b.Bin("xray"), "xray.exe")).Should().BeEqualTo("xray 26.11.9");
    }

    [Test]
    public async Task SeveralEngines_AreInstalledInOneRun_AndOneFailureOnlyMarksItsOwnBit()
    {
        using var b = new Bench();
        var xrayZip = b.MakeZip(("xray.exe", NewXray), ("wintun.dll", NewWintun));
        var sbZip = b.MakeZip(("sing-box.exe", NewSingBox));
        var doc = b.Doc();
        doc.Cores["xray"] = [Entry("26.11.9", "win-x64", b.Asset(xrayZip, ("xray.exe", NewXray), ("wintun.dll", NewWintun)))];
        doc.Cores["singBox"] = [Entry("1.14.4", "win-x64", b.Asset(sbZip, ("sing-box.exe", NewSingBox)))];
        var (index, sig) = b.WriteIndex(doc);
        // the second download is not the build the list names
        File.WriteAllBytes(sbZip, [.. File.ReadAllBytes(sbZip), 0x00]);

        var code = Run(b, index, sig, [$"Xray={xrayZip}", $"sing_box={sbZip}"]);

        await code.Should().BeEqualTo(0b10);
        await File.Exists(Path.Combine(b.Bin("xray"), "xray.exe")).Should().BeTrue();
        await File.Exists(Path.Combine(b.Bin("sing_box"), "sing-box.exe")).Should().BeFalse();
    }

    // ---- the signed list is checked again by the administrator process ------------------------------------------

    [Test]
    public async Task ListSignedWithAnotherKey_IsRefused_AndNothingIsWritten()
    {
        using var b = new Bench();
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var zip = b.MakeZip(("xray.exe", NewXray), ("wintun.dll", NewWintun));
        var doc = b.Doc();
        doc.Cores["xray"] = [Entry("26.11.9", "win-x64", b.Asset(zip, ("xray.exe", NewXray), ("wintun.dll", NewWintun)))];
        var (index, sig) = b.WriteIndex(doc, other);

        await Run(b, index, sig, [$"Xray={zip}"]).Should().BeEqualTo(ElevatedCoreInstall.IndexRejected);
        await Directory.Exists(b.Bin("xray")).Should().BeFalse();
        await File.Exists(b.StatePath).Should().BeFalse();
    }

    [Test]
    public async Task ListChangedAfterSigning_IsRefused()
    {
        using var b = new Bench();
        var zip = b.MakeZip(("xray.exe", NewXray), ("wintun.dll", NewWintun));
        var doc = b.Doc();
        doc.Cores["xray"] = [Entry("26.11.9", "win-x64", b.Asset(zip, ("xray.exe", NewXray), ("wintun.dll", NewWintun)))];
        var (index, sig) = b.WriteIndex(doc, alterAfterSigning: j => j.Replace("26.11.9", "26.11.8"));

        await Run(b, index, sig, [$"Xray={zip}"]).Should().BeEqualTo(ElevatedCoreInstall.IndexRejected);
        await Directory.Exists(b.Bin("xray")).Should().BeFalse();
    }

    [Test]
    public async Task ExpiredList_IsRefused()
    {
        using var b = new Bench();
        var zip = b.MakeZip(("xray.exe", NewXray), ("wintun.dll", NewWintun));
        var doc = b.Doc(expires: Now.AddMinutes(-5));
        doc.Issued = Now.AddDays(-30);
        doc.Cores["xray"] = [Entry("26.11.9", "win-x64", b.Asset(zip, ("xray.exe", NewXray), ("wintun.dll", NewWintun)))];
        var (index, sig) = b.WriteIndex(doc);

        await Run(b, index, sig, [$"Xray={zip}"]).Should().BeEqualTo(ElevatedCoreInstall.IndexRejected);
        await Directory.Exists(b.Bin("xray")).Should().BeFalse();
    }

    [Test]
    public async Task OlderListThanOneAlreadyAccepted_IsRefused_AsAReplay()
    {
        using var b = new Bench();
        var (index, sig, zip) = OneEngineIndex(b, sequence: 5);
        Directory.CreateDirectory(Path.Combine(b.Root, "bin"));
        CoreIndexState.Save(b.StatePath, 9);

        await Run(b, index, sig, [$"Xray={zip}"]).Should().BeEqualTo(ElevatedCoreInstall.IndexRejected);
        await Directory.Exists(b.Bin("xray")).Should().BeFalse();
        await CoreIndexState.Load(b.StatePath).Should().BeEqualTo(9);
    }

    [Test]
    public async Task ASignedListCannotBeSwappedForAnotherFileWhileItIsRead()
    {
        // The index is opened once with writes denied: while it is open, nobody can replace it under the process.
        using var b = new Bench();
        var (index, _, _) = OneEngineIndex(b);
        using var holder = new FileStream(index, FileMode.Open, FileAccess.Read, FileShare.Read);

        var swap = () => File.WriteAllText(index, "swapped");
        await swap.Should().Throw<IOException>();
    }

    // ---- the download itself ----------------------------------------------------------------------------------------

    [Test]
    public async Task TamperedDownload_IsRefused_AndTheInstalledEngineIsUntouched()
    {
        using var b = new Bench();
        Directory.CreateDirectory(b.Bin("xray"));
        File.WriteAllBytes(Path.Combine(b.Bin("xray"), "xray.exe"), Bytes("old xray"));
        var (index, sig, zip) = OneEngineIndex(b);
        var bytes = File.ReadAllBytes(zip);
        bytes[bytes.Length / 2] ^= 0x01;
        File.WriteAllBytes(zip, bytes);

        await Run(b, index, sig, [$"Xray={zip}"]).Should().BeEqualTo(1);
        await File.ReadAllText(Path.Combine(b.Bin("xray"), "xray.exe")).Should().BeEqualTo("old xray");
    }

    [Test]
    public async Task InterruptedDownload_ShortFile_IsRefused_AndTheInstalledEngineIsUntouched()
    {
        using var b = new Bench();
        Directory.CreateDirectory(b.Bin("xray"));
        File.WriteAllBytes(Path.Combine(b.Bin("xray"), "xray.exe"), Bytes("old xray"));
        var (index, sig, zip) = OneEngineIndex(b);
        var bytes = File.ReadAllBytes(zip);
        File.WriteAllBytes(zip, bytes[..(bytes.Length / 2)]);

        await Run(b, index, sig, [$"Xray={zip}"]).Should().BeEqualTo(1);
        await File.ReadAllText(Path.Combine(b.Bin("xray"), "xray.exe")).Should().BeEqualTo("old xray");
        await Directory.GetDirectories(b.Bin("xray"), CoreInstaller.StagingPrefix + "*").Should().BeEmpty();
    }

    [Test]
    public async Task AnOlderListedBuild_IsNotInstalled_TheNewestAllowedOneIsTheOnlyAcceptedZip()
    {
        // No downgrade through the helper: it picks the build itself, a zip of an older listed build does not match it.
        using var b = new Bench();
        var oldZip = b.MakeZip(("xray.exe", Bytes("xray 26.10.1")), ("wintun.dll", NewWintun));
        var newZip = b.MakeZip(("xray.exe", NewXray), ("wintun.dll", NewWintun));
        var doc = b.Doc();
        doc.Cores["xray"] =
        [
            Entry("26.10.1", "win-x64", b.Asset(oldZip, ("xray.exe", Bytes("xray 26.10.1")), ("wintun.dll", NewWintun))),
            Entry("26.11.9", "win-x64", b.Asset(newZip, ("xray.exe", NewXray), ("wintun.dll", NewWintun))),
        ];
        var (index, sig) = b.WriteIndex(doc);

        await Run(b, index, sig, [$"Xray={oldZip}"]).Should().BeEqualTo(1);
        await Directory.Exists(b.Bin("xray")).Should().BeFalse();
        await Run(b, index, sig, [$"Xray={newZip}"]).Should().BeEqualTo(0);
    }

    [Test]
    public async Task AWithdrawnBuild_IsNeverPicked()
    {
        using var b = new Bench();
        var goodZip = b.MakeZip(("xray.exe", Bytes("xray 26.10.1")), ("wintun.dll", NewWintun));
        var badZip = b.MakeZip(("xray.exe", NewXray), ("wintun.dll", NewWintun));
        var doc = b.Doc();
        doc.Cores["xray"] =
        [
            Entry("26.10.1", "win-x64", b.Asset(goodZip, ("xray.exe", Bytes("xray 26.10.1")), ("wintun.dll", NewWintun))),
            Entry("26.11.9", "win-x64", b.Asset(badZip, ("xray.exe", NewXray), ("wintun.dll", NewWintun))),
        ];
        doc.Revoked = [new CoreIndexRevocation { Core = "xray", Version = "26.11.9", Reason = "withdrawn" }];
        var (index, sig) = b.WriteIndex(doc);

        await Run(b, index, sig, [$"Xray={badZip}"]).Should().BeEqualTo(1);
        await Run(b, index, sig, [$"Xray={goodZip}"]).Should().BeEqualTo(0);
    }

    [Test]
    public async Task X86_UsesTheX86Asset_AndRefusesTheX64Zip()
    {
        using var b = new Bench();
        var x64 = b.MakeZip(("xray.exe", Bytes("xray x64")), ("wintun.dll", NewWintun));
        var x86 = b.MakeZip(("xray.exe", Bytes("xray x86")), ("wintun.dll", NewWintun));
        var entry = new CoreIndexEntry { Version = "26.11.9", MinGlide = "1.1.0" };
        entry.Assets["win-x64"] = b.Asset(x64, ("xray.exe", Bytes("xray x64")), ("wintun.dll", NewWintun));
        entry.Assets["win-x86"] = b.Asset(x86, ("xray.exe", Bytes("xray x86")), ("wintun.dll", NewWintun));
        var doc = b.Doc();
        doc.Cores["xray"] = [entry];
        var (index, sig) = b.WriteIndex(doc);

        await Run(b, index, sig, [$"Xray={x64}"], "win-x86").Should().BeEqualTo(1);
        await Run(b, index, sig, [$"Xray={x86}"], "win-x86").Should().BeEqualTo(0);
        await File.ReadAllText(Path.Combine(b.Bin("xray"), "xray.exe")).Should().BeEqualTo("xray x86");
    }

    // ---- what goes wrong on a real PC --------------------------------------------------------------------------------

    [Test]
    public async Task ARunningEngine_LockedExe_RollsBack_AndKeepsTheWorkingOne()
    {
        using var b = new Bench();
        Directory.CreateDirectory(b.Bin("xray"));
        var exe = Path.Combine(b.Bin("xray"), "xray.exe");
        File.WriteAllBytes(exe, Bytes("old xray"));
        File.WriteAllBytes(Path.Combine(b.Bin("xray"), "wintun.dll"), Bytes("old wintun"));
        var (index, sig, zip) = OneEngineIndex(b);

        // a running program holds its exe open without sharing deletes or renames
        using (new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await Run(b, index, sig, [$"Xray={zip}"]).Should().BeEqualTo(1);
        }

        await File.ReadAllText(exe).Should().BeEqualTo("old xray");
        await File.ReadAllText(Path.Combine(b.Bin("xray"), "wintun.dll")).Should().BeEqualTo("old wintun");
        await Directory.GetDirectories(b.Bin("xray"), CoreInstaller.StagingPrefix + "*").Should().BeEmpty();
    }

    [Test]
    public async Task EngineFolderThatCannotBeWritten_FailsWithoutDamage()
    {
        // Stands in for a permission failure (an ACL cannot be set up portably in a test): the engine's folder is a file.
        using var b = new Bench();
        Directory.CreateDirectory(Path.Combine(b.Root, "bin"));
        File.WriteAllText(b.Bin("xray"), "not a folder");
        var (index, sig, zip) = OneEngineIndex(b);

        await Run(b, index, sig, [$"Xray={zip}"]).Should().BeEqualTo(1);
        await File.ReadAllText(b.Bin("xray")).Should().BeEqualTo("not a folder");
    }

    [Test]
    public async Task NotEnoughDiskSpace_ChangesNothing_AndSaysSo()
    {
        using var b = new Bench();
        Directory.CreateDirectory(b.Bin("xray"));
        File.WriteAllBytes(Path.Combine(b.Bin("xray"), "xray.exe"), Bytes("old xray"));
        var zip = b.MakeZip(("xray.exe", NewXray), ("wintun.dll", NewWintun));
        var asset = b.Asset(zip, ("xray.exe", NewXray), ("wintun.dll", NewWintun));

        var r = CoreInstaller.Install(zip, asset, b.Bin("xray"), freeBytes: _ => asset.Size * CoreInstaller.SpaceFactor - 1);

        await r.Ok.Should().BeFalse();
        await r.Error.Should().BeEqualTo(CoreInstallError.NoSpace);
        await File.ReadAllText(Path.Combine(b.Bin("xray"), "xray.exe")).Should().BeEqualTo("old xray");
        await Directory.GetDirectories(b.Bin("xray"), CoreInstaller.StagingPrefix + "*").Should().BeEmpty();
    }

    [Test]
    public async Task TheZipIsHeldOpen_SoItCannotBeSwappedBetweenTheCheckAndTheExtraction()
    {
        using var b = new Bench();
        var zip = b.MakeZip(("xray.exe", NewXray), ("wintun.dll", NewWintun));
        var asset = b.Asset(zip, ("xray.exe", NewXray), ("wintun.dll", NewWintun));
        // the free-space probe runs after the check and before the extraction: the moment an attacker would swap the file
        var swapped = false;
        long Probe(string _)
        {
            try
            {
                File.WriteAllBytes(zip, Bytes("evil"));
                swapped = true;
            }
            catch (IOException)
            {
            }
            return long.MaxValue;
        }

        var r = CoreInstaller.Install(zip, asset, b.Bin("xray"), freeBytes: Probe);

        await swapped.Should().BeFalse();
        await r.Ok.Should().BeTrue();
    }

    // ---- arguments and the Windows permission prompt ---------------------------------------------------------------

    [Test]
    public async Task Arguments_AreStrict_UnknownCoresDuplicatesAndPathsAreRefused()
    {
        using var b = new Bench();
        var (index, sig, zip) = OneEngineIndex(b);

        await Run(b, index, sig, [$"v2rayN={zip}"]).Should().BeEqualTo(ElevatedCoreInstall.BadArguments);
        await Run(b, index, sig, [$"..\\..\\Windows={zip}"]).Should().BeEqualTo(ElevatedCoreInstall.BadArguments);
        await Run(b, index, sig, [$"Xray={zip}", $"Xray={zip}"]).Should().BeEqualTo(ElevatedCoreInstall.BadArguments);
        await Run(b, index, sig, ["Xray="]).Should().BeEqualTo(ElevatedCoreInstall.BadArguments);
        await Run(b, index, sig, []).Should().BeEqualTo(ElevatedCoreInstall.BadArguments);
        await Run(b, index, sig, [$"Xray={zip}"], arch: null!).Should().BeEqualTo(0);   // null = this PC's own architecture
        await Directory.Exists(Path.Combine(b.Root, "Windows")).Should().BeFalse();
    }

    [Test]
    public async Task UacDeclined_MarksEveryEngineDeclined_AndLeavesNoTemporaryFiles()
    {
        using var b = new Bench();
        var zip = b.MakeZip(("xray.exe", NewXray));
        string? seen = null;

        var outcomes = await ElevatedCoreInstallLauncher.RunAsync(
            [(ECoreType.Xray, zip)], "{}", "sig", b.Root,
            (exe, args) =>
            {
                seen = args;
                throw new Win32Exception(1223);
            });

        await outcomes[ECoreType.Xray].Should().BeEqualTo(ElevatedInstallOutcome.Declined);
        await seen!.StartsWith(ElevatedCoreInstall.Verb).Should().BeTrue();
        await Directory.GetFiles(b.Root, "*.json").Should().BeEmpty();
        await Directory.GetFiles(b.Root, "*" + UpdateSignature.Extension).Should().BeEmpty();
    }

    [Test]
    public async Task ExitCode_MapsToEachEngine_AndOtherStartFailuresAreFailures()
    {
        using var b = new Bench();
        var jobs = new[] { (ECoreType.Xray, "a.zip"), (ECoreType.sing_box, "b.zip"), (ECoreType.mihomo, "c.zip") };

        var partial = await ElevatedCoreInstallLauncher.RunAsync(jobs, "{}", "sig", b.Root, (_, _) => Task.FromResult(0b010));
        await partial[ECoreType.Xray].Should().BeEqualTo(ElevatedInstallOutcome.Installed);
        await partial[ECoreType.sing_box].Should().BeEqualTo(ElevatedInstallOutcome.Failed);
        await partial[ECoreType.mihomo].Should().BeEqualTo(ElevatedInstallOutcome.Installed);

        var rejected = await ElevatedCoreInstallLauncher.RunAsync(jobs, "{}", "sig", b.Root, (_, _) => Task.FromResult(ElevatedCoreInstall.IndexRejected));
        await rejected.Values.All(o => o == ElevatedInstallOutcome.Failed).Should().BeTrue();

        var cannotStart = await ElevatedCoreInstallLauncher.RunAsync(jobs, "{}", "sig", b.Root, (_, _) => throw new Win32Exception(5));
        await cannotStart.Values.All(o => o == ElevatedInstallOutcome.Failed).Should().BeTrue();
    }

    [Test]
    public async Task PathsWithSpaces_AreQuotedForTheAdministratorProcess()
    {
        using var b = new Bench();
        string? seen = null;
        await ElevatedCoreInstallLauncher.RunAsync([(ECoreType.Xray, @"C:\Users\A B\dl.zip")], "{}", "sig", b.Root,
            (_, args) =>
            {
                seen = args;
                return Task.FromResult(0);
            });

        await seen!.Contains("\"Xray=C:\\Users\\A B\\dl.zip\"").Should().BeTrue();
    }

    // ---- which rows the Updates screen shows ------------------------------------------------------------------------

    [Test]
    public async Task UpdateRows_AreTheSameForEveryInstallation_ApplicationAndThreeEngines()
    {
        var rows = CoreInfoManager.GetCheckUpdateCoreTypes(true, false);
        await rows.SequenceEqual([ECoreType.v2rayN, ECoreType.Xray, ECoreType.mihomo, ECoreType.sing_box]).Should().BeTrue();
        await CoreInfoManager.GetCheckUpdateCoreTypes(false, false).Count.Should().BeEqualTo(3);
        await CoreInfoManager.GetCheckUpdateCoreTypes(true, true).SequenceEqual([ECoreType.v2rayN]).Should().BeTrue();
    }

    [Test]
    public async Task EveryEngineGoesThroughTheSignedList_InstalledAndPortable()
    {
        await UpdateService.UsesCoreIndex(ECoreType.Xray).Should().BeTrue();
        await UpdateService.UsesCoreIndex(ECoreType.sing_box).Should().BeTrue();
        await UpdateService.UsesCoreIndex(ECoreType.mihomo).Should().BeTrue();
        await UpdateService.UsesCoreIndex(ECoreType.v2fly).Should().BeFalse();
    }

    // ---- tripwires on code that has no unit-test host ---------------------------------------------------------------

    [Test]
    public async Task AppUpdate_NeverOverwritesAnInstalledEngine_AndTheAdminProcessIsReachedBeforeAnyWindow()
    {
        var updater = File.ReadAllText(Path.Combine(TestRepo.Root(), "v2rayN", "AmazTool", "UpgradeApp.cs"));
        await updater.Contains("IsEngineFile(fullName)").Should().BeTrue();

        var program = File.ReadAllText(Path.Combine(TestRepo.Root(), "v2rayN", "v2rayN.Desktop", "Program.cs"));
        var verb = program.IndexOf("ElevatedCoreInstall.Verb", StringComparison.Ordinal);
        await (verb > 0 && verb < program.IndexOf("OnStartup(args)", StringComparison.Ordinal)).Should().BeTrue();
        await program.Contains("GlidePaths.IsInstalled").Should().BeTrue();
    }

    [Test]
    public async Task Uninstall_RemovesWhatEngineUpdatesLeaveInProgramFiles()
    {
        var iss = File.ReadAllText(Path.Combine(TestRepo.Root(), "installer", "glide.iss"));
        foreach (var core in new[] { "xray", "sing_box", "mihomo" })
        {
            await iss.Contains($@"{{app}}\bin\{core}\{CoreInstaller.PreviousFolder}").Should().BeTrue();
        }
        await iss.Contains(ElevatedCoreInstall.StateFileName).Should().BeTrue();
    }

    [Test]
    public async Task ReloadAfterAnUpdate_RunsEvenWhenAnInstallThrows()
    {
        var vm = File.ReadAllText(Path.Combine(TestRepo.Root(), "v2rayN", "ServiceLib", "ViewModels", "CheckUpdateViewModel.cs"));
        var finallyAt = vm.IndexOf("finally", vm.IndexOf("await UpgradeCore();", StringComparison.Ordinal), StringComparison.Ordinal);
        await (finallyAt > 0 && vm.IndexOf("UpdateFinishedSub(true)", finallyAt, StringComparison.Ordinal) > finallyAt).Should().BeTrue();
    }
}
