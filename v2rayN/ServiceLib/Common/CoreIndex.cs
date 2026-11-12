namespace ServiceLib.Common;

/// <summary>
/// Glide: the signed engine index. The portable version updates Xray, sing-box and mihomo independently of Glide
/// releases, but only to builds that Glide's maintainers have vetted and listed in this file. It is signed with the
/// same ECDSA P-256 release key as app updates (see <see cref="UpdateSignatureKey"/>), so a tampered release or
/// connection cannot make the app run an engine nobody vetted.
/// What it protects and what it does not is summarised in SECURITY.md.
/// </summary>
public sealed class CoreIndexAsset
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";

    /// <summary>The files Glide installs from the zip (leaf name -> SHA-256 of the extracted file).</summary>
    [JsonPropertyName("files")] public Dictionary<string, string> Files { get; set; } = [];

    /// <summary>Installed name -> name inside the zip, only for a file that is installed under another name (mihomo's versioned exe becomes mihomo.exe).</summary>
    [JsonPropertyName("sources")] public Dictionary<string, string>? Sources { get; set; }
}

public sealed class CoreIndexEntry
{
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("minGlide")] public string MinGlide { get; set; } = "";
    [JsonPropertyName("vettedOn")] public string? VettedOn { get; set; }
    [JsonPropertyName("assets")] public Dictionary<string, CoreIndexAsset> Assets { get; set; } = [];
}

public sealed class CoreIndexRevocation
{
    [JsonPropertyName("core")] public string Core { get; set; } = "";
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("reason")] public string? Reason { get; set; }
}

public sealed class CoreIndexDocument
{
    [JsonPropertyName("format")] public int Format { get; set; }
    [JsonPropertyName("sequence")] public long Sequence { get; set; }
    [JsonPropertyName("issued")] public DateTimeOffset Issued { get; set; }
    [JsonPropertyName("expires")] public DateTimeOffset Expires { get; set; }
    [JsonPropertyName("cores")] public Dictionary<string, List<CoreIndexEntry>> Cores { get; set; } = [];
    [JsonPropertyName("revoked")] public List<CoreIndexRevocation> Revoked { get; set; } = [];
}

public sealed class CoreIndexStateFile
{
    [JsonPropertyName("sequence")] public long Sequence { get; set; }
}

/// <summary>Source-generated JSON (the app is built for AOT, so reflection-based serialization is off).</summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(CoreIndexDocument))]
[JsonSerializable(typeof(CoreIndexStateFile))]
public partial class CoreIndexJsonContext : JsonSerializerContext;

public enum CoreIndexError
{
    None,
    BadSignature,
    Malformed,
    UnsupportedFormat,
    NotYetValid,
    Expired,
    TooLongLived,
    Replay,
}

public sealed record CoreIndexResult(CoreIndexDocument? Document, CoreIndexError Error, string Message)
{
    public bool Ok => Error == CoreIndexError.None && Document != null;
}

public sealed record CoreIndexPick(CoreIndexEntry Entry, CoreIndexAsset Asset, SemanticVersion Version);

public static class CoreIndexVerifier
{
    public const int SupportedFormat = 1;
    public const int MaxIndexBytes = 512 * 1024;

    /// <summary>An index may not claim a longer life than this, so one stolen old file cannot freeze users for long.</summary>
    public static readonly TimeSpan MaxLifetime = TimeSpan.FromDays(120);

    /// <summary>Tolerance for a wrong PC clock when checking "issued".</summary>
    public static readonly TimeSpan ClockSkew = TimeSpan.FromDays(1);

    public const long MaxAssetBytes = 300L * 1024 * 1024;

    /// <summary>
    /// Verifies the signature FIRST (nothing unsigned is parsed), then the format, the validity window and the
    /// sequence number. <paramref name="lastSeenSequence"/> is the highest sequence this PC has accepted before.
    /// </summary>
    public static CoreIndexResult Verify(byte[]? indexBytes, string? signatureBase64, string? publicKeyPem, DateTimeOffset now, long lastSeenSequence)
    {
        if (indexBytes is null || indexBytes.Length == 0 || indexBytes.Length > MaxIndexBytes)
        {
            return Fail(CoreIndexError.Malformed, "The engine list is empty or too large.");
        }

        using (var stream = new MemoryStream(indexBytes))
        {
            if (!UpdateSignatureKey.VerifyStream(stream, signatureBase64, publicKeyPem))
            {
                return Fail(CoreIndexError.BadSignature, "The engine list's signature does not match Glide's release key.");
            }
        }

        CoreIndexDocument? doc;
        try
        {
            doc = JsonSerializer.Deserialize(indexBytes, CoreIndexJsonContext.Default.CoreIndexDocument);
        }
        catch (JsonException)
        {
            return Fail(CoreIndexError.Malformed, "The engine list could not be read.");
        }
        if (doc is null)
        {
            return Fail(CoreIndexError.Malformed, "The engine list could not be read.");
        }
        if (doc.Format != SupportedFormat)
        {
            return Fail(CoreIndexError.UnsupportedFormat, $"The engine list uses format {doc.Format}, which this Glide does not know.");
        }
        if (doc.Issued > now + ClockSkew)
        {
            return Fail(CoreIndexError.NotYetValid, "The engine list is dated in the future; check this PC's clock.");
        }
        if (doc.Expires <= now)
        {
            return Fail(CoreIndexError.Expired, "The engine list has expired.");
        }
        if (doc.Expires - doc.Issued > MaxLifetime)
        {
            return Fail(CoreIndexError.TooLongLived, "The engine list claims a validity longer than allowed.");
        }
        if (doc.Sequence < lastSeenSequence)
        {
            return Fail(CoreIndexError.Replay, "The engine list is older than one already accepted.");
        }
        var bad = Validate(doc);
        if (bad != null)
        {
            return Fail(CoreIndexError.Malformed, bad);
        }
        return new CoreIndexResult(doc, CoreIndexError.None, "");
    }

