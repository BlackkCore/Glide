using System.Globalization;

namespace v2rayN.Desktop.Common;

public static class SpeedSize
{
    /// <summary>
    /// Parses Utils.HumanFy output such as "184.6 KB" back into the amount HumanFy was given
    /// (its first unit is KB), so <c>Utils.HumanFy((long)ParseSize(s))</c> round-trips.
    /// </summary>
    public static double ParseSize(string? text)
    {
        if (text.IsNullOrEmpty())
        {
            return 0;
        }
        var match = System.Text.RegularExpressions.Regex.Match(text, @"([\d.,]+)\s*([KMGT]?)B", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success || !double.TryParse(match.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return 0;
        }
        return match.Groups[2].Value.ToUpperInvariant() switch
        {
            "K" => value,
            "M" => value * 1024,
            "G" => value * 1024 * 1024,
            "T" => value * 1024 * 1024 * 1024,
            _ => 0
        };
    }
}
