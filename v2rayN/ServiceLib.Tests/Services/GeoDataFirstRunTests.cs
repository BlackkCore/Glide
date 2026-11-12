using System.Net;
using ServiceLib.Tests.Common;
using System.Net.Sockets;

namespace ServiceLib.Tests.Services;

/// <summary>
/// Glide's packages do not contain the GeoLite-derived data files; the app
/// notices the missing lists and downloads them. These tests walk that path with a local server in place of GitHub:
/// nothing there, a successful download, and no connection with a user's own copy already on disk.
/// </summary>
public class GeoDataFirstRunTests
{
    // A geosite.dat / geoip.dat is a list of entries (field 1), each starting with its code (field 1).
    private static byte[] Dat(params string[] codes)
    {
        List<byte> data = [];
        foreach (var code in codes)
        {
            var codeBytes = Encoding.ASCII.GetBytes(code);
            List<byte> entry = [0x0A, (byte)codeBytes.Length, .. codeBytes, 0x12, 0x03, 0x01, 0x02, 0x03];
            data.Add(0x0A);
            data.Add((byte)entry.Count);
            data.AddRange(entry);
        }
        return [.. data];
    }

    // The references Glide's own default routing uses (custom_routing_global names geosite:private and geoip:private).
    private static readonly string[] Refs = ["geosite:private", "geoip:private"];

    private sealed class FileServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _cancel = new();
        private readonly Task _loop;
        private readonly Dictionary<string, byte[]> _files;

        public FileServer(Dictionary<string, byte[]> files)
        {
            _files = files;
            _listener.Start();
            _loop = Task.Run(AcceptLoop);
        }

