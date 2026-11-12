using System.Buffers.Binary;
using System.Globalization;

namespace ServiceLib.Handler;

/// <summary>
/// Country of an IP address from the MaxMind-format Country.mmdb in the bin folder, read entirely offline:
/// nothing is sent anywhere and no names are resolved (only literal IP addresses are looked up).
/// Used to give a flag to servers whose names don't say where they are.
/// </summary>
public static class GeoIpCountry
{
    private static readonly byte[] MetadataMarker = [0xAB, 0xCD, 0xEF, .. "MaxMind.com"u8];
    private static readonly object Gate = new();
    private static Reader? _reader;
    // Last-write time of the file at the last load attempt (MinValue: missing), so a database that
    // appears or is updated later is picked up instead of disabling flags for the whole session.
    private static DateTime? _loadedStamp;
    private static readonly Dictionary<string, string?> Cache = new(StringComparer.OrdinalIgnoreCase);

    // Country.mmdb is ~8 MB; even a City database is under 100 MB. Anything bigger isn't read.
    private const long MaxFileBytes = 256L * 1024 * 1024;

    /// <summary>Loads the database off the UI thread at startup, so the first list paint doesn't.</summary>
    public static void Preload()
    {
        lock (Gate)
        {
            EnsureLoadedLocked();
        }
    }

