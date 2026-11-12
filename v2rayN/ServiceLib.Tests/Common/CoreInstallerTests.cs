using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace ServiceLib.Tests.Common;

/// <summary>
/// An engine zip is installed only when it is exactly what the signed index lists: signed size and hash for the zip, signed
/// hashes for each file, only the listed files extracted, and the old files kept or restored if anything goes wrong.
/// </summary>
public class CoreInstallerTests
{
    private static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

    private static string NewDir() => Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"glide-core-{Guid.NewGuid():N}")).FullName;

    private static string Hex(byte[] b) => Convert.ToHexString(b);

    private static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);

    private static string MakeZip(string dir, params (string Path, byte[] Data)[] entries)
    {
        var path = Path.Combine(dir, $"engine-{Guid.NewGuid():N}.zip");
        using var fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        foreach (var (p, data) in entries)
        {
            var e = zip.CreateEntry(p);
            using var s = e.Open();
            s.Write(data);
        }
        return path;
    }

    private static CoreIndexAsset AssetFor(string zip, params (string Name, byte[] Data)[] files) => new()
    {
        Name = Path.GetFileName(zip),
        Url = "https://github.com/XTLS/Xray-core/releases/download/v1/x.zip",
        Size = new FileInfo(zip).Length,
        Sha256 = Sha(File.ReadAllBytes(zip)),
        Files = files.ToDictionary(f => f.Name, f => Sha(f.Data)),
    };

    private static readonly byte[] NewExe = Bytes("new xray binary");
    private static readonly byte[] NewDll = Bytes("new wintun");
    private static readonly byte[] OldExe = Bytes("old xray binary");
    private static readonly byte[] OldDll = Bytes("old wintun");

    [Test]
    public async Task GoodZip_InstallsOnlyTheListedFiles_AndKeepsThePreviousOnes()
    {
        var dir = NewDir();
        var bin = Path.Combine(dir, "bin");
        Directory.CreateDirectory(bin);
        File.WriteAllBytes(Path.Combine(bin, "xray.exe"), OldExe);
        File.WriteAllBytes(Path.Combine(bin, "geoip.dat"), Bytes("users own geo file"));
        var zip = MakeZip(dir, ("xray.exe", NewExe), ("wintun.dll", NewDll), ("geoip.dat", Bytes("from the zip")), ("README.md", Bytes("hi")));
        var asset = AssetFor(zip, ("xray.exe", NewExe), ("wintun.dll", NewDll));

        var r = CoreInstaller.Install(zip, asset, bin);

        await r.Ok.Should().BeTrue();
        await Hex(File.ReadAllBytes(Path.Combine(bin, "xray.exe"))).Should().BeEqualTo(Hex(NewExe));
        await Hex(File.ReadAllBytes(Path.Combine(bin, "wintun.dll"))).Should().BeEqualTo(Hex(NewDll));
        // not listed: left alone / not extracted
        await File.ReadAllText(Path.Combine(bin, "geoip.dat")).Should().BeEqualTo("users own geo file");
        await File.Exists(Path.Combine(bin, "README.md")).Should().BeFalse();
        // the old engine is kept for a manual rollback, and no staging folder is left behind
        await Hex(File.ReadAllBytes(Path.Combine(bin, CoreInstaller.PreviousFolder, "xray.exe"))).Should().BeEqualTo(Hex(OldExe));
        await Directory.GetDirectories(bin, CoreInstaller.StagingPrefix + "*").Should().BeEmpty();
    }

    [Test]
    public async Task VersionedExeInTheZip_IsInstalledUnderTheSignedName_AndNoVersionedFileIsLeftBehind()
    {
        var dir = NewDir();
        var bin = Path.Combine(dir, "bin", "mihomo");
        Directory.CreateDirectory(bin);
        File.WriteAllBytes(Path.Combine(bin, "mihomo.exe"), OldExe);
        var zip = MakeZip(dir, ("mihomo-windows-amd64-v1.19.34.exe", NewExe), ("LICENSE", Bytes("l")));
        var asset = AssetFor(zip, ("mihomo.exe", NewExe));
        asset.Sources = new Dictionary<string, string> { ["mihomo.exe"] = "mihomo-windows-amd64-v1.19.34.exe" };

        var r = CoreInstaller.Install(zip, asset, bin);

        await r.Ok.Should().BeTrue();
        await Hex(File.ReadAllBytes(Path.Combine(bin, "mihomo.exe"))).Should().BeEqualTo(Hex(NewExe));
        await Hex(File.ReadAllBytes(Path.Combine(bin, CoreInstaller.PreviousFolder, "mihomo.exe"))).Should().BeEqualTo(Hex(OldExe));
        await Directory.GetFiles(bin, "*.exe").Length.Should().BeEqualTo(1);
    }

    [Test]
    public async Task SourceNameMissingFromTheZip_IsRefused()
    {
        var dir = NewDir();
        var zip = MakeZip(dir, ("mihomo.exe", NewExe));
        var asset = AssetFor(zip, ("mihomo.exe", NewExe));
        asset.Sources = new Dictionary<string, string> { ["mihomo.exe"] = "mihomo-windows-amd64-v9.9.9.exe" };
        var r = CoreInstaller.Install(zip, asset, Path.Combine(dir, "bin"));
        await r.Error.Should().BeEqualTo(CoreInstallError.MissingEntry);
    }

    [Test]
    public async Task FirstInstall_WorksWhenNothingIsThereYet()
    {
        var dir = NewDir();
        var zip = MakeZip(dir, ("xray.exe", NewExe));
        var r = CoreInstaller.Install(zip, AssetFor(zip, ("xray.exe", NewExe)), Path.Combine(dir, "bin", "xray"));
        await r.Ok.Should().BeTrue();
        await Hex(File.ReadAllBytes(Path.Combine(dir, "bin", "xray", "xray.exe"))).Should().BeEqualTo(Hex(NewExe));
    }

    [Test]
    public async Task ZipOfTheWrongSize_IsRefused_AndNothingChanges()
    {
        var dir = NewDir();
        var bin = Path.Combine(dir, "bin");
        Directory.CreateDirectory(bin);
        File.WriteAllBytes(Path.Combine(bin, "xray.exe"), OldExe);
        var zip = MakeZip(dir, ("xray.exe", NewExe));
        var asset = AssetFor(zip, ("xray.exe", NewExe));
        asset.Size += 1;

        var r = CoreInstaller.Install(zip, asset, bin);

        await r.Error.Should().BeEqualTo(CoreInstallError.SizeMismatch);
        await Hex(File.ReadAllBytes(Path.Combine(bin, "xray.exe"))).Should().BeEqualTo(Hex(OldExe));
    }

    [Test]
    public async Task TamperedZipOfTheSameSize_IsRefused()
    {
        var dir = NewDir();
        var bin = Path.Combine(dir, "bin");
        Directory.CreateDirectory(bin);
        File.WriteAllBytes(Path.Combine(bin, "xray.exe"), OldExe);
        var zip = MakeZip(dir, ("xray.exe", NewExe));
        var asset = AssetFor(zip, ("xray.exe", NewExe));
        var bytes = File.ReadAllBytes(zip);
        bytes[bytes.Length / 2] ^= 0x55;
        File.WriteAllBytes(zip, bytes);

        var r = CoreInstaller.Install(zip, asset, bin);

        await r.Error.Should().BeEqualTo(CoreInstallError.HashMismatch);
        await Hex(File.ReadAllBytes(Path.Combine(bin, "xray.exe"))).Should().BeEqualTo(Hex(OldExe));
    }

    [Test]
    public async Task FileInsideThatDoesNotMatchItsSignedHash_IsRefused_BeforeAnythingIsReplaced()
    {
        var dir = NewDir();
        var bin = Path.Combine(dir, "bin");
        Directory.CreateDirectory(bin);
        File.WriteAllBytes(Path.Combine(bin, "xray.exe"), OldExe);
        File.WriteAllBytes(Path.Combine(bin, "wintun.dll"), OldDll);
        var zip = MakeZip(dir, ("xray.exe", NewExe), ("wintun.dll", NewDll));
        // the zip as a whole is "signed" (size and hash fit) but wintun.dll's listed hash is for something else
        var asset = AssetFor(zip, ("xray.exe", NewExe), ("wintun.dll", Bytes("something else")));

        var r = CoreInstaller.Install(zip, asset, bin);

        await r.Error.Should().BeEqualTo(CoreInstallError.EntryHashMismatch);
        await Hex(File.ReadAllBytes(Path.Combine(bin, "xray.exe"))).Should().BeEqualTo(Hex(OldExe));
        await Hex(File.ReadAllBytes(Path.Combine(bin, "wintun.dll"))).Should().BeEqualTo(Hex(OldDll));
        await Directory.GetDirectories(bin, CoreInstaller.StagingPrefix + "*").Should().BeEmpty();
        await Directory.Exists(Path.Combine(bin, CoreInstaller.PreviousFolder)).Should().BeFalse();
    }

    [Test]
    public async Task ListedFileMissingFromTheZip_IsRefused()
    {
        var dir = NewDir();
        var zip = MakeZip(dir, ("xray.exe", NewExe));
        var r = CoreInstaller.Install(zip, AssetFor(zip, ("xray.exe", NewExe), ("wintun.dll", NewDll)), Path.Combine(dir, "bin"));
        await r.Error.Should().BeEqualTo(CoreInstallError.MissingEntry);
        await File.Exists(Path.Combine(dir, "bin", "xray.exe")).Should().BeFalse();
    }

    [Test]
    public async Task SameLeafNameInTwoFolders_IsAmbiguous_AndRefused()
    {
        var dir = NewDir();
        var zip = MakeZip(dir, ("a/xray.exe", NewExe), ("b/xray.exe", Bytes("other")));
        var r = CoreInstaller.Install(zip, AssetFor(zip, ("xray.exe", NewExe)), Path.Combine(dir, "bin"));
        await r.Error.Should().BeEqualTo(CoreInstallError.AmbiguousEntry);
    }

    [Test]
    [Arguments("..\\evil\\xray.exe")]
    [Arguments("../evil/xray.exe")]
    [Arguments("/abs/xray.exe")]
    [Arguments("C:/x/xray.exe")]
    public async Task EntryWithAnUnsafePath_IsRefused_AndNothingIsWrittenOutside(string entryPath)
    {
        var dir = NewDir();
        var bin = Path.Combine(dir, "engine", "bin");
        var zip = MakeZip(dir, (entryPath, NewExe));
        var r = CoreInstaller.Install(zip, AssetFor(zip, ("xray.exe", NewExe)), bin);
        await r.Ok.Should().BeFalse();
        await File.Exists(Path.Combine(dir, "evil", "xray.exe")).Should().BeFalse();
        await File.Exists(Path.Combine(dir, "engine", "evil", "xray.exe")).Should().BeFalse();
        await File.Exists(Path.Combine(bin, "xray.exe")).Should().BeFalse();
    }

    [Test]
    public async Task NotAZip_IsRefused()
    {
        var dir = NewDir();
        var junk = Path.Combine(dir, "junk.zip");
        File.WriteAllBytes(junk, Bytes("this is not a zip file"));
        var asset = new CoreIndexAsset
        {
            Size = new FileInfo(junk).Length,
            Sha256 = Sha(File.ReadAllBytes(junk)),
            Files = new Dictionary<string, string> { ["xray.exe"] = Sha(NewExe) },
        };
        var r = CoreInstaller.Install(junk, asset, Path.Combine(dir, "bin"));
        await r.Error.Should().BeEqualTo(CoreInstallError.BadArchive);
    }

    [Test]
    public async Task MissingDownload_IsReported()
    {
        var r = CoreInstaller.Install(Path.Combine(NewDir(), "nope.zip"), new CoreIndexAsset(), NewDir());
        await r.Error.Should().BeEqualTo(CoreInstallError.MissingFile);
    }

    [Test]
    public async Task FileLargerThanTheCap_IsRefused()
    {
        var dir = NewDir();
        var big = new byte[4096];
        var zip = MakeZip(dir, ("xray.exe", big));
        var r = CoreInstaller.Install(zip, AssetFor(zip, ("xray.exe", big)), Path.Combine(dir, "bin"), maxFileBytes: 1024);
        await r.Error.Should().BeEqualTo(CoreInstallError.TooLarge);
        await File.Exists(Path.Combine(dir, "bin", "xray.exe")).Should().BeFalse();
    }

    [Test]
    public async Task LockedRunningEngine_RollsBackEverythingAlreadyReplaced()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // sharing violations are a Windows behaviour
        }
        var dir = NewDir();
        var bin = Path.Combine(dir, "bin");
        Directory.CreateDirectory(bin);
        File.WriteAllBytes(Path.Combine(bin, "wintun.dll"), OldDll);
        File.WriteAllBytes(Path.Combine(bin, "xray.exe"), OldExe);
        var zip = MakeZip(dir, ("wintun.dll", NewDll), ("xray.exe", NewExe));
        // wintun.dll is swapped first, then xray.exe (held open by the "running engine") fails
        var asset = AssetFor(zip, ("wintun.dll", NewDll), ("xray.exe", NewExe));

        CoreInstallResult r;
        using (new FileStream(Path.Combine(bin, "xray.exe"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            r = CoreInstaller.Install(zip, asset, bin);
        }

        await r.Error.Should().BeEqualTo(CoreInstallError.SwapFailed);
        await Hex(File.ReadAllBytes(Path.Combine(bin, "wintun.dll"))).Should().BeEqualTo(Hex(OldDll));
        await Hex(File.ReadAllBytes(Path.Combine(bin, "xray.exe"))).Should().BeEqualTo(Hex(OldExe));
        await Directory.GetDirectories(bin, CoreInstaller.StagingPrefix + "*").Should().BeEmpty();
    }
}
