using System.Globalization;

namespace ServiceLib.Handler;

/// <summary>
/// Parsed geo-IP response for the user's own (non-proxy) network origin.
/// </summary>
public readonly record struct OriginGeoResult(
    string Code,
    double? Lat = null,
    double? Lon = null,
    string? Ip = null,
    string? Source = null,
    long Stamp = 0);

/// <summary>
/// Detects and caches the user's real (non-proxy) country and coordinates for the start of the globe route.
/// Re-detects on every app start and on network changes, connected or not. The lookup never uses the system
/// proxy; with Glide's TUN on it goes around the tunnel through the physical Wi-Fi/Ethernet adapter (and is
/// skipped when there is none). Answers matching the proxy exit are rejected, and nothing is guessed when
/// detection fails (no fixed country, time zone or Windows region).
/// Only the geo-IP services the app already uses for the exit check are contacted. Names are resolved with the
/// system's DNS, or, when TUN hands back fake addresses, with the DNS servers set on the physical adapter.
/// Create an empty file named "glide-no-locate" in the config folder to opt out completely.
/// </summary>
public sealed class OriginLocationService
{
    public const int CurrentCacheVersion = 2;

    /// <summary>How long an in-memory lookup stays fresh before a non-forced refresh re-queries (5 minutes).</summary>
    public const long CacheTtlSeconds = 300;

    /// <summary>Maximum age of a disk cache entry loaded on startup (15 minutes); older entries are discarded.</summary>
    public const long MaxDiskCacheAgeSeconds = 900;

    /// <summary>
    /// Geo-IP endpoints tried in order (direct, no proxy): the same services as the app's exit-IP check,
    /// minus the IPv6-only one (the lookup connects over IPv4).
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultServiceUrls = Global.IPAPIUrls
        .Where(u => !string.IsNullOrWhiteSpace(u) && !u.Contains("ipv6", StringComparison.OrdinalIgnoreCase))
        .ToList();

