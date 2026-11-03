namespace ServiceLib.Common;

/// <summary>
/// Display form of a routing preset's remarks. The built-in presets are named like
/// "V4-绕过大陆(Whitelist)"; the UI shows just the English part ("Whitelist").
/// Names without Chinese are shown unchanged, and a name that would end up empty is kept as is.
/// </summary>
public static class RoutingName
{
    private static readonly Regex Preset = new(@"^V\d+\s*-[^(（]*[(（]\s*(?<name>[^)）]+?)\s*[)）]\s*$", RegexOptions.Compiled);
    private static readonly Regex Cjk = new(@"[　-〿㐀-䶿一-鿿豈-﫿＀-／]", RegexOptions.Compiled);

    public static string Display(string? remarks)
    {
        if (remarks.IsNullOrEmpty() || !Cjk.IsMatch(remarks))
        {
            return remarks ?? string.Empty;
        }

        var preset = Preset.Match(remarks);
        if (preset.Success && !Cjk.IsMatch(preset.Groups["name"].Value))
        {
            return preset.Groups["name"].Value;
        }

        var stripped = Cjk.Replace(remarks, string.Empty);
        stripped = Regex.Replace(stripped, @"\(\s*\)", string.Empty);
        stripped = Regex.Replace(stripped, @"\s{2,}", " ").Trim(' ', '-', '_', '|');
        return stripped.IsNullOrEmpty() ? remarks : stripped;
    }
}
