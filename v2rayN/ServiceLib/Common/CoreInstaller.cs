using System.IO.Compression;

namespace ServiceLib.Common;

public enum CoreInstallError
{
    None,
    MissingFile,
    SizeMismatch,
    HashMismatch,
    BadArchive,
    MissingEntry,
    AmbiguousEntry,
    EntryHashMismatch,
    TooLarge,
    SwapFailed,
    NotVerified,
    NoSpace,
}

public sealed record CoreInstallResult(bool Ok, CoreInstallError Error, string Message)
{
    public static CoreInstallResult Success { get; } = new(true, CoreInstallError.None, "");
}

/// <summary>
/// Glide: installs an engine zip only if it is exactly the build the signed index lists.
/// 1. the whole zip must match the signed size and SHA-256;
/// 2. only the files the index lists are extracted (by leaf name, into a staging folder, never by a path taken from the zip);
/// 3. each extracted file must match its signed SHA-256;
/// 4. the swap keeps the previous files and rolls back if anything fails (a running, locked engine for example).
/// Nothing is written into the engine folder until every check has passed.
/// </summary>
public static class CoreInstaller
{
    public const long DefaultMaxFileBytes = 250L * 1024 * 1024;
    public const string StagingPrefix = ".glide-staging-";
    public const string PreviousFolder = ".glide-previous";

    /// <summary>Size and SHA-256 of the downloaded zip against the signed values.</summary>
    public static CoreInstallResult VerifyArchive(string zipPath, CoreIndexAsset asset)
    {
        if (!File.Exists(zipPath))
        {
            return Fail(CoreInstallError.MissingFile, "The downloaded file is missing.");
        }
        try
        {
            using var stream = OpenLocked(zipPath);
            return VerifyArchive(stream, asset);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Fail(CoreInstallError.MissingFile, ex.Message);
        }
    }

    /// <summary>Open for reading with writes and deletes denied to others, so what is verified is what is extracted.</summary>
    private static FileStream OpenLocked(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.Read);

