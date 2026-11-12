using System.Collections.Concurrent;
using System.Net.Sockets;

namespace ServiceLib.Tests.Services;

/// <summary>
/// The Updates screen showed "StatusCode error: OK" for Xray, mihomo and sing-box: the lookup of the newest release
/// asked github.com/.../releases/latest to redirect to .../releases/tag/vX, but the shared HTTP handler follows
/// redirects, so the reply was the final page (200 OK) and was reported as an error. These tests use a local server
/// that behaves like GitHub.
/// </summary>
public class UpdateCheckTests
{
    private const string Tag = "v26.10.31";

    [Test]
    public async Task UrlRedirect_GitHubStyleRedirect_ReturnsTheTagAddress_AndNoError()
    {
        await CertPemManager.Instance.Init(new Config { GuiItem = new GUIItem() });
        await using var server = new RouteServer();
        using var service = new DownloadService();
        var errors = Capture(service);

        var url = await service.UrlRedirectAsync(server.Url("/org/repo/releases/latest")).WaitAsync(TimeSpan.FromSeconds(20));

        await url.Should().NotBeNull();
        await UpdateService.TagFromReleaseUrl(url).Should().BeEqualTo(Tag);
        await errors.Should().BeEmpty();
    }

    [Test]
    public async Task UrlRedirect_AddressThatDoesNotRedirect_IsReportedClearly_NotAsStatusCodeOK()
    {
        await CertPemManager.Instance.Init(new Config { GuiItem = new GUIItem() });
        await using var server = new RouteServer();
        using var service = new DownloadService();
        var errors = Capture(service);

        var url = await service.UrlRedirectAsync(server.Url("/no-redirect/releases/latest")).WaitAsync(TimeSpan.FromSeconds(20));

        await url.Should().BeNull();
        await errors.Count.Should().BeEqualTo(1);
        await errors[0].Should().Contain("No redirect from 127.0.0.1");
        await errors[0].Contains("StatusCode error: OK").Should().BeFalse();
    }

    [Test]
    [Arguments("/api-down/releases/latest", "StatusCode error: 503 ServiceUnavailable")]
    [Arguments("/not-there/releases/latest", "StatusCode error: 404 NotFound")]
    public async Task UrlRedirect_ErrorStatus_ReportsTheStatusCode(string path, string expected)
    {
        await CertPemManager.Instance.Init(new Config { GuiItem = new GUIItem() });
        await using var server = new RouteServer();
        using var service = new DownloadService();
        var errors = Capture(service);

        var url = await service.UrlRedirectAsync(server.Url(path)).WaitAsync(TimeSpan.FromSeconds(20));

        await url.Should().BeNull();
        await errors.Should().HaveSingleItem();
        await errors[0].Should().BeEqualTo(expected);
    }

    [Test]
    public async Task UrlRedirect_ServerNotReachable_ReturnsNullWithAMessage_InsteadOfThrowing()
    {
        await CertPemManager.Instance.Init(new Config { GuiItem = new GUIItem() });
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        using var service = new DownloadService();
        var errors = Capture(service);

        var url = await service.UrlRedirectAsync($"http://127.0.0.1:{port}/org/repo/releases/latest").WaitAsync(TimeSpan.FromSeconds(20));

        await url.Should().BeNull();
        await errors.Should().HaveSingleItem();
        await errors[0].Should().NotBeEmpty();
    }

    [Test]
    public async Task UrlRedirect_RedirectToAPageWithoutATag_GivesTheAddress_ButNoTag()
    {
        await CertPemManager.Instance.Init(new Config { GuiItem = new GUIItem() });
        await using var server = new RouteServer();
        using var service = new DownloadService();

        var url = await service.UrlRedirectAsync(server.Url("/elsewhere/releases/latest")).WaitAsync(TimeSpan.FromSeconds(20));

        await url.Should().EndWith("/elsewhere/releases");
        await UpdateService.TagFromReleaseUrl(url).Should().BeNull();
    }

    [Test]
    [Arguments("https://github.com/XTLS/Xray-core/releases/tag/v26.10.31", "v26.10.31")]
    [Arguments("https://github.com/SagerNet/sing-box/releases/tag/v1.14.3/", "v1.14.3")]
    [Arguments("https://github.com/MetaCubeX/mihomo/releases/tag/v1.19.29?expanded=true#x", "v1.19.29")]
    [Arguments("https://github.com/o/r/releases/tag/v1.15.0-beta.1", "v1.15.0-beta.1")]
    [Arguments("https://github.com/o/r/releases/tag/v1.15.0%2Bbuild5", "v1.15.0+build5")]
    [Arguments("https://github.com/o/r/releases/tag/2.0", "2.0")]
    public async Task TagFromReleaseUrl_ReadsTheTag(string url, string expected)
    {
        await UpdateService.TagFromReleaseUrl(url).Should().BeEqualTo(expected);
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("https://github.com/XTLS/Xray-core/releases/latest")]
    [Arguments("https://github.com/XTLS/Xray-core/releases/tag/")]
    [Arguments("https://github.com/XTLS/Xray-core/releases/tag/nightly")]
    [Arguments("<html>not an address</html>")]
    public async Task TagFromReleaseUrl_NoTagOrNotAVersion_IsNull(string? url)
    {
        await UpdateService.TagFromReleaseUrl(url).Should().BeNull();
    }

