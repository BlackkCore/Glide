namespace ServiceLib.Common;

/// <summary>
/// Guesses a server's country (ISO 3166 alpha-2) from its remarks, for the country chip in the server list.
/// Looks for a flag emoji first, then country and city names (English and Persian), then an
/// upper-case two-letter code such as "DE". Returns null when nothing matches.
/// </summary>
public static class CountryDetector
{
    // code: English names and cities | Persian names
    private static readonly string[] Table =
    [
        "AE: united arab emirates, uae, emirates, dubai, abu dhabi | امارات, دبی",
        "AM: armenia, yerevan | ارمنستان, ایروان",
        "AR: argentina, buenos aires | آرژانتین",
        "AT: austria, vienna | اتریش",
        "AU: australia, sydney, melbourne | استرالیا",
        "AZ: azerbaijan, baku | آذربایجان, باکو",
        "BE: belgium, brussels | بلژیک",
        "BG: bulgaria, sofia | بلغارستان",
        "BH: bahrain | بحرین",
        "BR: brazil, sao paulo | برزیل",
        "CA: canada, toronto, montreal, vancouver | کانادا, تورنتو",
        "CH: switzerland, zurich, geneva | سوئیس",
        "CN: china, beijing, shanghai, shenzhen | چین",
        "CY: cyprus | قبرس",
        "CZ: czech, czechia, prague",
        "DE: germany, frankfurt, berlin, munich, nuremberg, falkenstein, dusseldorf | آلمان, فرانکفورت",
        "DK: denmark, copenhagen | دانمارک",
        "EE: estonia, tallinn | استونی",
        "ES: spain, madrid, barcelona | اسپانیا",
        "FI: finland, helsinki | فنلاند, هلسینکی",
        "FR: france, paris, marseille, gravelines, roubaix | فرانسه, پاریس",
        "GB: united kingdom, england, britain, london, manchester | انگلیس, انگلستان, بریتانیا, لندن",
        "GE: georgia, tbilisi | گرجستان",
        "GR: greece, athens | یونان",
        "HK: hong kong, hongkong | هنگ کنگ, هنگکنگ",
        "HU: hungary, budapest | مجارستان",
        "ID: indonesia, jakarta | اندونزی",
        "IE: ireland, dublin | ایرلند",
        "IL: israel, tel aviv",
        "IN: india, mumbai, bangalore, new delhi | هند",
        "IQ: iraq, baghdad | عراق",
        "IR: iran, tehran | ایران, تهران",
        "IT: italy, milan, rome | ایتالیا",
        "JP: japan, tokyo, osaka | ژاپن, توکیو",
        "KR: south korea, korea, seoul",
        "KZ: kazakhstan, almaty | قزاقستان",
        "LT: lithuania, vilnius | لیتوانی",
        "LU: luxembourg | لوکزامبورگ",
        "LV: latvia, riga | لتونی",
        "MD: moldova, chisinau | مولداوی",
        "MX: mexico | مکزیک",
        "MY: malaysia, kuala lumpur | مالزی",
        "NL: netherlands, holland, amsterdam, rotterdam | هلند, آمستردام",
        "NO: norway, oslo | نروژ",
        "OM: oman, muscat",
        "PL: poland, warsaw | لهستان, ورشو",
        "PT: portugal, lisbon | پرتغال",
        "QA: qatar, doha | قطر",
        "RO: romania, bucharest | رومانی",
        "RS: serbia, belgrade | صربستان",
        "RU: russia, moscow, saint petersburg | روسیه, مسکو",
        "SA: saudi arabia, saudi, riyadh | عربستان",
        "SE: sweden, stockholm | سوئد, استکهلم",
        "SG: singapore | سنگاپور",
        "TH: thailand, bangkok | تایلند",
        "TR: turkey, turkiye, istanbul, ankara | ترکیه, استانبول",
        "TW: taiwan, taipei | تایوان",
        "UA: ukraine, kyiv, kiev | اوکراین",
        "US: united states, usa, america, new york, los angeles, san jose, seattle, chicago, dallas, miami, silicon valley, ashburn | آمریکا, امریکا",
        "VN: vietnam, hanoi | ویتنام",
        "ZA: south africa, johannesburg | آفریقای جنوبی",
    ];

