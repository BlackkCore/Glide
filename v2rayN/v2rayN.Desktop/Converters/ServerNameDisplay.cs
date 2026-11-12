using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace v2rayN.Desktop.Converters;

/// <summary>
/// The server name as the list shows it: without emoji (the row already has a flag) and without the
/// plan details providers often put in names ("Turkey | 📊 9.72GB", "DE | 12 days left"). Display only:
/// the full name stays in the tooltip and is never changed.
/// </summary>
public static partial class ServerNameDisplay
{
    // A segment that is only plan information: an amount of data ("9.7GB"), days ("24D", "12 days left") or expiry.
    // F-06: data needs a byte unit ("9.7GB", "500 MiB"), so "5G" or "4K" in a real name is kept.
    [GeneratedRegex(@"^\s*\d+\s*D\s*$|\d+(\.\d+)?\s*(T|G|M|K)i?B\b|\b(days?|expire[sd]?|left|remaining)\b|روز|انقضا|باقی|حجم|گیگ|مگ", RegexOptions.IgnoreCase)]
    private static partial Regex PlanInfo();

    // One token of 8+ letters and digits, with both kinds in it: "OkmS7WWCP8", "a1b2c3d4".
    // F-06: a word followed by a number ("Node0001", "server-12") is a name, not an account code.
    [GeneratedRegex(@"^(?![A-Za-z]+[-_]?\d+$)(?=[A-Za-z0-9_-]*[A-Za-z])(?=[A-Za-z0-9_-]*\d)[A-Za-z0-9_-]{8,}$")]
    private static partial Regex AccountCode();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex Spaces();

    public static string Clean(string? remarks)
    {
        if (string.IsNullOrWhiteSpace(remarks))
        {
            return remarks ?? string.Empty;
        }
        var text = CountryDetector.RemoveFlags(remarks);

        // Drop emoji and their joiners / variation selectors.
        var sb = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            var emoji = rune.Value is 0x200D or 0xFE0F or 0x20E3
                || (rune.Value >= 0x1F000 && rune.Value <= 0x1FAFF)
                || (rune.Value >= 0x2600 && rune.Value <= 0x27BF)
                || (rune.Value >= 0x2190 && Rune.GetUnicodeCategory(rune) == UnicodeCategory.OtherSymbol);
            if (!emoji)
            {
                sb.Append(rune.ToString());
            }
        }

        // Keep the parts of "name | info | info" that aren't plan details.
        // After the first part, a lone code like "OkmS7WWCP8" (an account id from the provider) is left out too.
        var parts = sb.ToString()
            .Split(['|', '｜', '•'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            // F-06: the first part is the name and is always kept.
            .Where((p, i) => i == 0 || (!PlanInfo().IsMatch(p) && !AccountCode().IsMatch(p)))
            .ToList();
        var result = Spaces().Replace(string.Join(" · ", parts), " ").Trim(' ', '-', '_', '·');
        // Nothing sensible left (the whole name was plan details): show it as it is.
        return result.Length > 0 ? result : remarks.Trim();
    }
}
