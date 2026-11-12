namespace ServiceLib.Tests.Common;

[NotInParallel(nameof(RecentStore))]
public class RecentStoreTests
{
    [Test]
    public async Task Add_KeepsNewestFirstWithoutDuplicates_AndRemembers()
    {
        var file = Path.Combine(Path.GetTempPath(), $"glide-recent-{Guid.NewGuid():N}.json");
        var original = RecentStore.FilePath;
        RecentStore.FilePath = file;
        RecentStore.ResetCache();
        try
        {
            foreach (var id in new[] { "a", "b", "c", "a", "d", "e", "f", "g" })
            {
                RecentStore.Add(id);
            }
            await string.Join(",", RecentStore.All()).Should().BeEqualTo("g,f,e,d,a,c");

            RecentStore.ResetCache();
            await string.Join(",", RecentStore.All()).Should().BeEqualTo("g,f,e,d,a,c");
        }
        finally
        {
            RecentStore.FilePath = original;
            RecentStore.ResetCache();
            File.Delete(file);
        }
    }
}
