namespace ServiceLib.Services;

public sealed record CoreIndexLoad(CoreIndexDocument? Document, string Message, string? RawJson = null, string? RawSignature = null);

/// <summary>Where the index comes from and which key and state it is checked against. Production uses <see cref="Default"/>; tests use their own.</summary>
public sealed record CoreIndexOptions(string IndexUrl, string? PublicKeyPem, string StatePath)
{
    public static CoreIndexOptions Default => new(CoreIndexClient.IndexUrl, UpdateSignatureKey.PublicKeyPem, CoreIndexState.DefaultPath());
}

/// <summary>What the portable "Check update" does for one engine, decided from a verified index.</summary>
public sealed record CoreUpdateDecision(CoreIndexPick? Pick, bool UpToDate, string? Message);

/// <summary>
/// Glide: fetches the signed engine index and decides what an engine may be updated to. Used only by the portable
/// version, and only on Windows. The index address and how it is verified are described in SECURITY.md.
/// </summary>
public static class CoreIndexClient
{
    /// <summary>A fixed release asset of the app's repository, replaced whenever a new index is signed.</summary>
    public static string IndexUrl => $"{Global.GithubUrl}/{Global.AppReleaseRepo}/releases/download/cores-index/cores-index.json";

    /// <summary>
    /// Downloads the index and its ".sig256", verifies both, and remembers the sequence number. Fail closed:
    /// any problem returns no document and a message the user can read; nothing unverified is ever returned.
    /// </summary>
    public static async Task<CoreIndexLoad> LoadAsync(
        Func<string, CancellationToken, Task<string?>> fetch,
        string? publicKeyPem,
        DateTimeOffset now,
        string statePath,
        CancellationToken cancellationToken = default,
        string? indexUrl = null)
    {
        indexUrl ??= IndexUrl;
        string? json;
        string? signature;
        try
        {
            json = await fetch(indexUrl, cancellationToken);
            signature = json.IsNullOrEmpty() ? null : await fetch(indexUrl + UpdateSignature.Extension, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logging.SaveLog(nameof(CoreIndexClient), ex);
            return new CoreIndexLoad(null, ResUI.MsgCoreIndexUnavailable);
        }
        if (json.IsNullOrEmpty() || signature.IsNullOrEmpty())
        {
            return new CoreIndexLoad(null, ResUI.MsgCoreIndexUnavailable);
        }

        var result = CoreIndexVerifier.Verify(Encoding.UTF8.GetBytes(json), signature, publicKeyPem, now, CoreIndexState.Load(statePath));
        if (!result.Ok)
        {
            Logging.SaveLog($"{nameof(CoreIndexClient)}: {result.Error}: {result.Message}");
            var message = result.Error switch
            {
                CoreIndexError.Expired => ResUI.MsgCoreIndexExpired,
                CoreIndexError.Replay => ResUI.MsgCoreIndexReplay,
                _ => ResUI.MsgCoreIndexUnavailable,
            };
            return new CoreIndexLoad(null, message);
        }

        // Remembered only after a fully valid index, so a bad file can never raise the bar.
        CoreIndexState.Save(statePath, result.Document!.Sequence);
        return new CoreIndexLoad(result.Document, "", json, signature);
    }

    public static CoreUpdateDecision Decide(
        CoreIndexDocument doc, ECoreType type, string arch, SemanticVersion glideVersion, SemanticVersion? lockedMax, SemanticVersion installed)
    {
        var key = CoreIndexKeys.For(type);
        var name = Global.CoreDisplayName(type);
        var pick = key == null ? null : CoreIndexVerifier.Pick(doc, key, arch, glideVersion, lockedMax);
        if (pick == null)
        {
            return new CoreUpdateDecision(null, false, string.Format(ResUI.MsgCoreNoVerifiedUpdate, name));
        }
        var zero = new SemanticVersion(0, 0, 0);
        if (installed >= pick.Version && !installed.Equals(zero))
        {
            return new CoreUpdateDecision(null, true, string.Format(ResUI.IsLatestCore, type, installed.ToStandardVersionString("v")));
        }
        return new CoreUpdateDecision(pick, false, null);
    }

    /// <summary>A warning when the engine that is installed now has been withdrawn in the signed index; null otherwise.</summary>
    public static string? RevokedWarning(CoreIndexDocument doc, ECoreType type, SemanticVersion installed)
    {
        var key = CoreIndexKeys.For(type);
        var revocation = key == null ? null : CoreIndexVerifier.RevocationOf(doc, key, installed.ToString());
        return revocation == null
            ? null
            : string.Format(ResUI.MsgCoreRevoked, Global.CoreDisplayName(type), installed.ToStandardVersionString("v"), revocation.Reason ?? "");
    }
}

/// <summary>
/// The one place an engine zip becomes installed files: refuses anything that was not verified against the signed index
/// when it was downloaded, then installs it through the staged, hash-checked installer.
/// </summary>
public static class VerifiedCoreInstall
{
    public static CoreInstallResult Install(string zipPath, string binDir)
    {
        if (!VerifiedCoreDownloads.TryGet(zipPath, out var asset) || asset == null)
        {
            return new CoreInstallResult(false, CoreInstallError.NotVerified, "The download was not verified against the signed engine list.");
        }
        try
        {
            return CoreInstaller.Install(zipPath, asset, binDir);
        }
        finally
        {
            VerifiedCoreDownloads.Remove(zipPath);
        }
    }
}

/// <summary>
/// Engine zips that were checked against the signed index when they were downloaded. The installer step refuses an engine zip
/// that is not in here, so there is no path from a download to a running engine that skips the check.
/// </summary>
public static class VerifiedCoreDownloads
{
    private static readonly ConcurrentDictionary<string, CoreIndexAsset> Items = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The newest engine list this session verified, as downloaded; handed on so the administrator process can verify it again itself.</summary>
    public static (string Json, string Signature)? LatestIndex { get; set; }

    public static void Register(string zipPath, CoreIndexAsset asset) => Items[Path.GetFullPath(zipPath)] = asset;

    public static bool TryGet(string zipPath, out CoreIndexAsset? asset) => Items.TryGetValue(Path.GetFullPath(zipPath), out asset);

    public static void Remove(string zipPath) => Items.TryRemove(Path.GetFullPath(zipPath), out _);
}
