using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ServiceLib.Tests.Services;

/// <summary>
/// Loading the signed engine index with fake downloads (no network), and deciding what an engine may be updated to.
/// The rule under test: nothing unverified ever reaches the update step, and a failure always leaves the engines as they are.
/// </summary>
public class CoreIndexClientTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Hash = new('b', 64);

    private static CoreIndexDocument Doc(long sequence = 3, DateTimeOffset? expires = null)
    {
        var doc = new CoreIndexDocument { Format = 1, Sequence = sequence, Issued = Now.AddDays(-2), Expires = expires ?? Now.AddDays(70) };
        doc.Cores["xray"] = [Entry("26.10.31"), Entry("26.11.9")];
        return doc;
    }

    private static CoreIndexEntry Entry(string version)
    {
        var e = new CoreIndexEntry { Version = version, MinGlide = "1.1.0" };
        e.Assets["win-x64"] = new CoreIndexAsset
        {
            Name = "Xray-windows-64.zip",
            Url = $"https://github.com/XTLS/Xray-core/releases/download/v{version}/Xray-windows-64.zip",
            Size = 100,
            Sha256 = Hash,
            Files = new Dictionary<string, string> { ["xray.exe"] = Hash },
        };
        return e;
    }

    private sealed record Signed(string Json, string Signature, string PublicKey);

    private static Signed SignDoc(CoreIndexDocument doc, ECDsa? key = null)
    {
        using var own = key == null ? ECDsa.Create(ECCurve.NamedCurves.nistP256) : null;
        var k = key ?? own!;
        var json = JsonSerializer.Serialize(doc, CoreIndexJsonContext.Default.CoreIndexDocument);
        var sig = k.SignData(Encoding.UTF8.GetBytes(json), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        return new Signed(json, Convert.ToBase64String(sig), k.ExportSubjectPublicKeyInfoPem());
    }

    private static Func<string, CancellationToken, Task<string?>> Serve(Signed s) => (url, _) =>
        Task.FromResult<string?>(url.EndsWith(UpdateSignature.Extension, StringComparison.Ordinal) ? s.Signature : s.Json);

    private static string StatePath() => Path.Combine(Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"glide-cic-{Guid.NewGuid():N}")).FullName, "state.json");

    [Test]
    public async Task IndexAddress_IsAFixedReleaseAssetOfTheAppRepository()
    {
        await CoreIndexClient.IndexUrl.Should().BeEqualTo("https://github.com/BlackkCore/Glide/releases/download/cores-index/cores-index.json");
    }

    [Test]
    public async Task ValidIndex_IsLoaded_AndItsSequenceRemembered()
    {
        var s = SignDoc(Doc(sequence: 9));
        var state = StatePath();
        var load = await CoreIndexClient.LoadAsync(Serve(s), s.PublicKey, Now, state);
        await load.Document.Should().NotBeNull();
        await load.Message.Should().BeEqualTo("");
        await CoreIndexState.Load(state).Should().BeEqualTo(9);
    }

    [Test]
    public async Task BadSignature_LoadsNothing_AndDoesNotTouchTheRememberedSequence()
    {
        var s = SignDoc(Doc(sequence: 50));
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var state = StatePath();
        CoreIndexState.Save(state, 4);
        var load = await CoreIndexClient.LoadAsync(Serve(s), other.ExportSubjectPublicKeyInfoPem(), Now, state);
        await load.Document.Should().BeNull();
        await load.Message.Should().BeEqualTo(ResUI.MsgCoreIndexUnavailable);
        await CoreIndexState.Load(state).Should().BeEqualTo(4);
    }

    [Test]
    public async Task SignedButExpired_SaysSo_AndLoadsNothing()
    {
        var s = SignDoc(Doc(expires: Now.AddMinutes(-1)));
        var load = await CoreIndexClient.LoadAsync(Serve(s), s.PublicKey, Now, StatePath());
        await load.Document.Should().BeNull();
        await load.Message.Should().BeEqualTo(ResUI.MsgCoreIndexExpired);
    }

    [Test]
    public async Task OlderSignedIndex_IsAReplay_AndIgnored()
    {
        var s = SignDoc(Doc(sequence: 3));
        var state = StatePath();
        CoreIndexState.Save(state, 10);
        var load = await CoreIndexClient.LoadAsync(Serve(s), s.PublicKey, Now, state);
        await load.Document.Should().BeNull();
        await load.Message.Should().BeEqualTo(ResUI.MsgCoreIndexReplay);
    }

    [Test]
    public async Task MissingIndexOrSignature_Downloads_AreUnavailable()
    {
        var s = SignDoc(Doc());
        var noSig = await CoreIndexClient.LoadAsync(
            (url, _) => Task.FromResult<string?>(url.EndsWith(UpdateSignature.Extension, StringComparison.Ordinal) ? null : s.Json), s.PublicKey, Now, StatePath());
        await noSig.Document.Should().BeNull();
        var noIndex = await CoreIndexClient.LoadAsync((_, _) => Task.FromResult<string?>(null), s.PublicKey, Now, StatePath());
        await noIndex.Document.Should().BeNull();
        await noIndex.Message.Should().BeEqualTo(ResUI.MsgCoreIndexUnavailable);
    }

    [Test]
    public async Task NetworkException_IsReportedAsUnavailable_NotThrown()
    {
        var load = await CoreIndexClient.LoadAsync((_, _) => throw new HttpRequestException("no route"), "pem", Now, StatePath());
        await load.Document.Should().BeNull();
        await load.Message.Should().BeEqualTo(ResUI.MsgCoreIndexUnavailable);
    }

    [Test]
    public async Task TheIndexSignedByTheRealKeyIsNotAccepted_WhenOnlyATestKeyIsUsed()
    {
        // The embedded release key must be the one that decides; a file signed by any other key is refused.
        var s = SignDoc(Doc());
        var load = await CoreIndexClient.LoadAsync(Serve(s), UpdateSignatureKey.PublicKeyPem, Now, StatePath());
        await load.Document.Should().BeNull();
    }

    // ---- decisions ---------------------------------------------------------------------------------------------

    private static readonly SemanticVersion Glide = new("1.1.0");

    [Test]
    public async Task Decide_OffersTheNewestVettedBuild_WhenOlderOrNothingIsInstalled()
    {
        var doc = Doc();
        var older = CoreIndexClient.Decide(doc, ECoreType.Xray, "win-x64", Glide, null, new SemanticVersion("26.10.31"));
        await older.Pick!.Version.ToString().Should().BeEqualTo("26.11.9");
        var none = CoreIndexClient.Decide(doc, ECoreType.Xray, "win-x64", Glide, null, new SemanticVersion(0, 0, 0));
        await none.Pick.Should().NotBeNull();
    }

    [Test]
    public async Task Decide_SaysUpToDate_WhenTheInstalledBuildIsTheNewestVettedOne()
    {
        var d = CoreIndexClient.Decide(Doc(), ECoreType.Xray, "win-x64", Glide, null, new SemanticVersion("26.11.9"));
        await d.Pick.Should().BeNull();
        await d.UpToDate.Should().BeTrue();
        // a build newer than anything vetted (installed by hand) is not "downgraded"
        var newer = CoreIndexClient.Decide(Doc(), ECoreType.Xray, "win-x64", Glide, null, new SemanticVersion("27.1.1"));
        await newer.UpToDate.Should().BeTrue();
    }

    [Test]
    public async Task Decide_WhenNothingIsVettedForTheEngine_SaysNoVerifiedUpdate()
    {
        var d = CoreIndexClient.Decide(Doc(), ECoreType.mihomo, "win-x64", Glide, null, new SemanticVersion("1.0.0"));
        await d.Pick.Should().BeNull();
        await d.UpToDate.Should().BeFalse();
        await d.Message.Should().BeEqualTo(string.Format(ResUI.MsgCoreNoVerifiedUpdate, Global.CoreDisplayName(ECoreType.mihomo)));
        var notAnEngine = CoreIndexClient.Decide(Doc(), ECoreType.v2rayN, "win-x64", Glide, null, new SemanticVersion("1.0.0"));
        await notAnEngine.Pick.Should().BeNull();
    }

    [Test]
    public async Task Decide_HonoursTheLockedMaximumVersion()
    {
        var d = CoreIndexClient.Decide(Doc(), ECoreType.Xray, "win-x64", Glide, new SemanticVersion("26.11.0"), new SemanticVersion("26.10.0"));
        await d.Pick!.Version.ToString().Should().BeEqualTo("26.10.31");
    }

    [Test]
    public async Task RevokedWarning_NamesTheWithdrawnInstalledBuild_AndIsSilentOtherwise()
    {
        var doc = Doc();
        doc.Revoked.Add(new CoreIndexRevocation { Core = "xray", Version = "26.10.31", Reason = "CVE-0000-0000" });
        var warn = CoreIndexClient.RevokedWarning(doc, ECoreType.Xray, new SemanticVersion("26.10.31"));
        await warn.Should().NotBeNull();
        await warn!.Contains("CVE-0000-0000").Should().BeTrue();
        await CoreIndexClient.RevokedWarning(doc, ECoreType.Xray, new SemanticVersion("26.11.9")).Should().BeNull();
        await CoreIndexClient.RevokedWarning(doc, ECoreType.mihomo, new SemanticVersion("26.10.31")).Should().BeNull();
    }

    // ---- the registry the installer step trusts --------------------------------------------------------------

    [Test]
    public async Task VerifiedDownloads_AreFoundByNormalisedPath_AndForgotten()
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"glide-vd-{Guid.NewGuid():N}")).FullName;
        var zip = Path.Combine(dir, "a.zip");
        var asset = new CoreIndexAsset { Name = "a" };
        await VerifiedCoreDownloads.TryGet(zip, out _).Should().BeFalse();
        VerifiedCoreDownloads.Register(zip, asset);
        await VerifiedCoreDownloads.TryGet(Path.Combine(dir, ".", "A.ZIP"), out var found).Should().BeTrue();
        await ReferenceEquals(found, asset).Should().BeTrue();
        VerifiedCoreDownloads.Remove(zip);
        await VerifiedCoreDownloads.TryGet(zip, out _).Should().BeFalse();
    }

    [Test]
    public async Task OnlyTheThreeEngines_UseTheIndex_AndOnlyOnWindowsPortable()
    {
        // Installed copies (marker file) never update engines; the unit-test process has no marker, so this is the portable case.
        await UpdateService.UsesCoreIndex(ECoreType.Xray).Should().BeEqualTo(OperatingSystem.IsWindows());
        await UpdateService.UsesCoreIndex(ECoreType.v2rayN).Should().BeFalse();
        await UpdateService.UsesCoreIndex(ECoreType.hysteria).Should().BeFalse();
    }

    // ---- the whole chain ---------------------------------------------------------------------------------------

    private static string MakeEngineZip(string dir, byte[] exe, byte[] dll)
    {
        var path = Path.Combine(dir, $"x-{Guid.NewGuid():N}.zip");
        using (var fs = File.Create(path))
        using (var zip = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Create))
        {
            foreach (var (name, data) in new[] { ("xray.exe", exe), ("wintun.dll", dll), ("geoip.dat", new byte[] { 1 }) })
            {
                using var s = zip.CreateEntry(name).Open();
                s.Write(data);
            }
        }
        return path;
    }

    private static string HexSha(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

    [Test]
    public async Task Chain_SignedIndex_ThenVerifiedDownload_ThenInstall_AndATamperedDownloadNeverBecomesInstallable()
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"glide-flow-{Guid.NewGuid():N}")).FullName;
        var bin = Path.Combine(dir, "bin", "xray");
        Directory.CreateDirectory(bin);
        var oldExe = new byte[] { 9, 9, 9 };
        File.WriteAllBytes(Path.Combine(bin, "xray.exe"), oldExe);
        var exe = Encoding.UTF8.GetBytes("new xray"); var dll = Encoding.UTF8.GetBytes("wintun");
        var upstream = MakeEngineZip(dir, exe, dll);

        // the maintainer's side: an entry for exactly this zip, signed
        var doc = new CoreIndexDocument { Format = 1, Sequence = 4, Issued = Now.AddDays(-1), Expires = Now.AddDays(60) };
        var entry = new CoreIndexEntry { Version = "26.11.9", MinGlide = "1.1.0" };
        entry.Assets["win-x64"] = new CoreIndexAsset
        {
            Name = "Xray-windows-64.zip",
            Url = "https://github.com/XTLS/Xray-core/releases/download/v26.11.9/Xray-windows-64.zip",
            Size = new FileInfo(upstream).Length,
            Sha256 = HexSha(File.ReadAllBytes(upstream)),
            Files = new Dictionary<string, string> { ["xray.exe"] = HexSha(exe), ["wintun.dll"] = HexSha(dll) },
        };
        doc.Cores["xray"] = [entry];
        var s = SignDoc(doc);

        // the app's side: load, decide, "download", verify, register, install
        var load = await CoreIndexClient.LoadAsync(Serve(s), s.PublicKey, Now, StatePath());
        var decision = CoreIndexClient.Decide(load.Document!, ECoreType.Xray, "win-x64", Glide, null, new SemanticVersion("26.10.31"));
        await decision.Pick.Should().NotBeNull();
        var asset = decision.Pick!.Asset;

        var good = Path.Combine(dir, "download-good.zip");
        File.Copy(upstream, good);
        await CoreInstaller.VerifyArchive(good, asset).Ok.Should().BeTrue();
        VerifiedCoreDownloads.Register(good, asset);
        await VerifiedCoreDownloads.TryGet(good, out var registered).Should().BeTrue();
        await CoreInstaller.Install(good, registered!, bin).Ok.Should().BeTrue();
        await File.ReadAllText(Path.Combine(bin, "xray.exe")).Should().BeEqualTo("new xray");
        await File.Exists(Path.Combine(bin, "geoip.dat")).Should().BeFalse();

        // a download that was changed on the way never passes the check, so it is never registered and never installed
        var tampered = Path.Combine(dir, "download-bad.zip");
        var bytes = File.ReadAllBytes(upstream);
        bytes[bytes.Length / 3] ^= 0x10;
        File.WriteAllBytes(tampered, bytes);
        await CoreInstaller.VerifyArchive(tampered, asset).Ok.Should().BeFalse();
        await VerifiedCoreDownloads.TryGet(tampered, out _).Should().BeFalse();
    }
}
