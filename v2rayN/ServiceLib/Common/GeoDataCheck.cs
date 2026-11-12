namespace ServiceLib.Common;

/// <summary>
/// Finds site and IP lists ("geosite:ir", "geoip:ir") that the active routing or DNS settings use but
/// the installed geosite.dat / geoip.dat don't contain. Xray refuses to start in that case, with only
/// "failed to check code IR from geosite.dat > EOF" in the log, so Glide names the missing list and
/// offers to download the site lists instead.
/// </summary>
public static class GeoDataCheck
{
    private static readonly Regex GeoRef = new(@"\b(geosite|geoip):!?([A-Za-z0-9][A-Za-z0-9_\-.!]*)", RegexOptions.IgnoreCase);

    private static readonly object CacheGate = new();
    private static readonly Dictionary<string, (DateTime Modified, long Length, HashSet<string> Codes)> Cache = [];

    /// <summary>"geosite:ir" style references in a rule or DNS text, lower-case, without attributes ("@ads") or "!".</summary>
    public static List<string> ExtractRefs(string? text)
    {
        if (text.IsNullOrEmpty())
        {
            return [];
        }
        return GeoRef.Matches(text)
            .Select(m => $"{m.Groups[1].Value.ToLowerInvariant()}:{m.Groups[2].Value.Split('@')[0].TrimStart('!').ToLowerInvariant()}")
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// The list names in a geosite.dat or geoip.dat: a protobuf list whose entries (field 1) each start
    /// with their country_code (field 1). Upper-case, as stored.
    /// </summary>
    public static HashSet<string> ReadCodes(byte[] data)
    {
        HashSet<string> codes = new(StringComparer.OrdinalIgnoreCase);
        var pos = 0;
        while (pos < data.Length && data[pos] == 0x0A)
        {
            pos++;
            if (!TryReadVarint(data, ref pos, out var entryLength))
            {
                break;
            }
            var entryEnd = pos + (long)entryLength;
            if (entryEnd > data.Length)
            {
                break;
            }
            var p = pos;
            if (p < entryEnd && data[p] == 0x0A)
            {
                p++;
                if (TryReadVarint(data, ref p, out var codeLength) && p + (long)codeLength <= entryEnd)
                {
                    codes.Add(Encoding.ASCII.GetString(data, p, (int)codeLength));
                }
            }
            pos = (int)entryEnd;
        }
        return codes;
    }

    private static bool TryReadVarint(byte[] data, ref int pos, out ulong value)
    {
        value = 0;
        for (var shift = 0; shift < 64 && pos < data.Length; shift += 7)
        {
            var b = data[pos++];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Codes in a .dat file, read once per file version. Null when the file is missing or unreadable.</summary>
    public static HashSet<string>? CodesIn(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return null;
            }
            lock (CacheGate)
            {
                if (Cache.TryGetValue(path, out var cached) && cached.Modified == info.LastWriteTimeUtc && cached.Length == info.Length)
                {
                    return cached.Codes;
                }
            }
            var codes = ReadCodes(File.ReadAllBytes(path));
            lock (CacheGate)
            {
                Cache[path] = (info.LastWriteTimeUtc, info.Length, codes);
            }
            return codes;
        }
        catch (Exception ex)
        {
            Logging.SaveLog("GeoDataCheck", ex);
            return null;
        }
    }

    /// <summary>The references that the given .dat files don't contain (a missing file counts as containing nothing).</summary>
    public static List<string> FindMissing(IEnumerable<string> refs, HashSet<string>? siteCodes, HashSet<string>? ipCodes)
    {
        return refs
            .Where(r =>
            {
                var colon = r.IndexOf(':');
                var codes = r.StartsWith("geosite:", StringComparison.OrdinalIgnoreCase) ? siteCodes : ipCodes;
                return codes == null || !codes.Contains(r[(colon + 1)..]);
            })
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// Site and IP lists used by the active routing and the Xray DNS settings but missing from the
    /// installed files, e.g. ["geosite:ir"]. Empty when everything is there.
    /// </summary>
    public static async Task<List<string>> FindMissingAsync(Config config)
    {
        List<string> refs = [];
        var routing = await ConfigHandler.GetDefaultRouting(config);
        if (routing != null)
        {
            refs.AddRange(ExtractRuleRefs(routing.RuleSet));
        }
        var dns = await AppManager.Instance.GetDNSItem(ECoreType.Xray);
        if (dns is { Enabled: true })
        {
            refs.AddRange(ExtractRefs(dns.NormalDNS));
            refs.AddRange(ExtractRefs(dns.TunDNS));
        }
        if (refs.Count == 0)
        {
            return [];
        }
        // geosite.dat is tens of MB: read it off the caller's (UI) thread.
        return await Task.Run(() => FindMissing(refs, CodesIn(Utils.GetBinPath("geosite.dat")), CodesIn(Utils.GetBinPath("geoip.dat"))));
    }

    /// <summary>References in a routing rule set, from enabled rules only (a disabled rule doesn't stop Xray).</summary>
    public static List<string> ExtractRuleRefs(string? ruleSet)
    {
        var rules = JsonUtils.Deserialize<List<RulesItem>>(ruleSet);
        if (rules == null)
        {
            return ExtractRefs(ruleSet);
        }
        return rules.Where(r => r.Enabled)
            .SelectMany(r => (r.Domain ?? []).Concat(r.Ip ?? []))
            .SelectMany(ExtractRefs)
            .Distinct()
            .ToList();
    }
}
