namespace ServiceLib.Tests.Handler;

public class OriginLocationServiceTests
{
    [Test]
    public async Task TryParseGeoResponse_ParsesIpSbAndIpWhoIs()
    {
        var ipSb = """{"ip":"203.0.113.10","country_code":"DE","latitude":50.1109,"longitude":8.6821}""";
        await OriginLocationService.TryParseGeoResponse(ipSb, out var r1, "api.ip.sb").Should().BeTrue();
        await r1.Code.Should().BeEqualTo("DE");
        await r1.Ip.Should().BeEqualTo("203.0.113.10");
        await r1.Lat.Should().BeEqualTo(50.1109);
        await r1.Lon.Should().BeEqualTo(8.6821);
        await r1.Source.Should().BeEqualTo("api.ip.sb");

        var ipWhoIs = """{"ip":"198.51.100.7","success":true,"country_code":"GB","latitude":51.5074,"longitude":-0.1278}""";
        await OriginLocationService.TryParseGeoResponse(ipWhoIs, out var r2, "ipwho.is").Should().BeTrue();
        await r2.Code.Should().BeEqualTo("GB");
        await r2.Lat.Should().BeEqualTo(51.5074);
        await r2.Lon.Should().BeEqualTo(-0.1278);
    }

    [Test]
    public async Task TryParseGeoResponse_ParsesIpapiIsAndIpInfoAndTrace()
    {
        // Nested location object from api.ipapi.is
        var ipapiNested = """{"ip":"192.0.2.5","location":{"country_code":"fr","latitude":48.8566,"longitude":2.3522}}""";
        await OriginLocationService.TryParseGeoResponse(ipapiNested, out var r1).Should().BeTrue();
        await r1.Code.Should().BeEqualTo("FR");
        await r1.Lat.Should().BeEqualTo(48.8566);
        await r1.Lon.Should().BeEqualTo(2.3522);

        // Free-tier full country name + lat/lon from api.ipapi.is
        var ipapiFree = """{"ip":"203.0.113.77","is_bogon":false,"country":"Iran","lat":35.6892,"lon":51.389}""";
        await OriginLocationService.TryParseGeoResponse(ipapiFree, out var r2).Should().BeTrue();
        await r2.Code.Should().BeEqualTo("IR");
        await r2.Lat.Should().BeEqualTo(35.6892);
        await r2.Lon.Should().BeEqualTo(51.389);

        // ipinfo.io "loc": "lat,lon" and "UK" -> "GB" normalization
        var ipInfo = """{"ip":"198.51.100.9","country":"UK","loc":"51.5074,-0.1278"}""";
        await OriginLocationService.TryParseGeoResponse(ipInfo, out var r3).Should().BeTrue();
        await r3.Code.Should().BeEqualTo("GB");
        await r3.Lat.Should().BeEqualTo(51.5074);
        await r3.Lon.Should().BeEqualTo(-0.1278);

        // Cloudflare trace key=value format
        var trace = "fl=489f27\nh=www.cloudflare.com\nip=203.0.113.42\nloc=DE\ntls=TLSv1.3\n";
        await OriginLocationService.TryParseGeoResponse(trace, out var r4, "www.cloudflare.com").Should().BeTrue();
        await r4.Code.Should().BeEqualTo("DE");
        await r4.Ip.Should().BeEqualTo("203.0.113.42");
        await r4.Lat.Should().BeNull();
        await r4.Lon.Should().BeNull();
    }

