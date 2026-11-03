namespace ServiceLib.Tests.Common;

[NotInParallel(nameof(FavoriteStore))]
public class FavoriteStoreTests
{
    [Test]
    public async Task Toggle_StarsUnstarsAndRemembers()
    {
        var file = Path.Combine(Path.GetTempPath(), $"glide-fav-{Guid.NewGuid():N}.json");
        var original = FavoriteStore.FilePath;
        FavoriteStore.FilePath = file;
        FavoriteStore.ResetCache();
        try
        {
            await FavoriteStore.IsFavorite("a").Should().BeFalse();
            await FavoriteStore.Toggle("a").Should().BeTrue();
            await FavoriteStore.IsFavorite("a").Should().BeTrue();

            // Read back from the file, not the cache.
            FavoriteStore.ResetCache();
            await FavoriteStore.IsFavorite("a").Should().BeTrue();

            await FavoriteStore.Toggle("a").Should().BeFalse();
            FavoriteStore.ResetCache();
            await FavoriteStore.IsFavorite("a").Should().BeFalse();
            await FavoriteStore.Toggle(null).Should().BeFalse();
        }
        finally
        {
            FavoriteStore.FilePath = original;
            FavoriteStore.ResetCache();
            File.Delete(file);
        }
    }
}