    private const string ReleasesJson = """
        [
          { "tag_name": "v1.16.0-beta.2", "prerelease": true,  "draft": false },
          { "tag_name": "v1.15.9",        "prerelease": false, "draft": true },
          { "tag_name": "v1.15.8",        "prerelease": false, "draft": false },
          { "tag_name": "v1.14.3",        "prerelease": false, "draft": false }
        ]
        """;

    [Test]
    public async Task PickReleaseTag_StableSkipsPrereleasesAndDrafts()
    {
        await UpdateService.PickReleaseTag(ReleasesJson, false).Should().BeEqualTo("v1.15.8");
    }

    [Test]
    public async Task PickReleaseTag_PreReleaseWantedTakesTheNewest()
    {
        await UpdateService.PickReleaseTag(ReleasesJson, true).Should().BeEqualTo("v1.16.0-beta.2");
    }

    [Test]
    public async Task PickReleaseTag_NeverOffersAVersionAboveTheLockedMaximum()
    {
        await UpdateService.PickReleaseTag(ReleasesJson, false, new SemanticVersion(1, 14, int.MaxValue)).Should().BeEqualTo("v1.14.3");
        await UpdateService.PickReleaseTag(ReleasesJson, true, new SemanticVersion(1, 15, 8)).Should().BeEqualTo("v1.15.8");
        await UpdateService.PickReleaseTag(ReleasesJson, false, new SemanticVersion(1, 0, 0)).Should().BeNull();
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("[]")]
    [Arguments("{}")]
    [Arguments("not json at all")]
    [Arguments("[{\"message\":\"API rate limit exceeded\"}]")]
    [Arguments("{\"message\":\"API rate limit exceeded\",\"documentation_url\":\"https://docs.github.com\"}")]
    [Arguments("[{\"tag_name\":\"nightly\"}]")]
    [Arguments("[null]")]
    public async Task PickReleaseTag_EmptyMalformedOrErrorReplies_GiveNoTag_AndDoNotThrow(string? json)
    {
        await UpdateService.PickReleaseTag(json, false).Should().BeNull();
        await UpdateService.PickReleaseTag(json, true).Should().BeNull();
    }

    [Test]
    [Arguments("1.9.0", "1.10.0", -1)]
    [Arguments("v26.10.31", "26.10.31", 0)]
    [Arguments("v26.10.31", "v26.11.9", -1)]
    [Arguments("1.19.34", "1.19.29", 1)]
    [Arguments("1.15.0-beta.1", "1.15.0", -1)]
    [Arguments("1.15.0-beta.2", "1.15.0-beta.10", -1)]
    [Arguments("1.14.3", "1.14.3+build7", 0)]
    public async Task SemanticVersion_ComparesLikeAReleaseList(string left, string right, int expected)
    {
        var l = new SemanticVersion(left);
        var r = new SemanticVersion(right);
        await Math.Sign(l.CompareTo(r)).Should().BeEqualTo(expected);
        await (l >= r).Should().BeEqualTo(expected >= 0);
    }

    [Test]
    public async Task SemanticVersion_GarbageIsZero_SoItNeverLooksNewerThanAnInstalledCore()
    {
        var garbage = new SemanticVersion("<html>");
        await garbage.Equals(new SemanticVersion(0, 0, 0)).Should().BeTrue();
        await (new SemanticVersion("v1.14.3") >= garbage).Should().BeTrue();
    }

    /// <summary>A local server with a few fixed routes; every reply closes the connection.</summary>
    private sealed class RouteServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _cancellation = new();
        private readonly Task _serverTask;

        public ConcurrentQueue<string> Requests { get; } = new();

        public RouteServer()
        {
            _listener.Start();
            _serverTask = ServeAsync();
        }

        public string Url(string path) => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}{path}";

        private static string Reply(string path) => path switch
        {
            "/org/repo/releases/latest" => $"302 Found\r\nLocation: /org/repo/releases/tag/{Tag}\r\n",
            "/org/repo/releases/tag/" + Tag => "200 OK\r\nContent-Type: text/html\r\n",
            "/no-redirect/releases/latest" => "200 OK\r\nContent-Type: text/html\r\n",
            "/elsewhere/releases/latest" => "302 Found\r\nLocation: /elsewhere/releases\r\n",
            "/elsewhere/releases" => "200 OK\r\nContent-Type: text/html\r\n",
            "/api-down/releases/latest" => "503 Service Unavailable\r\n",
            _ => "404 Not Found\r\n",
        };

        private async Task ServeAsync()
        {
            var token = _cancellation.Token;
            while (!token.IsCancellationRequested)
            {
                using var client = await _listener.AcceptTcpClientAsync(token);
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                var requestLine = await reader.ReadLineAsync(token);
                if (requestLine == null)
                {
                    continue;
                }
                while (await reader.ReadLineAsync(token) is { Length: > 0 })
                {
                }
                var parts = requestLine.Split(' ');
                Requests.Enqueue($"{parts[0]} {parts[1]}");
                var body = parts[0] == "HEAD" ? "" : "<html></html>";
                var response = $"HTTP/1.1 {Reply(parts[1])}Content-Length: {body.Length}\r\nConnection: close\r\n\r\n{body}";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(response), token);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _cancellation.CancelAsync();
            _listener.Stop();
            try
            {
                await _serverTask;
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (SocketException)
            {
            }
        }
    }

    private static List<string> Capture(DownloadService service)
    {
        var errors = new List<string>();
        service.Error += (_, e) => errors.Add(e.GetException().Message);
        return errors;
    }
}
