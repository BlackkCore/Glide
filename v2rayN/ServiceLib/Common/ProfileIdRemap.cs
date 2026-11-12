namespace ServiceLib.Common;

/// <summary>
/// A subscription refresh deletes its servers and adds them again under new IndexIds. This pairs each
/// old server with its new copy so Glide's own id-keyed stores (favourites, recents, habits) follow it.
/// Pairs are one-to-one and only made when unambiguous: a wrong star is worse than a lost one.
/// </summary>
public static class ProfileIdRemap
{
    /// <summary>Old id -> new id for matched servers, and the old ids that have no new copy.</summary>
    public sealed record Result(IReadOnlyDictionary<string, string> Map, IReadOnlySet<string> Dropped);

    /// <summary>Raised after a refresh remapped ids (off the UI thread). Desktop-only stores listen.</summary>
    public static event Action<Result>? Remapped;

    /// <summary>
    /// Pairs servers in three passes: same server and name, same server renamed, same name moved
    /// elsewhere. In each pass a pair counts only when neither side has another candidate left.
    /// </summary>
    /// <param name="same">(old, new, compareRemarks) -> same server; upstream's ConfigHandler comparer.</param>
    public static Result Build(IReadOnlyList<ProfileItem> olds, IReadOnlyList<ProfileItem> news, Func<ProfileItem, ProfileItem, bool, bool> same)
    {
        var map = new Dictionary<string, string>();
        var oldLeft = olds.Where(o => o.IndexId.IsNotEmpty()).DistinctBy(o => o.IndexId).ToList();
        var newLeft = news.Where(n => n.IndexId.IsNotEmpty()).DistinctBy(n => n.IndexId).ToList();

        // The comparer requires type, address and port to match, so only compare within those buckets.
        Pass(o => $"{o.ConfigType}|{o.Address}|{o.Port}", (o, n) => same(o, n, true));
        Pass(o => $"{o.ConfigType}|{o.Address}|{o.Port}", (o, n) => same(o, n, false));
        Pass(o => o.Remarks.IsNullOrEmpty() ? null : o.Remarks, (o, n) => o.Remarks == n.Remarks);

        return new Result(map, oldLeft.Select(o => o.IndexId).ToHashSet());

        void Pass(Func<ProfileItem, string?> bucket, Func<ProfileItem, ProfileItem, bool> match)
        {
            var newByBucket = newLeft.Where(n => bucket(n) != null).ToLookup(n => bucket(n)!);
            var oldByBucket = oldLeft.Where(o => bucket(o) != null).ToLookup(o => bucket(o)!);
            var pairs = new List<(ProfileItem Old, ProfileItem New)>();
            foreach (var group in oldByBucket)
            {
                var newsHere = newByBucket[group.Key].ToList();
                foreach (var o in group)
                {
                    var candidates = newsHere.Where(n => match(o, n)).Take(2).ToList();
                    if (candidates.Count == 1 && group.Count(other => match(other, candidates[0])) == 1)
                    {
                        pairs.Add((o, candidates[0]));
                    }
                }
            }
            foreach (var (o, n) in pairs)
            {
                map[o.IndexId] = n.IndexId;
                oldLeft.Remove(o);
                newLeft.Remove(n);
            }
        }
    }

    /// <summary>Moves favourites, recents and (through <see cref="Remapped"/>) habits to the new ids.</summary>
    public static void Apply(Result result)
    {
        if (result.Map.Count == 0 && result.Dropped.Count == 0)
        {
            return;
        }
        FavoriteStore.Remap(result.Map, result.Dropped);
        RecentStore.Remap(result.Map, result.Dropped);
        try
        {
            Remapped?.Invoke(result);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(ProfileIdRemap), ex);
        }
    }
}
