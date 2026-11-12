using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ServiceLib.Tests.Services;

/// <summary>
/// The whole portable engine-update path with the real code and nothing mocked except GitHub: a fake server behind the real
/// <see cref="DownloadService"/>, the real <see cref="UpdateService"/>, the staged installer, and a disposable key per test.
/// What must hold: a good signed index and download install; every other case changes nothing and says why.
/// </summary>
public class CoreUpdateIntegrationTests
{
    private const string IndexUrl = "https://github.com/BlackkCore/Glide/releases/download/cores-index/cores-index.json";
    private const string AssetUrl = "https://github.com/XTLS/Xray-core/releases/download/v26.11.9/Xray-windows-64.zip";

    private sealed class FakeGitHub : HttpMessageHandler
    {
        public Dictionary<string, byte[]> Files { get; } = [];
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            lock (Requests)
            {
                Requests.Add($"{request.Method} {url}");
            }
            if (!Files.TryGetValue(url, out var data))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            HttpResponseMessage response;
            var range = request.Headers.Range?.Ranges.FirstOrDefault();
            if (request.Method == HttpMethod.Head)
            {
                response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
                response.Content.Headers.ContentLength = data.Length;
            }
            else if (range != null)
            {
                var from = range.From ?? 0;
                var to = Math.Min(range.To ?? data.Length - 1, data.Length - 1);
                response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(data[(int)from..(int)(to + 1)]) };
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, data.Length);
            }
            else
            {
                response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data) };
            }
            response.Headers.AcceptRanges.Add("bytes");
            return Task.FromResult(response);
        }

        public bool Asked(string url) => Requests.Any(r => r.EndsWith(" " + url, StringComparison.Ordinal));
    }

    private sealed class Bench
    {
        public required FakeGitHub Server { get; init; }
        public required string PublicKey { get; init; }
        public required string StatePath { get; init; }
        public required string Dir { get; init; }
        public required byte[] Zip { get; init; }
        public required ECDsa Key { get; init; }
        public List<(bool Success, string Msg)> Messages { get; } = [];
        public string? Ready => Messages.LastOrDefault(m => m.Success).Msg;
    }

    private static byte[] BuildZip(string exeText, string dllText)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            foreach (var (name, text) in new[] { ("xray.exe", exeText), ("wintun.dll", dllText), ("geoip.dat", "from the zip: must be ignored") })
            {
                using var w = zip.CreateEntry(name).Open();
                w.Write(Encoding.UTF8.GetBytes(text));
            }
        }
        return ms.ToArray();
    }

    private static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

    /// <summary>Publishes a signed index (and the engine zip) on the fake server, with a key made just for this test.</summary>
    private static Bench Publish(
        long sequence = 8, DateTimeOffset? expires = null, Func<byte[], byte[]>? serveZipAs = null, Func<string, string>? alterIndexJson = null, bool signWithOtherKey = false)
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"glide-it-{Guid.NewGuid():N}")).FullName;
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var zip = BuildZip("xray 26.11.9 binary", "wintun dll");
        var now = DateTimeOffset.UtcNow;

        var doc = new CoreIndexDocument { Format = 1, Sequence = sequence, Issued = now.AddDays(-1), Expires = expires ?? now.AddDays(60) };
        var entry = new CoreIndexEntry { Version = "26.11.9", MinGlide = "1.0.0", VettedOn = "2026-11-15" };
        entry.Assets["win-x64"] = new CoreIndexAsset
        {
            Name = "Xray-windows-64.zip",
            Url = AssetUrl,
            Size = zip.Length,
            Sha256 = Sha(zip),
            Files = new Dictionary<string, string>
            {
                ["xray.exe"] = Sha(Encoding.UTF8.GetBytes("xray 26.11.9 binary")),
                ["wintun.dll"] = Sha(Encoding.UTF8.GetBytes("wintun dll")),
            },
        };
        doc.Cores["xray"] = [entry];

        var json = JsonSerializer.Serialize(doc, CoreIndexJsonContext.Default.CoreIndexDocument);
        using var signer = signWithOtherKey ? ECDsa.Create(ECCurve.NamedCurves.nistP256) : null;
        var sig = Convert.ToBase64String((signer ?? key).SignData(Encoding.UTF8.GetBytes(json), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));
        var served = alterIndexJson?.Invoke(json) ?? json; // altered AFTER signing: what an attacker in the middle could do

        var server = new FakeGitHub();
        server.Files[IndexUrl] = Encoding.UTF8.GetBytes(served);
        server.Files[IndexUrl + UpdateSignature.Extension] = Encoding.UTF8.GetBytes(sig);
        server.Files[AssetUrl] = serveZipAs?.Invoke(zip) ?? zip;
        return new Bench { Server = server, PublicKey = key.ExportSubjectPublicKeyInfoPem(), StatePath = Path.Combine(dir, "state.json"), Dir = dir, Zip = zip, Key = key };
    }

    private static async Task RunCheckUpdate(Bench b, CancellationToken ct = default)
    {
        await CertPemManager.Instance.Init(new Config { GuiItem = new GUIItem() });
        using var ds = new DownloadService { Handler = b.Server };
        var service = new UpdateService(ds, new Config(), (ok, msg) =>
        {
            lock (b.Messages)
            {
                b.Messages.Add((ok, msg));
            }
            return Task.CompletedTask;
        })
        {
            IndexOptions = new CoreIndexOptions(IndexUrl, b.PublicKey, b.StatePath),
        };
        await service.CheckUpdateCore(ECoreType.Xray, false, ct);
    }

    private static string AllMessages(Bench b) => string.Join(" | ", b.Messages.Select(m => m.Msg));

    // ---- the good path ----------------------------------------------------------------------------------------

    [Test]
    public async Task SignedIndex_ThenDownload_ThenVerifiedInstall_ReplacesTheEngine()
    {
        var b = Publish();
        await RunCheckUpdate(b);

        await b.Ready.Should().NotBeNull();                       // "download ready" was reported
        await File.Exists(b.Ready!).Should().BeTrue();
        await VerifiedCoreDownloads.TryGet(b.Ready!, out _).Should().BeTrue();
        await b.Server.Asked(IndexUrl).Should().BeTrue();
        await b.Server.Asked(IndexUrl + UpdateSignature.Extension).Should().BeTrue();
        await CoreIndexState.Load(b.StatePath).Should().BeEqualTo(8);   // the sequence was remembered

        var bin = Path.Combine(b.Dir, "bin", "xray");
        Directory.CreateDirectory(bin);
        File.WriteAllText(Path.Combine(bin, "xray.exe"), "old xray");
        var installed = VerifiedCoreInstall.Install(b.Ready!, bin);

        await installed.Ok.Should().BeTrue();
        await File.ReadAllText(Path.Combine(bin, "xray.exe")).Should().BeEqualTo("xray 26.11.9 binary");
        await File.ReadAllText(Path.Combine(bin, "wintun.dll")).Should().BeEqualTo("wintun dll");
        await File.ReadAllText(Path.Combine(bin, CoreInstaller.PreviousFolder, "xray.exe")).Should().BeEqualTo("old xray");
        await File.Exists(Path.Combine(bin, "geoip.dat")).Should().BeFalse();       // not listed, so not extracted
        await VerifiedCoreDownloads.TryGet(b.Ready!, out _).Should().BeFalse();    // single use
    }

    [Test]
    public async Task CheckOnly_FindsTheVettedVersion_WithoutDownloadingTheEngine()
    {
        var b = Publish();
        await CertPemManager.Instance.Init(new Config { GuiItem = new GUIItem() });
        using var ds = new DownloadService { Handler = b.Server };
        var service = new UpdateService(ds, new Config(), (_, _) => Task.CompletedTask)
        {
            IndexOptions = new CoreIndexOptions(IndexUrl, b.PublicKey, b.StatePath),
        };
        var result = await service.CheckHasUpdateOnly(ECoreType.Xray, false);
        await result.Success.Should().BeTrue();
        await result.Version!.ToString().Should().BeEqualTo("26.11.9");
        await b.Server.Asked(AssetUrl).Should().BeFalse();
    }

    // ---- everything that must be refused ----------------------------------------------------------------------

    [Test]
    public async Task IndexAlteredInTransit_IsRefused_AndTheEngineIsNeverRequested()
    {
        var b = Publish(alterIndexJson: j => j.Replace("26.11.9", "26.11.8"));
        await RunCheckUpdate(b);
        await b.Ready.Should().BeNull();
        await b.Server.Asked(AssetUrl).Should().BeFalse();
        await AllMessages(b).Contains(ResUI.MsgCoreIndexUnavailable).Should().BeTrue();
        await CoreIndexState.Load(b.StatePath).Should().BeEqualTo(0);
    }

    [Test]
    public async Task IndexSignedByAnotherKey_IsRefused()
    {
        var b = Publish(signWithOtherKey: true);
        await RunCheckUpdate(b);
        await b.Ready.Should().BeNull();
        await b.Server.Asked(AssetUrl).Should().BeFalse();
    }

    [Test]
    public async Task EngineZipSwappedOnTheServer_IsDownloaded_ThenDiscarded_AndNeverBecomesInstallable()
    {
        var b = Publish(serveZipAs: z => { var c = (byte[])z.Clone(); c[c.Length / 2] ^= 0x7f; return c; });
        await RunCheckUpdate(b);
        await b.Server.Asked(AssetUrl).Should().BeTrue();           // it was fetched...
        await b.Ready.Should().BeNull();                            // ...but never reported ready
        await AllMessages(b).Contains(string.Format(ResUI.MsgCoreVerifyFailed, Global.CoreDisplayName(ECoreType.Xray))).Should().BeTrue();
        // the tampered file itself is gone from the temp folder and was never registered (matched by its content: tests run in parallel)
        var tamperedHash = Sha(b.Server.Files[AssetUrl]);
        var copies = Directory.GetFiles(Utils.GetTempPath(), "*.zip").Where(f => Sha(File.ReadAllBytes(f)) == tamperedHash).ToList();
        await copies.Count.Should().BeEqualTo(0);
    }

    [Test]
    public async Task EngineZipOfTheWrongLength_IsDiscarded()
    {
        var b = Publish(serveZipAs: z => z.Take(z.Length - 10).ToArray());
        await RunCheckUpdate(b);
        await b.Ready.Should().BeNull();
    }

    [Test]
    public async Task ExpiredIndex_IsRefusedWithItsOwnMessage()
    {
        var b = Publish(expires: DateTimeOffset.UtcNow.AddMinutes(-5));
        await RunCheckUpdate(b);
        await b.Ready.Should().BeNull();
        await b.Server.Asked(AssetUrl).Should().BeFalse();
        await AllMessages(b).Contains(ResUI.MsgCoreIndexExpired).Should().BeTrue();
    }

    [Test]
    public async Task OlderIndexThanOneAlreadyAccepted_IsAReplay()
    {
        var b = Publish(sequence: 3);
        CoreIndexState.Save(b.StatePath, 10);
        await RunCheckUpdate(b);
        await b.Ready.Should().BeNull();
        await b.Server.Asked(AssetUrl).Should().BeFalse();
        await AllMessages(b).Contains(ResUI.MsgCoreIndexReplay).Should().BeTrue();
    }

    [Test]
    public async Task NoIndexOnTheServer_LeavesEverythingAlone()
    {
        var b = Publish();
        b.Server.Files.Remove(IndexUrl);
        b.Server.Files.Remove(IndexUrl + UpdateSignature.Extension);
        await RunCheckUpdate(b);
        await b.Ready.Should().BeNull();
        await AllMessages(b).Contains(ResUI.MsgCoreIndexUnavailable).Should().BeTrue();
    }

    [Test]
    public async Task AnEngineZipThatWasNeverVerified_CannotBeInstalled()
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"glide-nv-{Guid.NewGuid():N}")).FullName;
        var rogue = Path.Combine(dir, "rogue.zip");
        File.WriteAllBytes(rogue, BuildZip("evil", "evil"));
        var bin = Path.Combine(dir, "bin");
        Directory.CreateDirectory(bin);
        File.WriteAllText(Path.Combine(bin, "xray.exe"), "old xray");

        var r = VerifiedCoreInstall.Install(rogue, bin);

        await r.Error.Should().BeEqualTo(CoreInstallError.NotVerified);
        await File.ReadAllText(Path.Combine(bin, "xray.exe")).Should().BeEqualTo("old xray");
        await Directory.Exists(Path.Combine(bin, CoreInstaller.PreviousFolder)).Should().BeFalse();
    }
}
