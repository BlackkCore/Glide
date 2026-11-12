namespace ServiceLib.Common;

/// <summary>
/// English needs "the" before some country names ("Connected to the Netherlands", not "Connected to Netherlands").
/// The names come from the system's region data, so the rule works on the name as given; it is used only for the
/// English headline (Persian names take no article).
/// </summary>
public static class CountryGrammar
{
    // Names that need "the" and do not follow a pattern below.
    private static readonly HashSet<string> TheNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Netherlands", "Philippines", "Bahamas", "Gambia", "Maldives", "Seychelles", "Comoros",
        "Caribbean Netherlands", "Netherlands Antilles", "Palestinian Territories", "Czech Republic",
    };

    // The region data names these in a way that reads badly in a sentence.
    private static readonly Dictionary<string, string> Phrases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Congo - Kinshasa"] = "the Democratic Republic of the Congo",
        ["Congo (DRC)"] = "the Democratic Republic of the Congo",
        ["Congo - Brazzaville"] = "the Republic of the Congo",
        ["Congo"] = "the Republic of the Congo",
    };

    /// <summary>The name ready for "Connected to {0}": with "the" where English wants it, unchanged otherwise.</summary>
    public static string WithArticle(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return name ?? string.Empty;
        }
        var n = name.Trim();
        if (Phrases.TryGetValue(n, out var phrase))
        {
            return phrase;
        }
        if (n.StartsWith("the ", StringComparison.OrdinalIgnoreCase))
        {
            return n;
        }
        var wantsThe = TheNames.Contains(n)
            || n.StartsWith("United ", StringComparison.Ordinal)
            || n.StartsWith("Republic of ", StringComparison.Ordinal)
            || n.EndsWith(" Republic", StringComparison.Ordinal)
            || n.Contains(" Islands", StringComparison.Ordinal)
            || n.Contains(" Territories", StringComparison.Ordinal)
            || n.EndsWith(" Territory", StringComparison.Ordinal);
        return wantsThe ? "the " + n : n;
    }
}
