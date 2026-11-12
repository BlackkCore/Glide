using System.Net.Sockets;

namespace ServiceLib.Tests.Services;

/// <summary>
/// A GitHub "No such host is known" seen while testing: the lookup of github.com failed in the computer's own
/// network (the update check goes straight out when no local proxy is running). That is not a Glide fault, so the
/// Updates screen now says so instead of showing the raw .NET text.
/// </summary>
public class DnsFailureTests
{
    private sealed class FailingHandler(Exception failure) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw failure;
    }

    private static Exception DnsFailure()
        => new HttpRequestException(HttpRequestError.NameResolutionError, "No such host is known. (github.com:443)",
            new SocketException((int)SocketError.HostNotFound));

    [Test]
    public async Task NameResolutionErrors_AreRecognised_ThroughInnerExceptions()
    {
        await NetworkFailure.IsNameResolution(DnsFailure()).Should().BeTrue();
        await NetworkFailure.IsNameResolution(new SocketException((int)SocketError.HostNotFound)).Should().BeTrue();
        await NetworkFailure.IsNameResolution(new SocketException((int)SocketError.TryAgain)).Should().BeTrue();
        await NetworkFailure.IsNameResolution(new Exception("outer", new SocketException((int)SocketError.HostNotFound))).Should().BeTrue();
    }

    [Test]
    public async Task OtherNetworkErrors_AreNotReportedAsDns()
    {
        await NetworkFailure.IsNameResolution(new SocketException((int)SocketError.ConnectionRefused)).Should().BeFalse();
        await NetworkFailure.IsNameResolution(new HttpRequestException(HttpRequestError.ConnectionError)).Should().BeFalse();
        await NetworkFailure.IsNameResolution(new TimeoutException()).Should().BeFalse();
        await NetworkFailure.IsNameResolution(null).Should().BeFalse();
        await NetworkFailure.Describe(new TimeoutException("timed out"), "github.com").Should().BeEqualTo("timed out");
    }

    [Test]
    public async Task Describe_NamesTheHost_AndSaysItIsNotGlide()
    {
        var text = NetworkFailure.Describe(DnsFailure(), "github.com");
        await text.Contains("github.com").Should().BeTrue();
        await text.Contains("DNS").Should().BeTrue();
        await text.Contains("did not block").Should().BeTrue();
        await text.Contains("No such host").Should().BeFalse();
    }

    [Test]
    public async Task ReleaseLookup_ReportsAFailedNameLookupPlainly()
    {
        await CertPemManager.Instance.Init(new Config { GuiItem = new GUIItem() });
        using var service = new DownloadService { Handler = new FailingHandler(DnsFailure()) };
        var errors = new List<string>();
        service.Error += (_, e) => errors.Add(e.GetException().Message);

        var url = await service.UrlRedirectAsync("https://github.com/XTLS/Xray-core/releases/latest").WaitAsync(TimeSpan.FromSeconds(20));

        await url.Should().BeNull();
        await errors.Count.Should().BeEqualTo(1);
        await errors[0].Contains("Could not look up github.com").Should().BeTrue();
    }
}