    private static CoreIndexResult Fail(CoreIndexError error, string message) => new(null, error, message);

    private static readonly Regex Sha256Regex = new("^[0-9a-f]{64}$", RegexOptions.Compiled);
    private static readonly Regex FileNameRegex = new(@"^[A-Za-z0-9][A-Za-z0-9._-]*\.(exe|dll)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Shape checks on a signed document: defence in depth against a mistake when the index was made.</summary>
    private static string? Validate(CoreIndexDocument doc)
    {
        foreach (var (core, entries) in doc.Cores)
        {
            foreach (var e in entries)
            {
                if (!SemanticVersion.TryParse(e.Version, out _) || !SemanticVersion.TryParse(e.MinGlide, out _))
                {
                    return $"{core}: bad version or minGlide.";
                }
                foreach (var (arch, a) in e.Assets)
                {
                    if (a.Size <= 0 || a.Size > MaxAssetBytes || !Sha256Regex.IsMatch(a.Sha256))
                    {
                        return $"{core} {e.Version} {arch}: bad size or hash.";
                    }
                    if (!Uri.TryCreate(a.Url, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps
                        || !(u.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)))
                    {
                        return $"{core} {e.Version} {arch}: the download address must be https://github.com/...";
                    }
                    if (a.Files.Count == 0)
                    {
                        return $"{core} {e.Version} {arch}: no files listed.";
                    }
                    foreach (var (name, hash) in a.Files)
                    {
                        if (!FileNameRegex.IsMatch(name) || !Sha256Regex.IsMatch(hash))
                        {
                            return $"{core} {e.Version} {arch}: bad file entry '{name}'.";
                        }
                    }
                    foreach (var (name, source) in a.Sources ?? [])
                    {
                        if (!a.Files.ContainsKey(name) || !FileNameRegex.IsMatch(source))
                        {
                            return $"{core} {e.Version} {arch}: bad source for '{name}'.";
                        }
                    }
                }
            }
        }
        return null;
    }

    /// <summary>
    /// The newest listed, not withdrawn build of <paramref name="coreKey"/> for <paramref name="arch"/> that this Glide
    /// may install (minGlide), no newer than <paramref name="lockedMax"/> when one is set. Null when there is none.
    /// </summary>
    public static CoreIndexPick? Pick(CoreIndexDocument doc, string coreKey, string arch, SemanticVersion glideVersion, SemanticVersion? lockedMax = null)
    {
        if (!doc.Cores.TryGetValue(coreKey, out var entries))
        {
            return null;
        }
        CoreIndexPick? best = null;
        foreach (var e in entries)
        {
            if (!SemanticVersion.TryParse(e.Version, out var v) || v is null
                || !SemanticVersion.TryParse(e.MinGlide, out var min) || min is null
                || min > glideVersion
                || (lockedMax != null && v > lockedMax)
                || IsRevoked(doc, coreKey, e.Version)
                || !e.Assets.TryGetValue(arch, out var asset))
            {
                continue;
            }
            if (best == null || v > best.Version)
            {
                best = new CoreIndexPick(e, asset, v);
            }
        }
        return best;
    }

    public static bool IsRevoked(CoreIndexDocument doc, string coreKey, string version)
        => RevocationOf(doc, coreKey, version) != null;

    public static CoreIndexRevocation? RevocationOf(CoreIndexDocument doc, string coreKey, string version)
    {
        if (!SemanticVersion.TryParse(version, out var v) || v is null)
        {
            return null;
        }
        return doc.Revoked.FirstOrDefault(r => r.Core.Equals(coreKey, StringComparison.OrdinalIgnoreCase)
                                                && SemanticVersion.TryParse(r.Version, out var rv) && rv != null && rv.Equals(v));
    }
}

/// <summary>Which index keys belong to which engine, and which build of this PC.</summary>
public static class CoreIndexKeys
{
    public static string? For(ECoreType type) => type switch
    {
        ECoreType.Xray => "xray",
        ECoreType.sing_box => "singBox",
        ECoreType.mihomo => "mihomo",
        _ => null,
    };

    /// <summary>"win-x64" or "win-x86" on Windows; null elsewhere (the index lists only the Windows builds Glide ships).</summary>
    public static string? CurrentArch() => OperatingSystem.IsWindows()
        ? RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "win-x64",
            Architecture.X86 => "win-x86",
            _ => null,
        }
        : null;
}

/// <summary>The highest index sequence this PC has accepted: stops an old signed index from being replayed.</summary>
public static class CoreIndexState
{
    public static string DefaultPath() => Utils.GetConfigPath("cores-index-state.json");

    public static long Load(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize(File.ReadAllText(path), CoreIndexJsonContext.Default.CoreIndexStateFile)?.Sequence ?? 0 : 0;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    /// <summary>Raises the stored value; never lowers it. Written through a temporary file so a crash cannot truncate it.</summary>
    public static void Save(string path, long sequence)
    {
        if (sequence <= Load(path))
        {
            return;
        }
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(new CoreIndexStateFile { Sequence = sequence }, CoreIndexJsonContext.Default.CoreIndexStateFile));
        File.Move(tmp, path, true);
    }
}