    private static CoreInstallResult VerifyArchive(FileStream stream, CoreIndexAsset asset)
    {
        if (stream.Length != asset.Size)
        {
            return Fail(CoreInstallError.SizeMismatch, $"The download is {stream.Length} bytes; the signed list says {asset.Size}.");
        }
        stream.Position = 0;
        var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        stream.Position = 0;
        return hash.Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase)
            ? CoreInstallResult.Success
            : Fail(CoreInstallError.HashMismatch, "The download does not match the signed list.");
    }

    /// <summary>Staging plus the kept previous files need room: engines unpack to a few times the zip's size.</summary>
    public const int SpaceFactor = 4;

    private static long FreeBytes(string folder) => new DriveInfo(Path.GetPathRoot(Path.GetFullPath(folder))!).AvailableFreeSpace;

    /// <summary>ERROR_HANDLE_DISK_FULL (0x27) or ERROR_DISK_FULL (0x70).</summary>
    private static bool IsDiskFull(IOException ex) => (ex.HResult & 0xFFFF) is 0x27 or 0x70;

    private static CoreInstallError FailureKind(Exception ex, CoreInstallError otherwise)
        => ex is IOException io && IsDiskFull(io) ? CoreInstallError.NoSpace : otherwise;

    /// <param name="freeBytes">Free space on the engine folder's drive; tests replace it.</param>
    public static CoreInstallResult Install(string zipPath, CoreIndexAsset asset, string binDir, long maxFileBytes = DefaultMaxFileBytes, Func<string, long>? freeBytes = null)
    {
        FileStream stream;
        try
        {
            // One handle for the check and the extraction: the file can't be swapped in between.
            stream = OpenLocked(zipPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Fail(CoreInstallError.MissingFile, ex.Message);
        }

        using (stream)
        {
            var verified = VerifyArchive(stream, asset);
            if (!verified.Ok)
            {
                return verified;
            }

            string? staging = null;
            try
            {
                Directory.CreateDirectory(binDir);
                if ((freeBytes ?? FreeBytes)(binDir) < asset.Size * SpaceFactor)
                {
                    return Fail(CoreInstallError.NoSpace, "There is not enough free disk space.");
                }
                staging = Path.Combine(binDir, StagingPrefix + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(staging);
                var extracted = Extract(stream, asset, staging, maxFileBytes);
                return extracted.Ok ? Swap(asset, staging, binDir) : extracted;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The engine folder itself can't be written (permissions): nothing was replaced.
                return Fail(FailureKind(ex, CoreInstallError.SwapFailed), ex.Message);
            }
            finally
            {
                if (staging != null)
                {
                    TryDeleteDirectory(staging);
                }
            }
        }
    }

    private static CoreInstallResult Extract(Stream zipStream, CoreIndexAsset asset, string staging, long maxFileBytes)
    {
        try
        {
            using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);
            foreach (var (name, expected) in asset.Files)
            {
                // Normally the file keeps its name; a signed "sources" entry says which zip entry becomes this file.
                var zipName = asset.Sources != null && asset.Sources.TryGetValue(name, out var source) ? source : name;
                var matches = zip.Entries
                    .Where(e => e.Name.Equals(zipName, StringComparison.OrdinalIgnoreCase) && e.Length > 0)
                    .ToList();
                if (matches.Count == 0)
                {
                    return Fail(CoreInstallError.MissingEntry, $"{zipName} is not in the download.");
                }
                if (matches.Count > 1)
                {
                    return Fail(CoreInstallError.AmbiguousEntry, $"{zipName} appears more than once in the download.");
                }
                var entry = matches[0];
                if (!IsPlainEntryPath(entry.FullName))
                {
                    return Fail(CoreInstallError.BadArchive, $"{name} has an unsafe path in the download.");
                }

                // The destination is built from the signed leaf name, never from the zip's own path.
                var target = Path.Combine(staging, name);
                using (var input = entry.Open())
                using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                {
                    var buffer = new byte[81920];
                    long total = 0;
                    int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        total += read;
                        if (total > maxFileBytes)
                        {
                            return Fail(CoreInstallError.TooLarge, $"{name} is larger than allowed.");
                        }
                        hasher.AppendData(buffer, 0, read);
                        output.Write(buffer, 0, read);
                    }
                    var actual = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
                    if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                    {
                        return Fail(CoreInstallError.EntryHashMismatch, $"{name} does not match the signed list.");
                    }
                }
            }
            return CoreInstallResult.Success;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return Fail(FailureKind(ex, CoreInstallError.BadArchive), ex.Message);
        }
    }

    /// <summary>No rooted path, no drive, no ".." segment: the entry is read, never used to build a path, but a zip with these is not what the index describes.</summary>
    private static bool IsPlainEntryPath(string fullName)
    {
        if (fullName.Length == 0 || fullName[0] is '/' or '\\' || fullName.Contains(':'))
        {
            return false;
        }
        return !fullName.Split('/', '\\').Any(s => s == "..");
    }

    private static CoreInstallResult Swap(CoreIndexAsset asset, string staging, string binDir)
    {
        var previous = Path.Combine(binDir, PreviousFolder);
        var movedAway = new List<(string Target, string Backup)>();
        var installed = new List<string>();
        try
        {
            Directory.CreateDirectory(previous);
            foreach (var name in asset.Files.Keys)
            {
                var target = Path.Combine(binDir, name);
                if (File.Exists(target))
                {
                    var backup = Path.Combine(previous, name);
                    File.Move(target, backup, true);
                    movedAway.Add((target, backup));
                }
                File.Move(Path.Combine(staging, name), target);
                installed.Add(target);
            }
            return CoreInstallResult.Success;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Rollback(installed, movedAway);
            return Fail(FailureKind(ex, CoreInstallError.SwapFailed), ex.Message);
        }
    }

    private static void Rollback(List<string> installed, List<(string Target, string Backup)> movedAway)
    {
        foreach (var target in installed)
        {
            TryDelete(target);
        }
        foreach (var (target, backup) in movedAway)
        {
            try
            {
                if (File.Exists(backup))
                {
                    File.Move(backup, target, true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Logging.SaveLog(nameof(CoreInstaller), ex);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logging.SaveLog(nameof(CoreInstaller), ex);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logging.SaveLog(nameof(CoreInstaller), ex);
        }
    }

    private static CoreInstallResult Fail(CoreInstallError error, string message) => new(false, error, message);
}
