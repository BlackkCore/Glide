using System.IO.Compression;

namespace ServiceLib.Common;

/// <summary>
/// "Export diagnostics": a zip for support with the last few days of logs and the settings file,
/// with addresses, passwords and tokens removed. The database (servers, subscriptions) is never
/// included.
/// </summary>
public static class DiagnosticsExport
{
    // Settings whose values identify servers or accounts.
    private static readonly Regex SensitiveKey = new("pass|secret|token|key|user|url|address|server|host|sni|id$", RegexOptions.IgnoreCase);

    public static void Create(string zipPath)
    {
        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);

        var logs = Directory.Exists(Utils.GetLogPath())
            ? Directory.GetFiles(Utils.GetLogPath(), "*.txt").OrderByDescending(File.GetLastWriteTime).Take(3)
            : [];
        foreach (var log in logs)
        {
            Write(zip, $"logs/{Path.GetFileName(log)}", LogSafe.Text(ReadShared(log)));
        }

        var config = Utils.GetConfigPath(Global.ConfigFileName);
        if (File.Exists(config))
        {
            Write(zip, "guiNConfig.json", RedactJson(ReadShared(config)));
        }
        Write(zip, "about.txt", $"{Utils.GetVersion()}{Environment.NewLine}{Environment.OSVersion}{Environment.NewLine}{DateTime.Now:yyyy-MM-dd HH:mm}");
    }

    /// <summary>Every value under a sensitive key becomes "•••"; other strings lose their addresses.</summary>
    public static string RedactJson(string json)
    {
        var root = JsonUtils.ParseJson(json);
        if (root == null)
        {
            return LogSafe.Text(json);
        }
        Redact(root);
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver() });
    }

    private static void Redact(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, value) in obj.ToList())
                {
                    if (value == null)
                    {
                        continue;
                    }
                    if (SensitiveKey.IsMatch(key) && value is JsonValue)
                    {
                        obj[key] = "•••";
                    }
                    else
                    {
                        Redact(value);
                        if (value is JsonValue v && v.TryGetValue<string>(out var s))
                        {
                            obj[key] = LogSafe.Text(s);
                        }
                    }
                }
                break;
            case JsonArray array:
                foreach (var item in array.Where(i => i != null))
                {
                    Redact(item!);
                }
                break;
        }
    }

    private static string ReadShared(string path)
    {
        // The running app keeps today's log open.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static void Write(ZipArchive zip, string name, string text)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open());
        writer.Write(text);
    }
}
