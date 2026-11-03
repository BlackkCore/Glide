namespace ServiceLib.Common;

/// <summary>
/// Data used / left and expiry that subscription providers send in the "subscription-userinfo"
/// response header ("upload=1; download=2; total=3; expire=1700000000"). Kept per subscription in
/// guiConfigs/glide-subinfo.json, keyed by a hash of the URL so no subscription token is ever stored.
/// </summary>
public static class SubscriptionInfoStore
{
    public sealed record Info(long Upload, long Download, long Total, long Expire, long UpdatedUnix)
    {
        public long Used => Upload + Download;
        public long Left => Total > 0 ? Math.Max(0, Total - Used) : 0;
        public DateTimeOffset? Expires => Expire > 0 ? DateTimeOffset.FromUnixTimeSeconds(Expire) : null;
        public DateTimeOffset Updated => DateTimeOffset.FromUnixTimeSeconds(UpdatedUnix);
    }

    private static readonly object Gate = new();
    private static Dictionary<string, Info>? _cache;

    // Public only so tests can point it (and ResetCache) somewhere else.
    public static string FilePath { get; set; } = Utils.GetConfigPath("glide-subinfo.json");

    public static void ResetCache()
    {
        lock (Gate)
        {
            _cache = null;
        }
    }

    public static string KeyFor(string url)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url.Trim())))[..32];
    }

    /// <summary>Parses the header value; null when it carries nothing useful.</summary>
    public static Info? Parse(string? header, DateTimeOffset now)
    {
        if (header.IsNullOrEmpty())
        {
            return null;
        }
        long Get(string name)
        {
            var match = Regex.Match(header, $@"(?:^|[;,\s]){name}\s*=\s*(\d+)", RegexOptions.IgnoreCase);
            return match.Success && long.TryParse(match.Groups[1].Value, out var v) ? v : 0;
        }
        var info = new Info(Get("upload"), Get("download"), Get("total"), Get("expire"), now.ToUnixTimeSeconds());
        return info.Total > 0 || info.Expire > 0 || info.Used > 0 ? info : null;
    }

    public static void Record(string url, string? header)
    {
        var info = Parse(header, DateTimeOffset.UtcNow);
        if (info == null || url.IsNullOrEmpty())
        {
            return;
        }
        lock (Gate)
        {
            var all = LoadLocked();
            all[KeyFor(url)] = info;
            try
            {
                // Through JsonUtils: this build only serializes the types listed in AppJsonContext.
                File.WriteAllText(FilePath, JsonUtils.Serialize(all, false));
            }
            catch
            {
                // Only a convenience: a read-only folder means the info isn't remembered.
            }
        }
    }

    public static Info? Get(string? url)
    {
        if (url.IsNullOrEmpty())
        {
            return null;
        }
        lock (Gate)
        {
            return LoadLocked().TryGetValue(KeyFor(url), out var info) ? info : null;
        }
    }

    private static Dictionary<string, Info> LoadLocked()
    {
        if (_cache != null)
        {
            return _cache;
        }
        try
        {
            _cache = File.Exists(FilePath)
                ? JsonUtils.Deserialize<Dictionary<string, Info>>(File.ReadAllText(FilePath)) ?? []
                : [];
        }
        catch
        {
            _cache = [];
        }
        return _cache;
    }
}