    private static readonly Dictionary<string, string> CountryNameToIso = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Iran"] = "IR", ["Islamic Republic of Iran"] = "IR",
        ["Germany"] = "DE", ["Deutschland"] = "DE",
        ["United Kingdom"] = "GB", ["Great Britain"] = "GB", ["England"] = "GB",
        ["Netherlands"] = "NL", ["The Netherlands"] = "NL", ["Holland"] = "NL",
        ["United States"] = "US", ["United States of America"] = "US", ["USA"] = "US",
        ["Canada"] = "CA", ["France"] = "FR", ["Switzerland"] = "CH", ["Austria"] = "AT",
        ["Belgium"] = "BE", ["Luxembourg"] = "LU", ["Italy"] = "IT", ["Spain"] = "ES",
        ["Portugal"] = "PT", ["Sweden"] = "SE", ["Norway"] = "NO", ["Finland"] = "FI",
        ["Denmark"] = "DK", ["Iceland"] = "IS", ["Ireland"] = "IE", ["Poland"] = "PL",
        ["Czechia"] = "CZ", ["Czech Republic"] = "CZ", ["Slovakia"] = "SK", ["Hungary"] = "HU",
        ["Romania"] = "RO", ["Bulgaria"] = "BG", ["Greece"] = "GR", ["Serbia"] = "RS",
        ["Croatia"] = "HR", ["Slovenia"] = "SI", ["Ukraine"] = "UA", ["Moldova"] = "MD",
        ["Lithuania"] = "LT", ["Latvia"] = "LV", ["Estonia"] = "EE", ["Russia"] = "RU",
        ["Russian Federation"] = "RU", ["Belarus"] = "BY", ["Turkey"] = "TR", ["Türkiye"] = "TR",
        ["Cyprus"] = "CY", ["Israel"] = "IL", ["United Arab Emirates"] = "AE", ["UAE"] = "AE",
        ["Saudi Arabia"] = "SA", ["Qatar"] = "QA", ["Iraq"] = "IQ", ["Egypt"] = "EG",
        ["South Africa"] = "ZA", ["Nigeria"] = "NG", ["Kenya"] = "KE", ["Morocco"] = "MA",
        ["India"] = "IN", ["Pakistan"] = "PK", ["Bangladesh"] = "BD", ["Kazakhstan"] = "KZ",
        ["China"] = "CN", ["Hong Kong"] = "HK", ["Taiwan"] = "TW", ["Japan"] = "JP",
        ["South Korea"] = "KR", ["Republic of Korea"] = "KR", ["Korea"] = "KR",
        ["Singapore"] = "SG", ["Malaysia"] = "MY", ["Thailand"] = "TH", ["Vietnam"] = "VN",
        ["Indonesia"] = "ID", ["Philippines"] = "PH", ["Australia"] = "AU", ["New Zealand"] = "NZ",
        ["Armenia"] = "AM", ["Georgia"] = "GE", ["Azerbaijan"] = "AZ",
        ["Brazil"] = "BR", ["Argentina"] = "AR", ["Chile"] = "CL", ["Colombia"] = "CO",
        ["Peru"] = "PE", ["Mexico"] = "MX",
    };

    private readonly object _gate = new();
    private readonly string _cacheFilePath;
    private readonly string _optOutFilePath;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Func<bool> _isProxyActive;
    private readonly Func<(string? ExitCountry, string? ExitIp)> _getExitInfo;
    private readonly Func<string, CancellationToken, Task<string?>> _fetchUrlAsync;
    private readonly IReadOnlyList<string> _serviceUrls;

    private OriginGeoResult? _located;
    private OriginGeoResult? _previousLocated;
    private bool _loaded;
    private bool _startupRefreshed;
    private bool _refreshing;
    private bool _pendingForceRefresh;
    private string? _lastOutboundLocalIp;

    public event Action? Changed;

    /// <param name="isProxyActive">True while Glide's TUN is on, so a lookup must go around it.</param>
    /// <param name="canBypassTunnel">True when a physical adapter exists to send the lookup around the tunnel.</param>
    public OriginLocationService(
        string? cacheFilePath = null,
        string? optOutFilePath = null,
        Func<DateTimeOffset>? utcNow = null,
        Func<bool>? isProxyActive = null,
        Func<(string? ExitCountry, string? ExitIp)>? getExitInfo = null,
        Func<string, CancellationToken, Task<string?>>? fetchUrlAsync = null,
        IReadOnlyList<string>? serviceUrls = null,
        Func<bool>? canBypassTunnel = null)
    {
        _cacheFilePath = cacheFilePath ?? Utils.GetConfigPath("glide-origin.json");
        _optOutFilePath = optOutFilePath ?? Utils.GetConfigPath("glide-no-locate");
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _isProxyActive = isProxyActive ?? DefaultIsGlideProxyActive;
        _canBypassTunnel = canBypassTunnel ?? (() => FindPhysicalInternetInterface() != null);
        _getExitInfo = getExitInfo ?? (() => (null, null));
        _fetchUrlAsync = fetchUrlAsync ?? ((url, ct) => FetchDirectAsync(url, _isProxyActive(), ct));
        _serviceUrls = serviceUrls ?? DefaultServiceUrls;
    }

    private readonly Func<bool> _canBypassTunnel;

    // The opt-out file is checked when loading and at each refresh, never on a read: the globe reads
    // Code/Position several times per frame on the UI thread.
    private volatile bool _optedOut;

    /// <summary>True when the user has created the "glide-no-locate" opt-out file in the config folder (as of the last check).</summary>
    public bool IsOptedOut => _optedOut;

    private void RecheckOptOut()
    {
        try
        {
            _optedOut = !string.IsNullOrEmpty(_optOutFilePath) && File.Exists(_optOutFilePath);
        }
        catch
        {
            _optedOut = false;
        }
    }

    /// <summary>True while a network lookup is currently in flight.</summary>
    public bool IsLocating
    {
        get
        {
            lock (_gate)
            {
                return _refreshing;
            }
        }
    }

    /// <summary>Current confirmed non-proxy origin result, or null when unknown.</summary>
    public OriginGeoResult? Current
    {
        get
        {
            EnsureLoaded();
            lock (_gate)
            {
                if (IsOptedOut)
                {
                    return null;
                }
                return _located;
            }
        }
    }

    /// <summary>2-letter ISO country code of the user's real origin, or null when unknown.</summary>
    public string? Code => Current?.Code;

    /// <summary>Coordinates of the user's real origin when known.</summary>
    public (double Lat, double Lon)? Position
        => Current is { Lat: { } la, Lon: { } lo } ? (la, lo) : null;

    /// <summary>
    /// Called when the proxy availability check reports the exit IP and country.
    /// If the stored origin was updated right around connecting and matches the proxy exit, reverts to the previous non-proxy location.
    /// </summary>
    public void NotifyExitInfo(string? exitCountry, string? exitIp)
    {
        var changed = false;
        lock (_gate)
        {
            if (_located is not { } current)
            {
                return;
            }
            var normExitCountry = NormalizeCountryCode(exitCountry);
            var ipMatches = !string.IsNullOrWhiteSpace(exitIp)
                && !string.IsNullOrWhiteSpace(current.Ip)
                && string.Equals(current.Ip.Trim(), exitIp.Trim(), StringComparison.OrdinalIgnoreCase);

            var ageSeconds = _utcNow().ToUnixTimeSeconds() - current.Stamp;
            var recentCountryMatches = normExitCountry != null
                && string.Equals(current.Code, normExitCountry, StringComparison.OrdinalIgnoreCase)
                && ageSeconds <= 20
                && _previousLocated is { } prev
                && !string.Equals(prev.Code, normExitCountry, StringComparison.OrdinalIgnoreCase);

            if (ipMatches || recentCountryMatches)
            {
                _located = _previousLocated;
                if (_located is { } restored)
                {
                    SaveCacheLocked(restored);
                }
                else
                {
                    DeleteCacheFileSafe();
                }
                changed = true;
            }
        }
        if (changed)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>Formats the source tooltip (e.g. "from your network, 10:41").</summary>
    public string? FormatSourceTooltip(bool persianUi)
    {
        if (IsOptedOut)
        {
            return ResUI.TipOriginOptOut;
        }
        if (Current is { } loc)
        {
            var localTime = DateTimeOffset.FromUnixTimeSeconds(loc.Stamp).ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
            return string.Format(ResUI.TipOriginFromNetwork, persianUi ? ToPersianDigits(localTime) : localTime);
        }
        if (IsLocating)
        {
            return ResUI.TbLocating;
        }
        return null;
    }

    /// <summary>Replaces ASCII digits with Persian ones (۰–۹) for the Persian interface.</summary>
    public static string ToPersianDigits(string text)
    {
        var chars = text.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] is >= '0' and <= '9')
            {
                chars[i] = (char)('۰' + (chars[i] - '0'));
            }
        }
        return new string(chars);
    }

    private void EnsureLoaded()
    {
        lock (_gate)
        {
            if (_loaded)
            {
                return;
            }
            _loaded = true;
            RecheckOptOut();
            if (IsOptedOut)
            {
                DeleteCacheFileSafe();
                return;
            }
            if (TryReadCacheFile(_cacheFilePath, _utcNow().ToUnixTimeSeconds(), MaxDiskCacheAgeSeconds, out var cached))
            {
                _located = cached;
            }
        }
    }

    /// <summary>
    /// Refreshes the user's real location when Glide's proxy is off.
    /// Forces a fresh lookup on the first call after app start and when <paramref name="force"/> is true (e.g. on network change).
    /// </summary>
    public async Task<OriginGeoResult?> RefreshAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        EnsureLoaded();
        RecheckOptOut();

        var activeLocalIp = GetActiveOutboundLocalIp();
        var tunOn = _isProxyActive();
        var canBypass = !tunOn || _canBypassTunnel();
        bool shouldRun;
        var routeChanged = false;
        lock (_gate)
        {
            if (IsOptedOut)
            {
                _located = null;
                DeleteCacheFileSafe();
                return null;
            }
            if (_refreshing)
            {
                if (force)
                {
                    _pendingForceRefresh = true;
                }
                return _located;
            }

            // With Glide's TUN on, a lookup is only honest if it can leave through the physical adapter;
            // without one it would travel through the server and report the server's country.
            if (tunOn && !canBypass)
            {
                return _located;
            }

            var now = _utcNow();

            routeChanged = !string.IsNullOrEmpty(activeLocalIp)
                && !string.IsNullOrEmpty(_lastOutboundLocalIp)
                && !string.Equals(activeLocalIp, _lastOutboundLocalIp, StringComparison.OrdinalIgnoreCase);
            var needsStartupRefresh = !_startupRefreshed;
            var cacheFresh = _located is { } loc && (now.ToUnixTimeSeconds() - loc.Stamp) < CacheTtlSeconds;
            shouldRun = force || needsStartupRefresh || routeChanged || !cacheFresh;
            if (!shouldRun)
            {
                return _located;
            }

            _refreshing = true;
            _pendingForceRefresh = false;
        }

        Changed?.Invoke();

        bool runFollowUp = false;
        try
        {
            foreach (var url in _serviceUrls)
            {
                if (string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }
                cancellationToken.ThrowIfCancellationRequested();

                // TUN came on while an earlier service was tried and there is no adapter to go around it: stop.
                if (_isProxyActive() && !_canBypassTunnel())
                {
                    return _located;
                }

                try
                {
                    var body = await _fetchUrlAsync(url, cancellationToken).ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(body))
                    {
                        continue;
                    }

                    var sourceHost = TryGetHost(url);
                    if (!TryParseGeoResponse(body, out var parsed, sourceHost))
                    {
                        continue;
                    }

                    var updated = false;
                    OriginGeoResult? result = null;
                    lock (_gate)
                    {
                        var now = _utcNow();
                        var (exitCountry, exitIp) = _getExitInfo();
                        var normExitCountry = NormalizeCountryCode(exitCountry);

                        var hasBothIps = !string.IsNullOrWhiteSpace(exitIp) && !string.IsNullOrWhiteSpace(parsed.Ip);
                        var matchesExitIp = hasBothIps
                            && string.Equals(parsed.Ip!.Trim(), exitIp!.Trim(), StringComparison.OrdinalIgnoreCase);

                        var matchesExitCountry = normExitCountry != null
                            && string.Equals(parsed.Code, normExitCountry, StringComparison.OrdinalIgnoreCase);

                        // An answer with the proxy's own exit IP came through the server, not from here: never keep it.
                        if (matchesExitIp)
                        {
                            return _located;
                        }
                        if (!hasBothIps && matchesExitCountry && _located is { } existing && !string.Equals(existing.Code, parsed.Code, StringComparison.OrdinalIgnoreCase))
                        {
                            return _located;
                        }

                        var stamped = parsed with
                        {
                            Stamp = now.ToUnixTimeSeconds(),
                            Source = parsed.Source ?? sourceHost ?? "network",
                        };

                        if (_located is { } prev && !string.Equals(prev.Code, stamped.Code, StringComparison.OrdinalIgnoreCase))
                        {
                            _previousLocated = prev;
                        }
                        _located = stamped;
                        _startupRefreshed = true;
                        _lastOutboundLocalIp = GetActiveOutboundLocalIp() ?? activeLocalIp;
                        SaveCacheLocked(stamped);
                        result = stamped;
                        updated = true;
                    }

                    if (updated)
                    {
                        Changed?.Invoke();
                    }
                    return result;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    // Try the next geo-IP service.
                }
            }

            // Every service failed. If the computer is now on a different network (a new local address), the
            // remembered country may be from somewhere else: show nothing rather than a stale guess.
            if (routeChanged)
            {
                lock (_gate)
                {
                    _located = null;
                    _lastOutboundLocalIp = activeLocalIp;
                    DeleteCacheFileSafe();
                }
            }
            return _located;
        }
        finally
        {
            lock (_gate)
            {
                _refreshing = false;
                if (_pendingForceRefresh && !cancellationToken.IsCancellationRequested)
                {
                    _pendingForceRefresh = false;
                    runFollowUp = true;
                }
            }
            Changed?.Invoke();
            if (runFollowUp)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(1200, cancellationToken).ConfigureAwait(false);
                        await RefreshAsync(force: true, cancellationToken).ConfigureAwait(false);
                    }
                    catch
                    {
                        // Ignore follow-up cancellation.
                    }
                }, cancellationToken);
            }
        }
    }

    private void SaveCacheLocked(OriginGeoResult result)
    {
        try
        {
            if (string.IsNullOrEmpty(_cacheFilePath))
            {
                return;
            }
            var dir = Path.GetDirectoryName(_cacheFilePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            using var ms = new MemoryStream();
            using (var writer = new Utf8JsonWriter(ms))
            {
                writer.WriteStartObject();
                writer.WriteNumber("Version", CurrentCacheVersion);
                writer.WriteString("Code", result.Code);
                if (result.Lat is { } lat)
                {
                    writer.WriteNumber("Lat", lat);
                }
                if (result.Lon is { } lon)
                {
                    writer.WriteNumber("Lon", lon);
                }
                if (!string.IsNullOrWhiteSpace(result.Ip))
                {
                    writer.WriteString("Ip", result.Ip);
                }
                if (!string.IsNullOrWhiteSpace(result.Source))
                {
                    writer.WriteString("Source", result.Source);
                }
                writer.WriteNumber("Stamp", result.Stamp);
                writer.WriteEndObject();
            }
            File.WriteAllBytes(_cacheFilePath, ms.ToArray());
        }
        catch
        {
            // Ignore disk write failures.
        }
    }

    private void DeleteCacheFileSafe()
    {
        try
        {
            if (!string.IsNullOrEmpty(_cacheFilePath) && File.Exists(_cacheFilePath))
            {
                File.Delete(_cacheFilePath);
            }
        }
        catch
        {
            // Ignore delete failures.
        }
    }

    /// <summary>
    /// Reads and validates a v2 cache file, rejecting legacy v1 files and entries older than <paramref name="maxAgeSeconds"/>.
    /// </summary>
    public static bool TryReadCacheFile(string? path, long nowUnixSeconds, long maxAgeSeconds, out OriginGeoResult result)
    {
        result = default;
        try
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return false;
            }
            return TryParseCacheJson(File.ReadAllText(path), nowUnixSeconds, maxAgeSeconds, out result);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Parses a cached glide-origin.json payload, requiring <see cref="CurrentCacheVersion"/> and a timestamp within <paramref name="maxAgeSeconds"/>.
    /// </summary>
    public static bool TryParseCacheJson(string? json, long nowUnixSeconds, long maxAgeSeconds, out OriginGeoResult result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }
            // Reject legacy v1 cache files (which had 24h TTL and no proxy/TUN bypass).
            if (!root.TryGetProperty("Version", out var verProp)
                || verProp.ValueKind != JsonValueKind.Number
                || verProp.GetInt32() < CurrentCacheVersion)
            {
                return false;
            }
            if (!root.TryGetProperty("Code", out var codeProp)
                || codeProp.ValueKind != JsonValueKind.String
                || NormalizeCountryCode(codeProp.GetString()) is not { } code)
            {
                return false;
            }
            if (!root.TryGetProperty("Stamp", out var stampProp)
                || stampProp.ValueKind != JsonValueKind.Number
                || !stampProp.TryGetInt64(out var stamp)
                || stamp <= 0)
            {
                return false;
            }
            var age = nowUnixSeconds - stamp;
            if (age < -60 || age > maxAgeSeconds)
            {
                return false;
            }
            double? lat = root.TryGetProperty("Lat", out var la) && la.ValueKind == JsonValueKind.Number && la.TryGetDouble(out var lad) ? lad : null;
            double? lon = root.TryGetProperty("Lon", out var lo) && lo.ValueKind == JsonValueKind.Number && lo.TryGetDouble(out var lod) ? lod : null;
            string? ip = root.TryGetProperty("Ip", out var ipP) && ipP.ValueKind == JsonValueKind.String ? ipP.GetString() : null;
            string? src = root.TryGetProperty("Source", out var srcP) && srcP.ValueKind == JsonValueKind.String ? srcP.GetString() : null;
            result = new OriginGeoResult(code, lat, lon, ip, src, stamp);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Parses a JSON or key=value trace response from a geo-IP service into an <see cref="OriginGeoResult"/>.
    /// </summary>
    public static bool TryParseGeoResponse(string? body, out OriginGeoResult result, string? source = null)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }
        var trimmed = body.Trim();
        if (trimmed.StartsWith('{'))
        {
            return TryParseJsonGeo(trimmed, out result, source);
        }
        return TryParseTraceGeo(trimmed, out result, source);
    }

    private static bool TryParseJsonGeo(string json, out OriginGeoResult result, string? source)
    {
        result = default;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            // Reject explicit error / bogon / rate-limit payloads.
            if (root.TryGetProperty("success", out var succ) && succ.ValueKind == JsonValueKind.False)
            {
                return false;
            }
            if (root.TryGetProperty("is_bogon", out var bogon) && bogon.ValueKind == JsonValueKind.True)
            {
                return false;
            }
            if (root.TryGetProperty("error", out var err) && err.ValueKind is JsonValueKind.Object or JsonValueKind.String or JsonValueKind.True)
            {
                return false;
            }
            if (root.TryGetProperty("status", out var status))
            {
                if (status.ValueKind == JsonValueKind.String && status.GetString()?.Equals("fail", StringComparison.OrdinalIgnoreCase) == true)
                {
                    return false;
                }
                if (status.ValueKind == JsonValueKind.Number && status.TryGetInt32(out var codeNum) && codeNum >= 400)
                {
                    return false;
                }
            }

            var code = ExtractCountryCode(root);
            if (code == null && root.TryGetProperty("location", out var locObj) && locObj.ValueKind == JsonValueKind.Object)
            {
                code = ExtractCountryCode(locObj);
            }
            if (code == null)
            {
                return false;
            }

            var (lat, lon) = ExtractCoordinates(root);
            if ((lat == null || lon == null) && root.TryGetProperty("location", out var locCoords) && locCoords.ValueKind == JsonValueKind.Object)
            {
                var (nLat, nLon) = ExtractCoordinates(locCoords);
                lat ??= nLat;
                lon ??= nLon;
            }
            if (lat is { } laVal && lon is { } loVal)
            {
                if (double.IsNaN(laVal) || double.IsInfinity(laVal) || laVal < -90 || laVal > 90
                    || double.IsNaN(loVal) || double.IsInfinity(loVal) || loVal < -180 || loVal > 180
                    || (Math.Abs(laVal) < 0.0001 && Math.Abs(loVal) < 0.0001))
                {
                    lat = null;
                    lon = null;
                }
            }
            else
            {
                lat = null;
                lon = null;
            }

            string? ip = null;
            foreach (var ipField in new[] { "ip", "clientIp", "ip_addr", "query", "ipAddress" })
            {
                if (root.TryGetProperty(ipField, out var ipProp) && ipProp.ValueKind == JsonValueKind.String && ipProp.GetString() is { Length: > 0 } s)
                {
                    ip = s.Trim();
                    break;
                }
            }

            result = new OriginGeoResult(code, lat, lon, ip, source);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? ExtractCountryCode(JsonElement obj)
    {
        foreach (var field in new[] { "country_code", "countryCode", "country_iso", "country" })
        {
            if (obj.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.String)
            {
                var raw = v.GetString()?.Trim();
                if (NormalizeCountryCode(raw) is { } two)
                {
                    return two;
                }
                if (!string.IsNullOrEmpty(raw) && CountryNameToIso.TryGetValue(raw, out var mapped))
                {
                    return mapped;
                }
            }
        }
        return null;
    }

    private static (double? Lat, double? Lon) ExtractCoordinates(JsonElement obj)
    {
        var lat = TryGetDoubleProperty(obj, "latitude") ?? TryGetDoubleProperty(obj, "lat");
        var lon = TryGetDoubleProperty(obj, "longitude") ?? TryGetDoubleProperty(obj, "lon") ?? TryGetDoubleProperty(obj, "lng");
        if (lat != null && lon != null)
        {
            return (lat, lon);
        }
        if (obj.TryGetProperty("loc", out var locStr) && locStr.ValueKind == JsonValueKind.String && locStr.GetString() is { } s)
        {
            var parts = s.Split(',');
            if (parts.Length == 2
                && double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedLat)
                && double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedLon))
            {
                return (parsedLat, parsedLon);
            }
        }
        return (lat, lon);
    }

    private static double? TryGetDoubleProperty(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var prop))
        {
            return null;
        }
        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDouble(out var d))
        {
            return d;
        }
        if (prop.ValueKind == JsonValueKind.String
            && double.TryParse(prop.GetString()?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }
        return null;
    }

    private static bool TryParseTraceGeo(string text, out OriginGeoResult result, string? source)
    {
        result = default;
        string? code = null;
        string? ip = null;
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("loc=", StringComparison.OrdinalIgnoreCase))
            {
                code = NormalizeCountryCode(line[4..].Trim());
            }
            else if (line.StartsWith("ip=", StringComparison.OrdinalIgnoreCase))
            {
                var v = line[3..].Trim();
                if (v.Length > 0)
                {
                    ip = v;
                }
            }
        }
        if (code == null)
        {
            return false;
        }
        result = new OriginGeoResult(code, null, null, ip, source);
        return true;
    }

    public static string? NormalizeCountryCode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }
        var trimmed = raw.Trim().ToUpperInvariant();
        if (trimmed.Length != 2
            || !char.IsAsciiLetterUpper(trimmed[0])
            || !char.IsAsciiLetterUpper(trimmed[1])
            || trimmed is "XX" or "ZZ" or "T1" or "UN")
        {
            return null;
        }
        return trimmed == "UK" ? "GB" : trimmed;
    }

    private static string? TryGetHost(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : null;
    }

    public static bool DefaultIsGlideProxyActive()
    {
        try
        {
            var config = AppManager.Instance.Config;
            if (config == null)
            {
                return false;
            }
            // Only TUN captures the lookup's own connections (the system proxy is never used by it), so only
            // TUN makes the lookup go around the tunnel through the physical adapter.
            return config.TunModeItem?.EnableTun == true;
        }
        catch
        {
            return false;
        }
    }

    // ------------------------------------------------------------------------------------------------
    // Direct HTTP transport. It never uses the Windows system proxy (UseProxy = false) and, on Windows, binds
    // to the physical internet adapter (Wi-Fi / Ethernet) that carries the default route, so another VPN does
    // not move the answer. Host names are resolved with the system's own DNS only; FakeIP answers are refused
    // and the service is skipped (no DNS servers of our own are ever contacted).
    // ------------------------------------------------------------------------------------------------

    private static async Task<string?> FetchDirectAsync(string url, bool mustBypassTunnel, CancellationToken cancellationToken)
    {
        var physicalNic = FindPhysicalInternetInterface();
        if (physicalNic == null && mustBypassTunnel)
        {
            // TUN is on and there is no physical adapter to go around it: the answer would be the server's.
            return null;
        }
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(5));

        using var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            UseCookies = false,
            ConnectTimeout = TimeSpan.FromSeconds(4),
        };

        if (physicalNic is { } nic)
        {
            handler.ConnectCallback = async (ctx, ct) =>
            {
                var targetIp = await ResolveRealIpv4Async(ctx.DnsEndPoint.Host, nic, ct).ConfigureAwait(false)
                    ?? throw new HttpRequestException($"No usable IPv4 address for {ctx.DnsEndPoint.Host}");
                var sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    if (OperatingSystem.IsWindows() && nic.IfIndex > 0)
                    {
                        // Winsock IP_UNICAST_IF = 31 (interface index in network byte order).
                        var netIfIndex = IPAddress.HostToNetworkOrder(nic.IfIndex);
                        sock.SetSocketOption(SocketOptionLevel.IP, (SocketOptionName)31, BitConverter.GetBytes(netIfIndex));
                    }
                    sock.Bind(new IPEndPoint(nic.LocalIp, 0));
                    await sock.ConnectAsync(new IPEndPoint(targetIp, ctx.DnsEndPoint.Port), ct).ConfigureAwait(false);
                    // Make sure the connection really left from the physical adapter, not the tunnel.
                    if (sock.LocalEndPoint is not IPEndPoint local || !local.Address.Equals(nic.LocalIp))
                    {
                        throw new HttpRequestException("The lookup did not leave through the physical adapter.");
                    }
                    return new NetworkStream(sock, ownsSocket: true);
                }
                catch
                {
                    sock.Dispose();
                    throw;
                }
            };
        }

        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(5),
        };
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json, text/plain");

        using var response = await client.GetAsync(url, cts.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }
        return await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
    }

    private readonly record struct PhysicalNicInfo(int IfIndex, IPAddress LocalIp, NetworkInterfaceType Type, IReadOnlyList<IPAddress> DnsServers);

    /// <summary>
    /// Returns the local IPv4 address of the physical internet adapter (Wi-Fi / Ethernet) used for lookups.
    /// </summary>
    public static string? GetActiveOutboundLocalIp()
    {
        return FindPhysicalInternetInterface()?.LocalIp.ToString();
    }

    /// <summary>The local IPv4 address the OS would use for the internet (its default route); nothing is sent.</summary>
    private static IPAddress? DefaultRouteLocalIp()
    {
        try
        {
            using var probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            // Connecting a UDP socket only picks a route; no packet leaves the machine.
            probe.Connect(new IPEndPoint(new IPAddress([1, 1, 1, 1]), 53));
            return (probe.LocalEndPoint as IPEndPoint)?.Address;
        }
        catch
        {
            return null;
        }
    }

    private static PhysicalNicInfo? FindPhysicalInternetInterface()
    {
        try
        {
            var candidates = new List<PhysicalNicInfo>();
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up)
                {
                    continue;
                }
                if (nic.NetworkInterfaceType is not (NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.Ethernet))
                {
                    continue;
                }
                var label = $"{nic.Name} {nic.Description}";
                if (Regex.IsMatch(label, @"tun|tap|vpn|wintun|wireguard|warp|tailscale|zerotier|virtual|hyper-v|vmware|vbox|loopback", RegexOptions.IgnoreCase))
                {
                    continue;
                }
                var props = nic.GetIPProperties();
                var hasGateway = props.GatewayAddresses.Any(g =>
                    g.Address.AddressFamily == AddressFamily.InterNetwork
                    && !g.Address.Equals(IPAddress.Any));
                if (!hasGateway)
                {
                    continue;
                }
                var localIp = props.UnicastAddresses
                    .Select(u => u.Address)
                    .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a) && !IsApipaOrFakeIp(a));
                if (localIp == null)
                {
                    continue;
                }
                var ifIndex = 0;
                try
                {
                    ifIndex = props.GetIPv4Properties()?.Index ?? 0;
                }
                catch
                {
                    // Ignore if IPv4 properties are unavailable.
                }
                var dns = props.DnsAddresses
                    .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a) && !IsApipaOrFakeIp(a))
                    .ToList();
                candidates.Add(new PhysicalNicInfo(ifIndex, localIp, nic.NetworkInterfaceType, dns));
            }
            if (candidates.Count == 0)
            {
                return null;
            }
            // The adapter that carries the default route wins. When the default route is a VPN, prefer a wired
            // connection over Wi-Fi (the usual "docked laptop" case).
            var routeIp = DefaultRouteLocalIp();
            foreach (var c in candidates)
            {
                if (routeIp != null && c.LocalIp.Equals(routeIp))
                {
                    return c;
                }
            }
            return candidates.OrderBy(c => c.Type == NetworkInterfaceType.Ethernet ? 0 : 1).First();
        }
        catch
        {
            // Ignore interface enumeration errors.
        }
        return null;
    }

    private static bool IsApipaOrFakeIp(IPAddress ip)
    {
        if (ip.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }
        var b = ip.GetAddressBytes();
        // 169.254.0.0/16 (APIPA), 198.18.0.0/15 (FakeIP), 127.0.0.0/8 (loopback)
        return (b[0] == 169 && b[1] == 254)
            || (b[0] == 198 && (b[1] == 18 || b[1] == 19))
            || b[0] == 127;
    }

    private static async Task<IPAddress?> ResolveRealIpv4Async(string host, PhysicalNicInfo nic, CancellationToken ct)
    {
        if (IPAddress.TryParse(host, out var literal))
        {
            return literal.AddressFamily == AddressFamily.InterNetwork && !IsApipaOrFakeIp(literal) ? literal : null;
        }
        try
        {
            var addrs = await Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork, ct).ConfigureAwait(false);
            if (addrs.FirstOrDefault(a => !IsApipaOrFakeIp(a)) is { } real)
            {
                return real;
            }
        }
        catch
        {
            // Fall through to the adapter's own DNS.
        }
        // TUN answered with a fake address (or not at all): ask the DNS servers configured on the physical
        // adapter itself (the router or provider), through that adapter. No other DNS server is ever used.
        foreach (var server in nic.DnsServers)
        {
            if (await QueryARecordAsync(host, server, nic, ct).ConfigureAwait(false) is { } resolved && !IsApipaOrFakeIp(resolved))
            {
                return resolved;
            }
        }
        return null;
    }

    private static async Task<IPAddress?> QueryARecordAsync(string host, IPAddress server, PhysicalNicInfo nic, CancellationToken ct)
    {
        try
        {
            using var udp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            if (OperatingSystem.IsWindows() && nic.IfIndex > 0)
            {
                udp.SetSocketOption(SocketOptionLevel.IP, (SocketOptionName)31, BitConverter.GetBytes(IPAddress.HostToNetworkOrder(nic.IfIndex)));
            }
            udp.Bind(new IPEndPoint(nic.LocalIp, 0));
            var id = (ushort)Random.Shared.Next(1, 0xFFFF);
            var query = BuildDnsQuery(host, id);
            if (query == null)
            {
                return null;
            }
            var remote = new IPEndPoint(server, 53);
            await udp.SendToAsync(query, SocketFlags.None, remote, ct).ConfigureAwait(false);
            var buffer = new byte[512];
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(1500));
            var received = await udp.ReceiveFromAsync(buffer, SocketFlags.None, new IPEndPoint(IPAddress.Any, 0), timeout.Token).ConfigureAwait(false);
            // Only the server we asked, answering our question, counts.
            if (received.RemoteEndPoint is not IPEndPoint from || !from.Address.Equals(server))
            {
                return null;
            }
            return ParseDnsAnswer(buffer.AsSpan(0, received.ReceivedBytes), id);
        }
        catch
        {
            return null;
        }
    }

    private static byte[]? BuildDnsQuery(string host, ushort id)
    {
        using var ms = new MemoryStream();
        // Header: ID, standard query with recursion desired, one question.
        ms.Write([(byte)(id >> 8), (byte)(id & 0xFF), 0x01, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]);
        foreach (var label in host.TrimEnd('.').Split('.'))
        {
            var bytes = Encoding.ASCII.GetBytes(label);
            if (bytes.Length is 0 or > 63)
            {
                return null;
            }
            ms.WriteByte((byte)bytes.Length);
            ms.Write(bytes);
        }
        ms.WriteByte(0);
        ms.Write([0x00, 0x01, 0x00, 0x01]); // QTYPE A, QCLASS IN
        return ms.ToArray();
    }

    private static IPAddress? ParseDnsAnswer(ReadOnlySpan<byte> packet, ushort id)
    {
        if (packet.Length < 12 || ((packet[0] << 8) | packet[1]) != id || (packet[2] & 0x80) == 0 || (packet[3] & 0x0F) != 0)
        {
            return null;
        }
        var qdCount = (packet[4] << 8) | packet[5];
        var anCount = (packet[6] << 8) | packet[7];
        var pos = 12;
        for (var q = 0; q < qdCount; q++)
        {
            pos = SkipDnsName(packet, pos);
            if (pos < 0 || pos + 4 > packet.Length)
            {
                return null;
            }
            pos += 4;
        }
        for (var a = 0; a < anCount; a++)
        {
            pos = SkipDnsName(packet, pos);
            if (pos < 0 || pos + 10 > packet.Length)
            {
                return null;
            }
            var type = (packet[pos] << 8) | packet[pos + 1];
            var rdLen = (packet[pos + 8] << 8) | packet[pos + 9];
            pos += 10;
            if (pos + rdLen > packet.Length)
            {
                return null;
            }
            if (type == 1 && rdLen == 4)
            {
                return new IPAddress(packet.Slice(pos, 4));
            }
            pos += rdLen;
        }
        return null;
    }

    private static int SkipDnsName(ReadOnlySpan<byte> packet, int pos)
    {
        while (pos < packet.Length)
        {
            var len = packet[pos];
            if (len == 0)
            {
                return pos + 1;
            }
            if ((len & 0xC0) == 0xC0)
            {
                return pos + 2 <= packet.Length ? pos + 2 : -1;
            }
            pos += 1 + len;
        }
        return -1;
    }
}
