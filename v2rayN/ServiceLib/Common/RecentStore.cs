namespace ServiceLib.Common;

/// <summary>
/// The servers used most recently, newest first, for the tray and the quick switcher. Kept in
/// guiConfigs/glide-recent.json as a list of server IndexIds, outside the upstream database.
/// </summary>
public static class RecentStore
{
    private const int Max = 6;
    private static readonly object Gate = new();
    private static List<string>? _cache;

    // Public only so tests can point it (and ResetCache) somewhere else.
    public static string FilePath { get; set; } = Utils.GetConfigPath("glide-recent.json");

    /// <summary>Raised after the list changes (the tray menu listens).</summary>
    public static event Action? Changed;

    public static IReadOnlyList<string> All()
    {
        lock (Gate)
        {
            return LoadLocked().ToList();
        }
    }

    /// <summary>Moves a server to the front (adding it when new) and keeps the newest few.</summary>
    public static void Add(string? indexId)
    {
        if (indexId.IsNullOrEmpty())
        {
            return;
        }
        lock (Gate)
        {
            var all = LoadLocked();
            if (all.Count > 0 && all[0] == indexId)
            {
                return;
            }
            all.Remove(indexId);
            all.Insert(0, indexId);
            if (all.Count > Max)
            {
                all.RemoveRange(Max, all.Count - Max);
            }
            try
            {
                // Through JsonUtils: this build only serializes the types listed in AppJsonContext.
                GlideFile.WriteAtomic(FilePath, JsonUtils.Serialize(all, false));
            }
            catch
            {
                // Only a convenience: a read-only folder means the list isn't remembered.
            }
        }
        Changed?.Invoke();
    }

    /// <summary>After a subscription refresh: entries follow servers to their new ids, order kept; gone servers drop out.</summary>
    public static void Remap(IReadOnlyDictionary<string, string> map, IReadOnlySet<string> dropped)
    {
        lock (Gate)
        {
            var all = LoadLocked();
            if (!all.Any(id => map.ContainsKey(id) || dropped.Contains(id)))
            {
                return;
            }
            var remapped = all.Where(id => !dropped.Contains(id))
                .Select(id => map.TryGetValue(id, out var to) ? to : id)
                .Distinct()
                .ToList();
            all.Clear();
            all.AddRange(remapped);
            try
            {
                GlideFile.WriteAtomic(FilePath, JsonUtils.Serialize(all, false));
            }
            catch
            {
                // Only a convenience, as in Add.
            }
        }
        Changed?.Invoke();
    }

    public static void ResetCache()
    {
        lock (Gate)
        {
            _cache = null;
        }
    }

    private static List<string> LoadLocked()
    {
        if (_cache != null)
        {
            return _cache;
        }
        _cache = GlideFile.Read(FilePath, JsonUtils.Deserialize<List<string>>) ?? [];
        return _cache;
    }
}