    private static void EnsureLoadedLocked()
    {
        var path = Utils.GetBinPath("Country.mmdb");
        var stamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        if (_loadedStamp == stamp)
        {
            return;
        }
        _loadedStamp = stamp;
        _reader = null;
        Cache.Clear();
        try
        {
            if (stamp != DateTime.MinValue && new FileInfo(path).Length <= MaxFileBytes)
            {
                _reader = Reader.Open(File.ReadAllBytes(path));
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("GeoIpCountry", ex);
        }
    }

    /// <summary>The two-letter country of a literal IP address, or null (names, private ranges, no database).</summary>
    public static string? Lookup(string? address)
    {
        if (string.IsNullOrWhiteSpace(address) || !IPAddress.TryParse(address.Trim().Trim('[', ']'), out var ip) || IsPrivate(ip))
        {
            return null;
        }
        lock (Gate)
        {
            if (Cache.TryGetValue(address, out var cached))
            {
                return cached;
            }
            EnsureLoadedLocked();
            if (_reader == null)
            {
                return null; // Not cached: the database may still arrive.
            }
            string? code;
            try
            {
                code = _reader.Country(ip);
            }
            catch
            {
                code = null;
            }
            Cache[address] = code;
            return code;
        }
    }

    /// <summary>Test hook: look up in a database given as bytes (a malformed one gives null, never throws).</summary>
    public static string? LookupIn(byte[] database, string address)
    {
        try
        {
            return IPAddress.TryParse(address, out var ip) ? Reader.Open(database)?.Country(ip) : null;
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or OverflowException or InvalidCastException or FormatException)
        {
            return null;
        }
    }

    private static bool IsPrivate(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip))
        {
            return true;
        }
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            return b[0] == 10 || b[0] == 0 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168)
                || (b[0] == 169 && b[1] == 254) || (b[0] == 100 && b[1] >= 64 && b[1] <= 127) || b[0] >= 224;
        }
        return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast;
    }

    private sealed class Reader
    {
        // S-10: a country record is a few dozen values; a hostile file can't make one lookup decode more.
        private const int MaxNodes = 10_000;

        private readonly byte[] _db;
        private readonly long _nodeCount;
        private readonly int _recordSize;
        private readonly int _ipVersion;
        private readonly long _dataStart;

        private Reader(byte[] db, long nodeCount, int recordSize, int ipVersion)
        {
            _db = db;
            _nodeCount = nodeCount;
            _recordSize = recordSize;
            _ipVersion = ipVersion;
            _dataStart = (nodeCount * recordSize * 2 / 8) + 16;
        }

        public static Reader? Open(byte[] db)
        {
            var marker = db.AsSpan().LastIndexOf(MetadataMarker);
            if (marker < 0)
            {
                return null;
            }
            var pos = (long)marker + MetadataMarker.Length;
            var metaBudget = MaxNodes;
            if (Decode(db, ref pos, pos, ref metaBudget) is not Dictionary<string, object?> meta)
            {
                return null;
            }
            var nodeCount = Convert.ToInt64(meta.GetValueOrDefault("node_count") ?? 0L, CultureInfo.InvariantCulture);
            var recordSize = Convert.ToInt32(meta.GetValueOrDefault("record_size") ?? 0L, CultureInfo.InvariantCulture);
            var ipVersion = Convert.ToInt32(meta.GetValueOrDefault("ip_version") ?? 4L, CultureInfo.InvariantCulture);
            // S-10: the search tree must fit inside the file, or Record() would read past it.
            if (nodeCount <= 0 || recordSize is not (24 or 28 or 32) || nodeCount > db.Length / (recordSize * 2 / 8))
            {
                return null;
            }
            return new Reader(db, nodeCount, recordSize, ipVersion);
        }

        public string? Country(IPAddress ip)
        {
            var bytes = ip.GetAddressBytes();
            if (bytes.Length == 16 && _ipVersion == 4)
            {
                return null;
            }
            long node = 0;
            var bitCount = bytes.Length * 8;
            if (bytes.Length == 4 && _ipVersion == 6)
            {
                // IPv4 addresses live under ::/96 in an IPv6 tree: follow 96 zero bits first.
                for (var i = 0; i < 96 && node < _nodeCount; i++)
                {
                    node = Record(node, 0);
                }
            }
            for (var i = 0; i < bitCount && node < _nodeCount; i++)
            {
                var bit = (bytes[i >> 3] >> (7 - (i & 7))) & 1;
                node = Record(node, bit);
            }
            if (node <= _nodeCount)
            {
                return null; // Not in the database.
            }
            var pos = _dataStart + (node - _nodeCount - 16);
            var budget = MaxNodes;
            return ExtractCode(Decode(_db, ref pos, _dataStart, ref budget));
        }

        private long Record(long node, int right)
        {
            var bytesPerNode = _recordSize * 2 / 8;
            var off = node * bytesPerNode;
            var b = _db.AsSpan((int)off, bytesPerNode);
            return _recordSize switch
            {
                24 => right == 0 ? (b[0] << 16) | (b[1] << 8) | b[2] : (b[3] << 16) | (b[4] << 8) | b[5],
                28 => right == 0
                    ? ((long)(b[3] & 0xF0) << 20) | ((long)b[0] << 16) | ((long)b[1] << 8) | b[2]
                    : ((long)(b[3] & 0x0F) << 24) | ((long)b[4] << 16) | ((long)b[5] << 8) | b[6],
                _ => right == 0 ? BinaryPrimitives.ReadUInt32BigEndian(b[..4]) : BinaryPrimitives.ReadUInt32BigEndian(b[4..8]),
            };
        }

        private static string? ExtractCode(object? value)
        {
            if (value is string { Length: 2 } s)
            {
                return s.ToUpperInvariant();
            }
            if (value is not Dictionary<string, object?> map)
            {
                return null;
            }
            foreach (var key in new[] { "country", "registered_country" })
            {
                if (map.GetValueOrDefault(key) is Dictionary<string, object?> c && c.GetValueOrDefault("iso_code") is string { Length: 2 } iso)
                {
                    return iso.ToUpperInvariant();
                }
            }
            return map.GetValueOrDefault("iso_code") is string { Length: 2 } top ? top.ToUpperInvariant() : null;
        }

        // MaxMind DB data section decoder (only what Country databases use, plus a safe skip of the rest).
        private static object? Decode(byte[] db, ref long pos, long dataStart, ref int budget, int depth = 0)
        {
            if (depth > 16 || pos < 0 || pos >= db.Length || --budget < 0)
            {
                return null;
            }
            var ctrl = db[pos++];
            var type = ctrl >> 5;
            if (type == 1)
            {
                // Pointer into the data section; the pointed-to value is read, and reading resumes after the pointer.
                var ss = (ctrl >> 3) & 0x3;
                long vvv = ctrl & 0x7;
                long target = ss switch
                {
                    0 => (vvv << 8) | db[pos],
                    1 => ((vvv << 16) | ((long)db[pos] << 8) | db[pos + 1]) + 2048,
                    2 => ((vvv << 24) | ((long)db[pos] << 16) | ((long)db[pos + 1] << 8) | db[pos + 2]) + 526336,
                    _ => BinaryPrimitives.ReadUInt32BigEndian(db.AsSpan((int)pos, 4)),
                };
                pos += ss + 1;
                var at = dataStart + target;
                return Decode(db, ref at, dataStart, ref budget, depth + 1);
            }
            if (type == 0)
            {
                type = 7 + db[pos++];
            }
            long size = ctrl & 0x1F;
            if (size >= 29)
            {
                size = size switch
                {
                    29 => 29 + db[pos],
                    30 => 285 + ((long)db[pos] << 8) + db[pos + 1],
                    _ => 65821 + ((long)db[pos] << 16) + ((long)db[pos + 1] << 8) + db[pos + 2],
                };
                pos += size switch { < 285 => 1, < 65821 => 2, _ => 3 };
            }
            switch (type)
            {
                case 2: // UTF-8 string
                {
                    if (pos + size > db.Length)
                    {
                        return null;
                    }
                    var s =Encoding.UTF8.GetString(db, (int)pos, (int)size);
                    pos += size;
                    return s;
                }
                case 7: // map
                {
                    var map = new Dictionary<string, object?>();
                    for (var i = 0; i < size && budget >= 0; i++)
                    {
                        var key =Decode(db, ref pos, dataStart, ref budget, depth + 1) as string ?? string.Empty;
                        map[key] = Decode(db, ref pos, dataStart, ref budget, depth + 1);
                    }
                    return map;
                }
                case 11: // array
                {
                    var list = new List<object?>();
                    for (var i = 0; i < size && budget >= 0; i++)
                    {
                        list.Add(Decode(db, ref pos, dataStart, ref budget, depth + 1));
                    }
                    return list;
                }
                case 5 or 6 or 8 or 9 or 10: // integers, big-endian, 'size' bytes
                {
                    long v = 0;
                    for (var i = 0; i < size && i < 8; i++)
                    {
                        v = (v << 8) | db[pos + i];
                    }
                    pos += size;
                    return v;
                }
                case 3: // double
                    pos += 8;
                    return null;
                case 15: // float
                    pos += 4;
                    return null;
                case 14: // boolean (value in size)
                    return size != 0;
                default: // bytes, containers, end markers: skip
                    pos += size;
                    return null;
            }
        }
    }
}