    [Test]
    public async Task TryParseGeoResponse_RejectsErrorsRateLimitsAndInvalidCodes()
    {
        await OriginLocationService.TryParseGeoResponse("""{"success":false,"message":"invalid"}""", out _).Should().BeFalse();
        await OriginLocationService.TryParseGeoResponse("""{"status":429,"error":{"title":"Rate limit hit"}}""", out _).Should().BeFalse();
        await OriginLocationService.TryParseGeoResponse("""{"ip":"127.0.0.1","is_bogon":true,"country_code":"US"}""", out _).Should().BeFalse();
        await OriginLocationService.TryParseGeoResponse("ip=1.2.3.4\nloc=XX\n", out _).Should().BeFalse();
        await OriginLocationService.TryParseGeoResponse("ip=1.2.3.4\nloc=T1\n", out _).Should().BeFalse();
        await OriginLocationService.TryParseGeoResponse("<html>403 Forbidden</html>", out _).Should().BeFalse();
    }

    [Test]
    public async Task Cache_RejectsLegacyV1AndExpiredEntries_AcceptsFreshV2()
    {
        var now = 1_794_305_000L;

        // Legacy v1 cache (stuck on NL, no Version field) must be rejected.
        var legacyV1 = """{"Lat":52.3716,"Lon":4.8883,"Code":"NL","Stamp":1794304900}""";
        await OriginLocationService.TryParseCacheJson(legacyV1, now, OriginLocationService.MaxDiskCacheAgeSeconds, out _)
            .Should().BeFalse();

        // Expired v2 cache (older than MaxDiskCacheAgeSeconds) must be rejected.
        var expiredV2 = $$"""{"Version":2,"Code":"NL","Lat":52.37,"Lon":4.89,"Stamp":{{now - OriginLocationService.MaxDiskCacheAgeSeconds - 10}}}""";
        await OriginLocationService.TryParseCacheJson(expiredV2, now, OriginLocationService.MaxDiskCacheAgeSeconds, out _)
            .Should().BeFalse();

        // Fresh v2 cache within MaxDiskCacheAgeSeconds is accepted.
        var freshV2 = $$"""{"Version":2,"Code":"DE","Lat":50.11,"Lon":8.68,"Ip":"203.0.113.1","Source":"api.ip.sb","Stamp":{{now - 120}}}""";
        await OriginLocationService.TryParseCacheJson(freshV2, now, OriginLocationService.MaxDiskCacheAgeSeconds, out var parsed)
            .Should().BeTrue();
        await parsed.Code.Should().BeEqualTo("DE");
        await parsed.Ip.Should().BeEqualTo("203.0.113.1");
    }

    [Test]
    public async Task RefreshAsync_RedetectsOnStartupAndAfterTtlExpiry()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "glide-origin-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var cacheFile = Path.Combine(tempDir, "glide-origin.json");
            var optOutFile = Path.Combine(tempDir, "glide-no-locate");
            var clock = DateTimeOffset.FromUnixTimeSeconds(1_794_305_000L);
            var currentCountry = "NL";
            var calls = 0;

            OriginLocationService CreateService() => new(
                cacheFilePath: cacheFile,
                optOutFilePath: optOutFile,
                utcNow: () => clock,
                isProxyActive: () => false,
                getExitInfo: () => (null, null),
                fetchUrlAsync: (_, _) =>
                {
                    calls++;
                    return Task.FromResult<string?>($"{{\"ip\":\"203.0.113.{calls}\",\"country_code\":\"{currentCountry}\",\"latitude\":51.5,\"longitude\":-0.1}}");
                },
                serviceUrls: ["https://api.ip.sb/geoip"]);

            var svc1 = CreateService();
            await svc1.RefreshAsync();
            await svc1.Code.Should().BeEqualTo("NL");
            await calls.Should().BeEqualTo(1);

            // Second call within 5-minute TTL on the same instance uses memory cache.
            clock = clock.AddSeconds(60);
            currentCountry = "DE";
            await svc1.RefreshAsync();
            await svc1.Code.Should().BeEqualTo("NL");
            await calls.Should().BeEqualTo(1);

            // New app start (new service instance) MUST re-detect even though disk cache is only 60s old.
            var svc2 = CreateService();
            await svc2.RefreshAsync();
            await svc2.Code.Should().BeEqualTo("DE");
            await calls.Should().BeEqualTo(2);

