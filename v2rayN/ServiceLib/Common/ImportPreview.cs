namespace ServiceLib.Common;

/// <summary>
/// What pasted text contains, shown before anything is imported: server share links
/// (vless://, vmess://, …, also inside a base64 block) and subscription addresses (http/https).
/// Reads only; the import itself still goes through ConfigHandler.AddBatchServers.
/// </summary>
public static class ImportPreview
{
    public sealed record Result(int Subscriptions, IReadOnlyList<ProfileItem> Servers)
    {
        public bool IsEmpty => Subscriptions == 0 && Servers.Count == 0;

        /// <summary>"VLESS ×2, Trojan", most common first.</summary>
        public string Protocols => string.Join(", ", Servers
            .GroupBy(s => s.ConfigType)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Count() > 1 ? $"{g.Key} ×{g.Count()}" : g.Key.ToString()));
    }

    public static Result Analyze(string? text)
    {
        var data = text?.Trim() ?? string.Empty;
        if (data.IsNullOrEmpty())
        {
            return new(0, []);
        }
        if (Utils.IsBase64String(data))
        {
            var decoded = Utils.Base64Decode(data);
            if (decoded.Contains("://"))
            {
                data = decoded;
            }
        }

        var subscriptions = 0;
        var servers = new List<ProfileItem>();
        foreach (var line in data.Split('\r', '\n').Select(l => l.Trim()).Where(l => l.IsNotEmpty()))
        {
            // Same rule as AddBatchServers: a pasted http(s) address is added as a subscription.
            if (line.StartsWith(Global.HttpsProtocol) || line.StartsWith(Global.HttpProtocol))
            {
                subscriptions++;
                continue;
            }
            if (FmtHandler.ResolveConfig(line, out _) is { } item)
            {
                servers.Add(item);
            }
        }
        return new(subscriptions, servers);
    }
}
