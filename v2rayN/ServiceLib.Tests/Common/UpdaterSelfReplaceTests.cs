namespace ServiceLib.Tests.Common;

public class UpdaterSelfReplaceTests
{
    // Tripwire only (it reads text; AmazTool has no unit-test host). The behaviour was reproduced and the fix verified by
    // running the published, trimmed AmazTool against a signed zip that contains AmazTool.exe (QA record, test U-04):
    // renaming the running exe aside first made every later extraction throw FileNotFoundException.
    [Test]
    public async Task Updater_ExtractsItsOwnExeLast()
    {
        var src = File.ReadAllText(Path.Combine(TestRepo.Root(), "v2rayN", "AmazTool", "UpgradeApp.cs"));
        await src.Contains("foreach (var entry in EntriesSelfLast(archive))").Should().BeTrue();
        await src.Contains("archive.Entries.OrderBy(IsSelf)").Should().BeTrue();
    }

    [Test]
    public async Task Updater_ReportsWhyAnExtractionFailed()
    {
        var src = File.ReadAllText(Path.Combine(TestRepo.Root(), "v2rayN", "AmazTool", "UpgradeApp.cs"));
        await src.Contains("ex.StackTrace").Should().BeFalse();
        await src.Contains("{ex.GetType().Name}: {ex.Message}").Should().BeTrue();
    }
}