        public string Url(string path) => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}{path}";

        private async Task AcceptLoop()
        {
            try
            {
                while (!_cancel.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync(_cancel.Token);
                    _ = Task.Run(() => Serve(client));
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private async Task Serve(TcpClient client)
        {
            using var _ = client;
            try
            {
                var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
                var requestLine = await reader.ReadLineAsync();
                string? line;
                while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync()))
                {
                }
                var parts = (requestLine ?? string.Empty).Split(' ');
                if (parts.Length < 2)
                {
                    return;
                }
                var found = _files.TryGetValue(parts[1], out var body);
                body ??= [];
                var head = $"HTTP/1.1 {(found ? "200 OK" : "404 Not Found")}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
                if (parts[0] != "HEAD")
                {
                    await stream.WriteAsync(body);
                }
            }
            catch (Exception)
            {
                // a client that went away
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _cancel.CancelAsync();
            _listener.Stop();
            try
            {
                await _loop;
            }
            catch (Exception)
            {
            }
        }
    }

    private static string NewDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "glide-geo-firstrun-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    [Test]
    public async Task NothingDownloadedYet_EveryReferencedListIsReportedMissing()
    {
        var dir = NewDir();
        try
        {
            var site = GeoDataCheck.CodesIn(Path.Combine(dir, "geosite.dat"));
            var ip = GeoDataCheck.CodesIn(Path.Combine(dir, "geoip.dat"));
            await site.Should().BeNull();
            await ip.Should().BeNull();
            var missing = GeoDataCheck.FindMissing(Refs, site, ip);
            await missing.Count.Should().BeEqualTo(2);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Test]
    public async Task AfterASuccessfulDownload_NothingIsMissing_AndTheFilesAreInstalled()
    {
        await CertPemManager.Instance.Init(new Config { GuiItem = new GUIItem() });
        var dir = NewDir();
        await using var server = new FileServer(new()
        {
            ["/geosite.dat"] = Dat("CN", "PRIVATE"),
            ["/geoip.dat"] = Dat("CN", "PRIVATE"),
        });
        try
        {
            var requests = new[] { "geosite", "geoip" }.Select(n => new FileDownloadRequest
            {
                FileUrl = server.Url($"/{n}.dat"),
                FilePath = Path.Combine(dir, $"{n}.tmp"),
                DisplayFileName = $"{n}.dat",
            }).ToList();
            using var service = new DownloadService();
            var errors = new List<string>();
            service.Error += (_, e) => errors.Add(e.GetException().Message);

            await service.DownloadSmallFilesAsync(requests).WaitAsync(TimeSpan.FromSeconds(60));

            var failed = UpdateService.InstallDownloadedFiles(
                requests.Select(r => (r.FilePath, Path.Combine(dir, r.DisplayFileName!))).ToList());

            await errors.Count.Should().BeEqualTo(0);
            await failed.Count.Should().BeEqualTo(0);
            var site = GeoDataCheck.CodesIn(Path.Combine(dir, "geosite.dat"));
            var ip = GeoDataCheck.CodesIn(Path.Combine(dir, "geoip.dat"));
            await GeoDataCheck.FindMissing(Refs, site, ip).Count.Should().BeEqualTo(0);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Test]
    public async Task OfflineDownload_FailsWithoutCrashing_AndKeepsTheUsersOwnCopy()
    {
        await CertPemManager.Instance.Init(new Config { GuiItem = new GUIItem() });
        var dir = NewDir();
        try
        {
            var target = Path.Combine(dir, "geoip.dat");
            File.WriteAllBytes(target, Dat("CN", "PRIVATE", "MY-OWN-LIST"));
            var temp = Path.Combine(dir, "geoip.tmp");
            var requests = new List<FileDownloadRequest>
            {
                // nothing listens here: the same as being offline
                new() { FileUrl = $"http://127.0.0.1:{FreePort()}/geoip.dat", FilePath = temp, DisplayFileName = "geoip.dat" },
            };
            using var service = new DownloadService();
            var errors = new List<string>();
            service.Error += (_, e) => errors.Add(e.GetException().Message);

            await service.DownloadSmallFilesAsync(requests).WaitAsync(TimeSpan.FromSeconds(60));
            var failed = UpdateService.InstallDownloadedFiles([(temp, target)]);

            await errors.Count.Should().BeGreaterThan(0);
            await failed.Count.Should().BeEqualTo(1);
            await GeoDataCheck.CodesIn(target)!.Contains("my-own-list").Should().BeTrue();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Test]
    public async Task ADownloadedFile_IsNotReplacedBySomethingShippedLater()
    {
        // An installed Glide keeps downloaded data in its data folder and only seeds files that are missing there.
        var program = NewDir();
        var data = NewDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(program, "bin"));
            Directory.CreateDirectory(Path.Combine(data, "bin"));
            File.WriteAllText(Path.Combine(program, "bin", "geosite.dat"), "shipped");
            File.WriteAllText(Path.Combine(data, "bin", "geosite.dat"), "downloaded by the user");

            InstallMigration.Run(program, data, Path.Combine(program, "no-old-install"));

            await File.ReadAllText(Path.Combine(data, "bin", "geosite.dat")).Should().BeEqualTo("downloaded by the user");
        }
        finally
        {
            Directory.Delete(program, true);
            Directory.Delete(data, true);
        }
    }

    [Test]
    public async Task TheStatusBar_OffersTheDownloadBeforeTheFirstConnection()
    {
        // Tripwire (the screen has no unit-test host): idle with the lists missing shows the first-run message and the button.
        var src = File.ReadAllText(Path.Combine(TestRepo.Root(), "v2rayN", "v2rayN.Desktop", "Views", "StatusBarView.axaml.cs"));
        await src.Contains("ConnState.NotConnected when geoMissing => ResUI.TipFirstRunDataNeeded").Should().BeTrue();
        await src.Contains("ConnState.EngineStopped or ConnState.NotConnected when geoMissing => ResUI.TbDownloadSiteLists").Should().BeTrue();
        await src.Contains("case ConnState.EngineStopped or ConnState.NotConnected when _missingGeo.Count > 0:").Should().BeTrue();
    }
}
