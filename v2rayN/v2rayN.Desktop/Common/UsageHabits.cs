namespace v2rayN.Desktop.Common;

/// <summary>
/// Remembers which server was connected at which hour of the day, to suggest "your usual at this hour".
/// Kept only on this computer, in glide-habits.json in the config folder: server id -> 24 hourly counts.
/// </summary>
public static class UsageHabits
{
    private static readonly object Gate = new();
    private static Dictionary<string, int[]>? _counts;

    private static string FilePath => Utils.GetConfigPath("glide-habits.json");

    private static Dictionary<string, int[]> Load()
    {
        if (_counts != null)
        {
            return _counts;
        }
        _counts = [];
        // Plain JSON nodes: the app's JSON helper only knows its own pre-generated types (trimmed build).
        var stored = GlideFile.Read(FilePath, text => System.Text.Json.Nodes.JsonNode.Parse(text) as System.Text.Json.Nodes.JsonObject);
        foreach (var (id, node) in stored ?? [])
        {
            try
            {
                if (id.IsNotEmpty() && node is System.Text.Json.Nodes.JsonArray { Count: 24 } arr)
                {
                    _counts[id] = arr.Select(v => v?.GetValue<int>() ?? 0).ToArray();
                }
            }
            catch
            {
                // One odd entry (a hand edit) only drops that server's habit.
            }
        }
        return _counts;
    }

    /// <summary>After a subscription refresh: counts follow servers to their new ids; gone servers drop out.</summary>
    public static void Remap(ProfileIdRemap.Result result)
    {
        lock (Gate)
        {
            var counts = Load();
            var touched = counts.Keys.Where(id => result.Map.ContainsKey(id) || result.Dropped.Contains(id)).ToList();
            if (touched.Count == 0)
            {
                return;
            }
            foreach (var id in touched)
            {
                if (counts.Remove(id, out var hours) && result.Map.TryGetValue(id, out var to))
                {
                    counts[to] = hours;
                }
            }
            Save(counts);
        }
    }

    /// <summary>Counts one verified connection to this server at the current hour.</summary>
    public static void Record(string? serverId)
    {
        if (serverId.IsNullOrEmpty())
        {
            return;
        }
        lock (Gate)
        {
            var counts = Load();
            if (!counts.TryGetValue(serverId, out var hours))
            {
                hours = new int[24];
                counts[serverId] = hours;
            }
            var hour = DateTime.Now.Hour;
            hours[hour] = Math.Min(hours[hour] + 1, 10_000);
            Save(counts);
        }
    }

    private static void Save(Dictionary<string, int[]> counts)
    {
        try
        {
            var json = new System.Text.Json.Nodes.JsonObject();
            foreach (var (id, h) in counts)
            {
                json[id] = new System.Text.Json.Nodes.JsonArray(h.Select(v => (System.Text.Json.Nodes.JsonNode?)v).ToArray());
            }
            GlideFile.WriteAtomic(FilePath, json.ToJsonString());
        }
        catch
        {
            // A read-only folder only means the habit isn't remembered.
        }
    }

    /// <summary>
    /// The server most often used around this hour (this hour counts double, the hours beside it once),
    /// when it has been used here at least three times; otherwise null.
    /// </summary>
    public static string? UsualNow(IEnumerable<string> available)
    {
        var hour = DateTime.Now.Hour;
        lock (Gate)
        {
            var counts = Load();
            string? best = null;
            var bestScore = 0;
            foreach (var id in available)
            {
                if (!counts.TryGetValue(id, out var hours))
                {
                    continue;
                }
                var score = (hours[hour] * 2) + hours[(hour + 23) % 24] + hours[(hour + 1) % 24];
                if (score > bestScore)
                {
                    best = id;
                    bestScore = score;
                }
            }
            return bestScore >= 3 ? best : null;
        }
    }
}
