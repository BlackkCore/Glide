using System.Runtime.CompilerServices;

namespace ServiceLib.Tests.Common;

/// <summary>Finds the repository root for tests that read repo files (lock, licences, workflows).</summary>
internal static class TestRepo
{
    public static string Root([CallerFilePath] string thisFile = "")
    {
        // Normally the compile-time path of this file. CI builds with deterministic paths ("/_/...") and the
        // build output may sit outside the repo, so fall back to walking up from the test binary.
        var fromSource = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile) ?? "", "..", "..", ".."));
        if (File.Exists(Path.Combine(fromSource, "cores.lock.json")))
        {
            return fromSource;
        }
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "cores.lock.json")))
            {
                return dir.FullName;
            }
        }
        throw new InvalidOperationException("Repository root (cores.lock.json) not found from the test files or the test binary.");
    }
}
