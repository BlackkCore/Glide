namespace ServiceLib.Common;

/// <summary>
/// Servers the user starred; they're listed first. Kept in guiConfigs/glide-favorites.json as a list
/// of server IndexIds, outside the upstream database so its schema stays as it is.
/// </summary>
public static class FavoriteStore
{
    private static readonly object Gate = new();
    private static HashSet<string>? _cache;

    // Public only so tests can point it (and ResetCache) somewhere else.
    public static string FilePath { get; set; } = Utils.GetConfigPath("glide-favorites.json");

    public static bool IsFavorite(string? indexId)
    {
        if (indexId.IsNullOrEmpty())
        {
            return false;
        }
        lock (Gate)
        {
            return LoadLocked().Contains(indexId);
        }
    }

    /// <summary>Stars or unstars a server; returns whether it is a favourite now.</summary>
    public static bool Toggle(string? indexId)
    {
        if (indexId.IsNullOrEmpty())
        {
            return false;
        }
        lock (Gate)
        {
            var all = LoadLocked();
            var now = all.Add(indexId) || !all.Remove(indexId);
            try
            {
                // Through JsonUtils: this build only serializes the types listed in AppJsonContext.
                File.WriteAllText(FilePath, JsonUtils.Serialize(all.ToList(), false));
            }
            catch
            {
                // Only a convenience: a read-only folder means the star isn't remembered.
            }
            return now;
        }
    }

    public static void ResetCache()
    {
        lock (Gate)
        {
            _cache = null;
        }
    }

    private static HashSet<string> LoadLocked()
    {
        if (_cache != null)
        {
            return _cache;
        }
        try
        {
            _cache = File.Exists(FilePath)
                ? (JsonUtils.Deserialize<List<string>>(File.ReadAllText(FilePath)) ?? []).ToHashSet()
                : [];
        }
        catch
        {
            _cache = [];
        }
        return _cache;
    }
}
