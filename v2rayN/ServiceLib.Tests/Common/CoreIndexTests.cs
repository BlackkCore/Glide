using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ServiceLib.Tests.Common;

/// <summary>
/// The signed engine index: an engine is offered only when the index verifies against Glide's release key, is current,
/// is not older than one already accepted, and lists the build. Every test signs with a throwaway key made here.
/// </summary>
public class CoreIndexTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Hash = new('a', 64);

    private static CoreIndexDocument Doc(long sequence = 5, int format = 1, DateTimeOffset? issued = null, DateTimeOffset? expires = null)
    {
        var doc = new CoreIndexDocument
        {
            Format = format,
            Sequence = sequence,
            Issued = issued ?? Now.AddDays(-1),
            Expires = expires ?? Now.AddDays(60),
        };
        doc.Cores["xray"] =
        [
            Entry("26.10.31", "1.1.0"),
            Entry("26.11.9", "1.1.0"),
            Entry("26.12.1", "1.2.0"),
        ];
        doc.Cores["singBox"] = [Entry("1.14.3", "1.1.0")];
        return doc;
    }

    private static CoreIndexEntry Entry(string version, string minGlide)
    {
        var e = new CoreIndexEntry { Version = version, MinGlide = minGlide, VettedOn = "2026-11-15" };
        e.Assets["win-x64"] = Asset("Xray-windows-64.zip", version);
        e.Assets["win-x86"] = Asset("Xray-windows-32.zip", version);
        return e;
    }

    private static CoreIndexAsset Asset(string name, string version) => new()
    {
        Name = name,
        Url = $"https://github.com/XTLS/Xray-core/releases/download/v{version}/{name}",
        Size = 15_000_000,
        Sha256 = Hash,
        Files = new Dictionary<string, string> { ["xray.exe"] = Hash },
    };

    private static (byte[] Bytes, string Signature, string PublicKey) Sign(CoreIndexDocument doc, ECDsa? key = null)
    {
        using var own = key == null ? ECDsa.Create(ECCurve.NamedCurves.nistP256) : null;
        var k = key ?? own!;
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(doc, CoreIndexJsonContext.Default.CoreIndexDocument));
        var sig = k.SignData(bytes, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        return (bytes, Convert.ToBase64String(sig), k.ExportSubjectPublicKeyInfoPem());
    }

    [Test]
    public async Task ValidIndex_IsAccepted()
    {
        var (bytes, sig, pub) = Sign(Doc());
        var r = CoreIndexVerifier.Verify(bytes, sig, pub, Now, 0);
        await r.Ok.Should().BeTrue();
        await r.Document!.Sequence.Should().BeEqualTo(5);
    }

    [Test]
    public async Task TamperedBytes_AreRefused()
    {
        var (bytes, sig, pub) = Sign(Doc());
        var tampered = (byte[])bytes.Clone();
        tampered[10] ^= 0x01;
        var r = CoreIndexVerifier.Verify(tampered, sig, pub, Now, 0);
        await r.Ok.Should().BeFalse();
        await r.Error.Should().BeEqualTo(CoreIndexError.BadSignature);
    }

    [Test]
    public async Task SignatureFromAnotherKey_IsRefused()
    {
        var (bytes, sig, _) = Sign(Doc());
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var r = CoreIndexVerifier.Verify(bytes, sig, other.ExportSubjectPublicKeyInfoPem(), Now, 0);
        await r.Error.Should().BeEqualTo(CoreIndexError.BadSignature);
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments("not base64 !!!")]
    public async Task MissingOrBrokenSignature_IsRefused(string? signature)
    {
        var (bytes, _, pub) = Sign(Doc());
        var r = CoreIndexVerifier.Verify(bytes, signature, pub, Now, 0);
        await r.Error.Should().BeEqualTo(CoreIndexError.BadSignature);
    }

    [Test]
    public async Task NoPublicKeyBuiltIn_IsRefused()
    {
        var (bytes, sig, _) = Sign(Doc());
        await CoreIndexVerifier.Verify(bytes, sig, "", Now, 0).Error.Should().BeEqualTo(CoreIndexError.BadSignature);
    }

    [Test]
    public async Task SignedGarbage_IsMalformed_NotAccepted()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var bytes = Encoding.UTF8.GetBytes("this is not json");
        var sig = Convert.ToBase64String(key.SignData(bytes, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));
        var r = CoreIndexVerifier.Verify(bytes, sig, key.ExportSubjectPublicKeyInfoPem(), Now, 0);
        await r.Error.Should().BeEqualTo(CoreIndexError.Malformed);
    }

    [Test]
    public async Task EmptyOrHugeInput_IsRefusedBeforeAnythingElse()
    {
        await CoreIndexVerifier.Verify(null, "x", "y", Now, 0).Error.Should().BeEqualTo(CoreIndexError.Malformed);
        await CoreIndexVerifier.Verify([], "x", "y", Now, 0).Error.Should().BeEqualTo(CoreIndexError.Malformed);
        await CoreIndexVerifier.Verify(new byte[CoreIndexVerifier.MaxIndexBytes + 1], "x", "y", Now, 0).Error.Should().BeEqualTo(CoreIndexError.Malformed);
    }

    [Test]
    public async Task ExpiredIndex_IsRefused()
    {
        var (bytes, sig, pub) = Sign(Doc(expires: Now.AddSeconds(-1)));
        await CoreIndexVerifier.Verify(bytes, sig, pub, Now, 0).Error.Should().BeEqualTo(CoreIndexError.Expired);
    }

    [Test]
    public async Task IndexDatedInTheFuture_IsRefused_ButOneDayOfClockSkewIsTolerated()
    {
        var future = Sign(Doc(issued: Now.AddDays(3), expires: Now.AddDays(60)));
        await CoreIndexVerifier.Verify(future.Bytes, future.Signature, future.PublicKey, Now, 0).Error.Should().BeEqualTo(CoreIndexError.NotYetValid);
        var skew = Sign(Doc(issued: Now.AddHours(20), expires: Now.AddDays(60)));
        await CoreIndexVerifier.Verify(skew.Bytes, skew.Signature, skew.PublicKey, Now, 0).Ok.Should().BeTrue();
    }

    [Test]
    public async Task IndexClaimingTooLongALife_IsRefused()
    {
        var (bytes, sig, pub) = Sign(Doc(issued: Now.AddDays(-1), expires: Now.AddDays(400)));
        await CoreIndexVerifier.Verify(bytes, sig, pub, Now, 0).Error.Should().BeEqualTo(CoreIndexError.TooLongLived);
    }

    [Test]
    public async Task OlderSequence_IsAReplay_SameOrNewerIsFine()
    {
        var (bytes, sig, pub) = Sign(Doc(sequence: 5));
        await CoreIndexVerifier.Verify(bytes, sig, pub, Now, 6).Error.Should().BeEqualTo(CoreIndexError.Replay);
        await CoreIndexVerifier.Verify(bytes, sig, pub, Now, 5).Ok.Should().BeTrue();
        await CoreIndexVerifier.Verify(bytes, sig, pub, Now, 4).Ok.Should().BeTrue();
    }

    [Test]
    public async Task UnknownFormat_IsRefusedSafely()
    {
        var (bytes, sig, pub) = Sign(Doc(format: 2));
        await CoreIndexVerifier.Verify(bytes, sig, pub, Now, 0).Error.Should().BeEqualTo(CoreIndexError.UnsupportedFormat);
    }

    [Test]
    [Arguments("http://github.com/XTLS/Xray-core/releases/download/v1/x.zip")]
    [Arguments("https://evil.example/x.zip")]
    [Arguments("https://github.com.evil.example/x.zip")]
    [Arguments("ftp://github.com/x.zip")]
    [Arguments("not a url")]
    public async Task DownloadAddressOutsideGithubHttps_IsRefused(string url)
    {
        var doc = Doc();
        doc.Cores["xray"][0].Assets["win-x64"].Url = url;
        var (bytes, sig, pub) = Sign(doc);
        await CoreIndexVerifier.Verify(bytes, sig, pub, Now, 0).Error.Should().BeEqualTo(CoreIndexError.Malformed);
    }

    [Test]
    [Arguments("..\\evil.exe")]
    [Arguments("../evil.exe")]
    [Arguments("sub/xray.exe")]
    [Arguments("C:\\Windows\\x.exe")]
    [Arguments("xray.txt")]
    [Arguments(".hidden.exe")]
    [Arguments("")]
    public async Task FileNamesThatCouldEscapeTheEngineFolder_AreRefused(string name)
    {
        var doc = Doc();
        doc.Cores["xray"][0].Assets["win-x64"].Files = new Dictionary<string, string> { [name] = Hash };
        var (bytes, sig, pub) = Sign(doc);
        await CoreIndexVerifier.Verify(bytes, sig, pub, Now, 0).Error.Should().BeEqualTo(CoreIndexError.Malformed);
    }

    [Test]
    [Arguments("..\evil.exe")]
    [Arguments("sub/xray.exe")]
    [Arguments("x.txt")]
    public async Task SourceNameThatCouldEscapeOrIsNotAnExecutable_IsRefused(string source)
    {
        var doc = Doc();
        doc.Cores["xray"][0].Assets["win-x64"].Sources = new Dictionary<string, string> { ["xray.exe"] = source };
        var (bytes, sig, pub) = Sign(doc);
        await CoreIndexVerifier.Verify(bytes, sig, pub, Now, 0).Error.Should().BeEqualTo(CoreIndexError.Malformed);
    }

    [Test]
    public async Task SourceForAFileThatIsNotListed_IsRefused_AndAValidRenameIsAccepted()
    {
        var doc = Doc();
        doc.Cores["xray"][0].Assets["win-x64"].Sources = new Dictionary<string, string> { ["other.exe"] = "mihomo-windows-amd64-v1.exe" };
        var bad = Sign(doc);
        await CoreIndexVerifier.Verify(bad.Bytes, bad.Signature, bad.PublicKey, Now, 0).Error.Should().BeEqualTo(CoreIndexError.Malformed);

        var ok = Doc();
        ok.Cores["xray"][0].Assets["win-x64"].Sources = new Dictionary<string, string> { ["xray.exe"] = "mihomo-windows-amd64-v1.19.34.exe" };
        var good = Sign(ok);
        await CoreIndexVerifier.Verify(good.Bytes, good.Signature, good.PublicKey, Now, 0).Ok.Should().BeTrue();
    }

    [Test]
    [Arguments("")]
    [Arguments("abc")]
    [Arguments("ZZZZ0000000000000000000000000000000000000000000000000000000000AA")]
    public async Task BadHash_IsRefused(string hash)
    {
        var doc = Doc();
        doc.Cores["xray"][0].Assets["win-x64"].Sha256 = hash;
        var (bytes, sig, pub) = Sign(doc);
        await CoreIndexVerifier.Verify(bytes, sig, pub, Now, 0).Error.Should().BeEqualTo(CoreIndexError.Malformed);
    }

    [Test]
    public async Task ZeroOrHugeSize_IsRefused()
    {
        foreach (var size in new[] { 0L, -5L, CoreIndexVerifier.MaxAssetBytes + 1 })
        {
            var doc = Doc();
            doc.Cores["xray"][0].Assets["win-x64"].Size = size;
            var (bytes, sig, pub) = Sign(doc);
            await CoreIndexVerifier.Verify(bytes, sig, pub, Now, 0).Error.Should().BeEqualTo(CoreIndexError.Malformed);
        }
    }

    // ---- choosing a build -------------------------------------------------------------------------------------

    [Test]
    public async Task Pick_TakesTheNewestBuildThisGlideMayInstall()
    {
        var doc = Doc();
        // 26.12.1 needs Glide 1.2.0, so Glide 1.1.0 gets 26.11.9
        var pick = CoreIndexVerifier.Pick(doc, "xray", "win-x64", new SemanticVersion("1.1.0"));
        await pick.Should().NotBeNull();
        await pick!.Entry.Version.Should().BeEqualTo("26.11.9");
        var newer = CoreIndexVerifier.Pick(doc, "xray", "win-x64", new SemanticVersion("1.2.0"));
        await newer!.Entry.Version.Should().BeEqualTo("26.12.1");
    }

    [Test]
    public async Task Pick_SkipsWithdrawnVersions()
    {
        var doc = Doc();
        doc.Revoked.Add(new CoreIndexRevocation { Core = "xray", Version = "26.11.9", Reason = "CVE" });
        var pick = CoreIndexVerifier.Pick(doc, "xray", "win-x64", new SemanticVersion("1.1.0"));
        await pick!.Entry.Version.Should().BeEqualTo("26.10.31");
        await CoreIndexVerifier.IsRevoked(doc, "xray", "26.11.9").Should().BeTrue();
        await CoreIndexVerifier.RevocationOf(doc, "xray", "26.11.9")!.Reason.Should().BeEqualTo("CVE");
        await CoreIndexVerifier.IsRevoked(doc, "xray", "26.10.31").Should().BeFalse();
    }

    [Test]
    public async Task Pick_RespectsALockedMaximum()
    {
        var doc = Doc();
        var pick = CoreIndexVerifier.Pick(doc, "xray", "win-x64", new SemanticVersion("1.2.0"), new SemanticVersion("26.11.0"));
        await pick!.Entry.Version.Should().BeEqualTo("26.10.31");
    }

    [Test]
    public async Task Pick_ReturnsNothingForUnknownCoreArchitectureOrWhenEverythingIsWithdrawn()
    {
        var doc = Doc();
        await CoreIndexVerifier.Pick(doc, "mihomo", "win-x64", new SemanticVersion("1.1.0")).Should().BeNull();
        await CoreIndexVerifier.Pick(doc, "xray", "win-arm64", new SemanticVersion("1.1.0")).Should().BeNull();
        await CoreIndexVerifier.Pick(doc, "singBox", "win-x64", new SemanticVersion("1.0.0")).Should().BeNull();
        doc.Revoked.Add(new CoreIndexRevocation { Core = "singBox", Version = "1.14.3" });
        await CoreIndexVerifier.Pick(doc, "singBox", "win-x64", new SemanticVersion("1.1.0")).Should().BeNull();
    }

    [Test]
    public async Task Keys_MapOnlyTheThreeEngines()
    {
        await CoreIndexKeys.For(ECoreType.Xray).Should().BeEqualTo("xray");
        await CoreIndexKeys.For(ECoreType.sing_box).Should().BeEqualTo("singBox");
        await CoreIndexKeys.For(ECoreType.mihomo).Should().BeEqualTo("mihomo");
        await CoreIndexKeys.For(ECoreType.v2rayN).Should().BeNull();
        await CoreIndexKeys.For(ECoreType.hysteria).Should().BeNull();
    }

    // ---- remembering the highest sequence ----------------------------------------------------------------------

    private static string TempState() => Path.Combine(Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"glide-idx-{Guid.NewGuid():N}")).FullName, "state.json");

    [Test]
    public async Task State_StartsAtZero_RaisesButNeverLowers()
    {
        var path = TempState();
        await CoreIndexState.Load(path).Should().BeEqualTo(0);
        CoreIndexState.Save(path, 7);
        await CoreIndexState.Load(path).Should().BeEqualTo(7);
        CoreIndexState.Save(path, 3);
        await CoreIndexState.Load(path).Should().BeEqualTo(7);
        CoreIndexState.Save(path, 9);
        await CoreIndexState.Load(path).Should().BeEqualTo(9);
        await File.Exists(path + ".tmp").Should().BeFalse();
    }

    [Test]
    public async Task State_DamagedFile_IsTreatedAsNothingSeen()
    {
        var path = TempState();
        File.WriteAllText(path, "{ not json");
        await CoreIndexState.Load(path).Should().BeEqualTo(0);
        CoreIndexState.Save(path, 2);
        await CoreIndexState.Load(path).Should().BeEqualTo(2);
    }

    // ---- made by another implementation --------------------------------------------------------------------------

    // scripts/cores/make-cores-index.mjs (Node) built and signed this with a throwaway key (its private half was discarded),
    // to prove the generator and the app agree on the bytes, the signature format (base64 DER) and the JSON shape.
    // The index is embedded as base64 so line endings in this source file cannot change the signed bytes.
    private const string NodeIndexBase64 = "ewogICJmb3JtYXQiOiAxLAogICJzZXF1ZW5jZSI6IDEyLAogICJpc3N1ZWQiOiAiMjAyNi0xMS0xMFQwMDowMDowMFoiLAogICJleHBpcmVzIjogIjIwMjYtMTItMjRUMDA6MDA6MDBaIiwKICAiY29yZXMiOiB7CiAgICAieHJheSI6IFsKICAgICAgewogICAgICAgICJ2ZXJzaW9uIjogIjI2LjExLjkiLAogICAgICAgICJtaW5HbGlkZSI6ICIxLjEuMCIsCiAgICAgICAgInZldHRlZE9uIjogIjIwMjYtMTEtMTIiLAogICAgICAgICJhc3NldHMiOiB7CiAgICAgICAgICAid2luLXg2NCI6IHsKICAgICAgICAgICAgIm5hbWUiOiAiWHJheS13aW5kb3dzLTY0LnppcCIsCiAgICAgICAgICAgICJ1cmwiOiAiaHR0cHM6Ly9naXRodWIuY29tL1hUTFMvWHJheS1jb3JlL3JlbGVhc2VzL2Rvd25sb2FkL3YyNi4xMS45L1hyYXktd2luZG93cy02NC56aXAiLAogICAgICAgICAgICAic2l6ZSI6IDE1NDU4MzA0LAogICAgICAgICAgICAic2hhMjU2IjogIjExMTExMTExMTExMTExMTExMTExMTExMTExMTExMTExMTExMTExMTExMTExMTExMTExMTExMTExMTExMTExMTEiLAogICAgICAgICAgICAiZmlsZXMiOiB7CiAgICAgICAgICAgICAgInhyYXkuZXhlIjogIjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIiLAogICAgICAgICAgICAgICJ3aW50dW4uZGxsIjogIjMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMiCiAgICAgICAgICAgIH0KICAgICAgICAgIH0KICAgICAgICB9CiAgICAgIH0KICAgIF0sCiAgICAic2luZ0JveCI6IFtdLAogICAgIm1paG9tbyI6IFtdCiAgfSwKICAicmV2b2tlZCI6IFsKICAgIHsKICAgICAgImNvcmUiOiAieHJheSIsCiAgICAgICJ2ZXJzaW9uIjogIjI2LjEwLjEiLAogICAgICAicmVhc29uIjogImNhZmUgXHUwNjQxXHUwNjI3XHUwNjMxXHUwNjMzXHUwNmNjIgogICAgfQogIF0KfQo=";
    private const string NodeSignature = "MEUCIQCN/3ec0QOWogdCY3rXYMm0lE/Vi2a3aLaIdbVHz6KwVQIgN9cE6RNY54bbM0pgWGl9LFg8Td+en4BgeMlIoakWsd4=";
    private const string NodePublicKey = """
        -----BEGIN PUBLIC KEY-----
        MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEeTu0Bx6hd7MK3CpsVCJ0uGTBo+vx
        hQW0uak6vTSyxeiAxhgqVjnWEAI3KYuH2rMUNZKf8z4YtWXEQETlw4cxMA==
        -----END PUBLIC KEY-----
        """;

    [Test]
    public async Task IndexMadeAndSignedByTheNodeGenerator_IsAcceptedByTheApp()
    {
        var bytes = Convert.FromBase64String(NodeIndexBase64);
        var r = CoreIndexVerifier.Verify(bytes, NodeSignature, NodePublicKey, Now, 0);
        await r.Ok.Should().BeTrue();
        await r.Document!.Sequence.Should().BeEqualTo(12);
        var pick = CoreIndexVerifier.Pick(r.Document, "xray", "win-x64", new SemanticVersion("1.1.0"));
        await pick!.Entry.Version.Should().BeEqualTo("26.11.9");
        await pick.Asset.Files.Count.Should().BeEqualTo(2);
        await pick.Asset.Size.Should().BeEqualTo(15458304);
        // the non-ASCII revocation reason survived the \uXXXX escapes
        await r.Document.Revoked[0].Reason!.StartsWith("cafe ").Should().BeTrue();
    }

    [Test]
    public async Task NodeIndex_ChangedByOneByte_IsRefused()
    {
        var bytes = Convert.FromBase64String(NodeIndexBase64);
        bytes[bytes.Length - 5] ^= 0x01;
        await CoreIndexVerifier.Verify(bytes, NodeSignature, NodePublicKey, Now, 0).Error.Should().BeEqualTo(CoreIndexError.BadSignature);
    }
}
