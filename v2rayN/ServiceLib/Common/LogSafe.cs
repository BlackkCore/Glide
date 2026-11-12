namespace ServiceLib.Common;

/// <summary>
/// Glide: text that is safe to write to logs or share. Subscription addresses carry their token in
/// the path or query, so only the scheme and host are kept.
/// </summary>
public static class LogSafe
{
    public static string Url(string? url)
    {
        if (url.IsNullOrEmpty())
        {
            return string.Empty;
        }
        return Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) && uri.Host.IsNotEmpty()
            ? $"{uri.Scheme}://{uri.Host}/…"
            : "…";
    }

    // Any "scheme://…" (share links carry the uuid/password before the host), UUIDs, and long
    // base64 runs (encoded share links or subscription bodies). All linear: no nested quantifiers.
    // Generous: the patterns are linear, the timeout is only a backstop. 1 s tripped under heavy load
    // (a cold, fully parallel test run), and a timeout costs the whole log line.
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(5);
    private static readonly Regex Link = new(@"\b([A-Za-z][A-Za-z0-9+.-]{1,15})://[^\s""'<>]+", RegexOptions.Compiled, MatchTimeout);
    private static readonly Regex Uuid = new(@"\b[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\b", RegexOptions.Compiled, MatchTimeout);
    // shortcut: also hides 40+ char runs without dots or backslashes (forward-slash paths, hex hashes);
    // narrow it if support logs start losing such lines.
    private static readonly Regex Base64Run =new(@"(?<![A-Za-z0-9+/_-])[A-Za-z0-9+/_-]{40,}={0,2}", RegexOptions.Compiled, MatchTimeout);

    /// <summary>
    /// A text with secrets removed (for logs and exports): http(s) addresses keep only scheme and host,
    /// other links only their scheme; UUIDs and long base64 runs become "•••".
    /// </summary>
    public static string Text(string? text)
    {
        if (text.IsNullOrEmpty())
        {
            return string.Empty;
        }
        try
        {
            text = Link.Replace(text, m => m.Groups[1].Value.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? Url(m.Value)
                : $"{m.Groups[1].Value}://…");
            text = Uuid.Replace(text, "•••");
            return Base64Run.Replace(text, "•••");
        }
        catch (RegexMatchTimeoutException)
        {
            // Can't tell what is safe in it, so none of it is.
            return "…";
        }
    }
}