            // After CacheTtlSeconds (5 minutes), non-forced RefreshAsync re-detects again.
            clock = clock.AddSeconds(OriginLocationService.CacheTtlSeconds + 5);
            currentCountry = "GB";
            await svc2.RefreshAsync();
            await svc2.Code.Should().BeEqualTo("GB");
            await calls.Should().BeEqualTo(3);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Test]
    public async Task RefreshAsync_DoesNotOverwriteWithProxyExitAndKeepsLastGoodLocation()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "glide-origin-proxy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var cacheFile = Path.Combine(tempDir, "glide-origin.json");
            var optOutFile = Path.Combine(tempDir, "glide-no-locate");
            var clock = DateTimeOffset.FromUnixTimeSeconds(1_794_305_000L);
            var proxyActive = false;
            (string? Country, string? Ip) exit = (null, null);
            var networkAnswer = """{"ip":"203.0.113.10","country_code":"DE","latitude":50.11,"longitude":8.68}""";

            var svc = new OriginLocationService(
                cacheFilePath: cacheFile,
                optOutFilePath: optOutFile,
                utcNow: () => clock,
                isProxyActive: () => proxyActive,
                getExitInfo: () => exit,
                fetchUrlAsync: (_, _) => Task.FromResult<string?>(networkAnswer),
                serviceUrls: ["https://api.ip.sb/geoip"],
                canBypassTunnel: () => true);

            // 1. Disconnected in Germany -> stores DE.
            await svc.RefreshAsync(force: true);
            await svc.Code.Should().BeEqualTo("DE");

            // 2. Connect to a Netherlands proxy -> lookup while proxy is active is skipped.
            proxyActive = true;
            exit = ("NL", "203.0.113.50");
            networkAnswer = """{"ip":"203.0.113.50","country_code":"NL","latitude":52.37,"longitude":4.89}""";
            await svc.RefreshAsync(force: true);
            await svc.Code.Should().BeEqualTo("DE");

            // 3. Even if proxyActive appears false during a race, an answer matching the exit IP/country is rejected.
            proxyActive = false;
            await svc.RefreshAsync(force: true);
            await svc.Code.Should().BeEqualTo("DE");

            // 4. If a lookup finished just before exit info arrived, NotifyExitInfo reverts to the previous good location.
            exit = (null, null);
            clock = clock.AddSeconds(10);
            await svc.RefreshAsync(force: true);
            await svc.Code.Should().BeEqualTo("NL");
            svc.NotifyExitInfo("NL", "203.0.113.50");
            await svc.Code.Should().BeEqualTo("DE");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Test]
    public async Task RefreshAsync_WithTunOn_LooksUpAroundTheTunnel_OnlyWhenAPhysicalAdapterExists()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "glide-origin-tun-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var calls = 0;
            var hasAdapter = false;
            var svc = new OriginLocationService(
                cacheFilePath: Path.Combine(tempDir, "glide-origin.json"),
                optOutFilePath: Path.Combine(tempDir, "glide-no-locate"),
                utcNow: () => DateTimeOffset.FromUnixTimeSeconds(1_794_305_000L),
                isProxyActive: () => true, // Glide's TUN is on
                getExitInfo: () => ("NL", "203.0.113.50"),
                fetchUrlAsync: (_, _) =>
                {
                    calls++;
                    return Task.FromResult<string?>("""{"ip":"81.2.69.160","country_code":"GB","latitude":51.5,"longitude":-0.12}""");
                },
                serviceUrls: ["https://api.ip.sb/geoip"],
                canBypassTunnel: () => hasAdapter);

            // No physical adapter to go around the tunnel: nothing is asked and nothing is guessed.
            await svc.RefreshAsync(force: true);
            await calls.Should().BeEqualTo(0);
            await svc.Code.Should().BeNull();

            // With an adapter, the lookup goes around the tunnel and finds the real place (London), not the exit (NL).
            hasAdapter = true;
            await svc.RefreshAsync(force: true);
            await calls.Should().BeEqualTo(1);
            await svc.Code.Should().BeEqualTo("GB");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Test]
    public async Task DefaultServices_AreOnlyTheAppsOwnExitCheckServices_OverIpv4()
    {
        await OriginLocationService.DefaultServiceUrls.Count.Should().BeGreaterThan(0);
        foreach (var url in OriginLocationService.DefaultServiceUrls)
        {
            await Global.IPAPIUrls.Contains(url).Should().BeTrue();
            await url.Contains("ipv6").Should().BeFalse();
        }
    }

    [Test]
    public async Task ToPersianDigits_ConvertsOnlyDigits()
    {
        await OriginLocationService.ToPersianDigits("10:41").Should().BeEqualTo("۱۰:۴۱");
        await OriginLocationService.ToPersianDigits("abc").Should().BeEqualTo("abc");
    }

    [Test]
    public async Task OptOut_IsCheckedOnRefresh_NotOnEveryRead()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "glide-origin-optout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var optOutFile = Path.Combine(tempDir, "glide-no-locate");
            var svc = new OriginLocationService(
                cacheFilePath: Path.Combine(tempDir, "glide-origin.json"),
                optOutFilePath: optOutFile,
                utcNow: () => DateTimeOffset.FromUnixTimeSeconds(1_794_305_000L),
                isProxyActive: () => false,
                getExitInfo: () => (null, null),
                fetchUrlAsync: (_, _) => Task.FromResult<string?>("""{"ip":"203.0.113.10","country_code":"DE"}"""),
                serviceUrls: ["https://api.ip.sb/geoip"]);

            await svc.RefreshAsync(force: true);
            await svc.Code.Should().BeEqualTo("DE");

            // Reads don't touch the disk: the new opt-out file is seen at the next refresh, which then clears everything.
            File.WriteAllText(optOutFile, "");
            await svc.Code.Should().BeEqualTo("DE");
            await svc.RefreshAsync();
            await svc.Code.Should().BeNull();
            await svc.IsOptedOut.Should().BeTrue();
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Test]
    public async Task RefreshAsync_FallsBackAcrossServices_AndReturnsNullWhenAllFail_AndRespectsOptOut()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "glide-origin-fail-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var cacheFile = Path.Combine(tempDir, "glide-origin.json");
            var optOutFile = Path.Combine(tempDir, "glide-no-locate");
            var clock = DateTimeOffset.FromUnixTimeSeconds(1_794_305_000L);
            var allFail = true;

            var svc = new OriginLocationService(
                cacheFilePath: cacheFile,
                optOutFilePath: optOutFile,
                utcNow: () => clock,
                isProxyActive: () => false,
                getExitInfo: () => (null, null),
                fetchUrlAsync: (url, _) =>
                {
                    if (allFail || url.Contains("ip.sb"))
                    {
                        throw new HttpRequestException("blocked");
                    }
                    return Task.FromResult<string?>("ip=198.51.100.1\nloc=GB\n");
                },
                serviceUrls: ["https://api.ip.sb/geoip", "https://www.cloudflare.com/cdn-cgi/trace"]);

            // When all services fail, NO country is guessed (Code and Position are null).
            await svc.RefreshAsync(force: true);
            await svc.Code.Should().BeNull();
            await svc.Position.Should().BeNull();

            // When second service succeeds, fallback works and tooltip shows source + time.
            allFail = false;
            await svc.RefreshAsync(force: true);
            await svc.Code.Should().BeEqualTo("GB");
            var enTip = svc.FormatSourceTooltip(persianUi: false);
            await enTip.Should().NotBeNull();
            await enTip!.Contains("from your network,").Should().BeTrue();

            // Creating glide-no-locate clears cached location and disables lookups.
            File.WriteAllText(optOutFile, "");
            await svc.RefreshAsync(force: true);
            await svc.Code.Should().BeNull();
            await File.Exists(cacheFile).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