    // Two-letter tokens that are ordinary words far more often than country codes.
    private static readonly HashSet<string> AmbiguousCodes = ["AM", "AS", "AT", "BE", "DO", "GO", "ID", "IN", "IS", "IT", "ME", "MY", "NO", "OR", "SO", "TO", "UP"];

    private static readonly Lazy<(List<(string Name, string Code)> Names, HashSet<string> Codes)> Data = new(Build);

    public static string? Detect(string? remarks)
    {
        if (string.IsNullOrWhiteSpace(remarks))
        {
            return null;
        }

        var flag = FromFlagEmoji(remarks);
        if (flag != null)
        {
            return flag;
        }

        var (names, codes) = Data.Value;
        var words = Tokenize(remarks);
        var text = " " + string.Join(' ', words).ToLowerInvariant() + " ";

        // Longest names first, so "south africa" is tried before shorter names.
        foreach (var (name, code) in names)
        {
            if (text.Contains(" " + name + " ", StringComparison.Ordinal))
            {
                return code;
            }
        }

        foreach (var word in words)
        {
            if (word.Length == 2 && word.All(char.IsAsciiLetterUpper) && !AmbiguousCodes.Contains(word))
            {
                var code = word == "UK" ? "GB" : word;
                if (codes.Contains(code))
                {
                    return code;
                }
            }
        }
        return null;
    }

    /// <summary>The first regional-indicator pair in the text, e.g. "🇩🇪" -> "DE".</summary>
    public static string? FromFlagEmoji(string text)
    {
        const int first = 0x1F1E6;
        Rune? previous = null;
        foreach (var rune in text.EnumerateRunes())
        {
            if (previous is { } p)
            {
                var a = p.Value - first;
                var b = rune.Value - first;
                if (a is >= 0 and < 26 && b is >= 0 and < 26)
                {
                    return $"{(char)('A' + a)}{(char)('A' + b)}";
                }
            }
            previous = rune;
        }
        return null;
    }

    /// <summary>Removes a leading flag emoji (and the space after it); the chip shows the country instead.</summary>
    public static string StripLeadingFlag(string? remarks)
    {
        if (string.IsNullOrEmpty(remarks))
        {
            return remarks ?? string.Empty;
        }
        var text = remarks.TrimStart();
        // A flag is two regional indicators, each a surrogate pair: four UTF-16 units.
        if (text.Length >= 4 && FromFlagEmoji(text[..4]) != null)
        {
            var rest = text[4..].TrimStart();
            return rest.Length > 0 ? rest : remarks;
        }
        return remarks;
    }

    // Splits on anything that is not a letter, so "DE-Frankfurt_02" gives "DE", "Frankfurt".
    // Arabic yeh/kaf are folded to their Persian forms so both spellings match.
    private static List<string> Tokenize(string text)
    {
        var words = new List<string>();
        var current = new StringBuilder();
        foreach (var ch in text)
        {
            if (char.IsLetter(ch))
            {
                current.Append(ch switch { 'ي' => 'ی', 'ك' => 'ک', _ => ch });
            }
            else if (current.Length > 0)
            {
                words.Add(current.ToString());
                current.Clear();
            }
        }
        if (current.Length > 0)
        {
            words.Add(current.ToString());
        }
        return words;
    }

    private static (List<(string Name, string Code)> Names, HashSet<string> Codes) Build()
    {
        var names = new List<(string Name, string Code)>();
        var codes = new HashSet<string>();
        foreach (var row in Table)
        {
            var code = row[..2];
            codes.Add(code);
            foreach (var name in row[3..].Split([',', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                names.Add((string.Join(' ', Tokenize(name)).ToLowerInvariant(), code));
            }
        }
        names.Sort((x, y) => y.Name.Length.CompareTo(x.Name.Length));
        return (names, codes);
    }
}
