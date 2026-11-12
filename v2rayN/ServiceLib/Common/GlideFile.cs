namespace ServiceLib.Common;

/// <summary>
/// Glide's small JSON state files (favourites, recents, habits, plan info, usage, last session).
/// A crash mid-write must not leave a truncated file that the next start reads as "empty" and then
/// saves over, so writes go to a temp file that replaces the old one in a single rename.
/// </summary>
public static class GlideFile
{
    /// <summary>Writes the text so the file is either the old or the new version; false (logged) on failure.</summary>
    public static bool WriteAtomic(string path, string text)
    {
        var tmp = $"{path}.tmp";
        try
        {
            File.WriteAllText(tmp, text);
            File.Move(tmp, path, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"{nameof(GlideFile)} write {Path.GetFileName(path)}", ex);
            try
            {
                File.Delete(tmp);
            }
            catch
            {
                // Nothing more to do: the old file is untouched.
            }
            return false;
        }
    }

    /// <summary>
    /// The parsed file, or null when it is missing or empty. A file that is there but can't be parsed
    /// (parse throws or returns null) is set aside first.
    /// </summary>
    public static T? Read<T>(string path, Func<string, T?> parse) where T : class
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }
            var text = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }
            var value = parse(text);
            if (value == null)
            {
                SetAside(path, new InvalidDataException("unreadable"));
            }
            return value;
        }
        catch (Exception ex)
        {
            SetAside(path, ex);
            return null;
        }
    }

    // A file that couldn't be read is copied to "*.bad" before the store starts over, so the next
    // save doesn't destroy the only copy of the user's data.
    private static void SetAside(string path, Exception ex)
    {
        Logging.SaveLog($"{nameof(GlideFile)} read {Path.GetFileName(path)}", ex);
        try
        {
            if (File.Exists(path))
            {
                File.Copy(path, $"{path}.bad", overwrite: true);
            }
        }
        catch
        {
            // Logged above; the store still works, it just starts empty.
        }
    }
}
