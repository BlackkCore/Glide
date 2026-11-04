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

    /// <summary>Every http(s) address in a text replaced by its safe form (for logs being exported).</summary>
    public static string Text(string? text)
    {
        if (text.IsNullOrEmpty())
        {
            return string.Empty;
        }
        return Regex.Replace(text, @"https?://[^\s""'<>]+", m => Url(m.Value));
    }
}
