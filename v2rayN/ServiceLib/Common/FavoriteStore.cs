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

    /// <summary>Raised after a server is starred or unstarred (the tray menu listens).</summary>
    public static event Action? Changed;

    /// <summary>A copy of the starred server ids.</summary>
    public static IReadOnlyList<string> All()
    {
        lock (Gate)
        {
            return LoadLocked().ToList();
        }
    }

    /// <summary>Stars or unstars a server; returns whether it is a favourite now.</summary>
    public static bool Toggle(string? indexId)
    {
        if (indexId.IsNullOrEmpty())
        {
            return false;
        }
        bool now;
        lock (Gate)
        {
            now = ToggleLocked(indexId);
        }
        Changed?.Invoke();
        return now;
    }

    /// <summary>After a subscription refresh: stars follow servers to their new ids; servers that are gone lose theirs.</summary>
    public static void Remap(IReadOnlyDictionary<string, string> map, IReadOnlySet<string> dropped)
    {
        lock (Gate)
        {
            var all = LoadLocked();
            var moved = all.Where(map.ContainsKey).ToList();
            var gone = all.Where(dropped.Contains).ToList();
            if (moved.Count == 0 && gone.Count == 0)
            {
                return;
            }
            all.ExceptWith(moved);
            all.ExceptWith(gone);
            all.UnionWith(moved.Select(id => map[id]));
            SaveLocked(all);
        }
        Changed?.Invoke();
    }

    private static bool ToggleLocked(string indexId)
    {
        var all = LoadLocked();
        var now = all.Add(indexId) || !all.Remove(indexId);
        SaveLocked(all);
        return now;
    }

    private static void SaveLocked(HashSet<string> all)
    {
        try
        {
            // Through JsonUtils: this build only serializes the types listed in AppJsonContext.
            GlideFile.WriteAtomic(FilePath, JsonUtils.Serialize(all.ToList(), false));
        }
        catch
        {
            // Only a convenience: a read-only folder means the star isn't remembered.
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
        _cache = (GlideFile.Read(FilePath, JsonUtils.Deserialize<List<string>>) ?? []).ToHashSet();
        return _cache;
    }
}
