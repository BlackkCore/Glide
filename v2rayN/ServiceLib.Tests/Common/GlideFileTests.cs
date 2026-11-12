namespace ServiceLib.Tests.Common;

[NotInParallel(nameof(FavoriteStore))]
public class GlideFileTests
{
    [Test]
    public async Task WriteAtomic_ReplacesAndLeavesNoTemp()
    {
        var file = Path.Combine(Path.GetTempPath(), $"glide-atomic-{Guid.NewGuid():N}.json");
        try
        {
            await GlideFile.WriteAtomic(file, "[\"a\"]").Should().BeTrue();
            await GlideFile.WriteAtomic(file, "[\"b\"]").Should().BeTrue();
            await File.ReadAllText(file).Should().BeEqualTo("[\"b\"]");
            await File.Exists($"{file}.tmp").Should().BeFalse();
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Test]
    public async Task WriteAtomic_UnwritableFolder_ReturnsFalse()
    {
        var file = Path.Combine(Path.GetTempPath(), $"no-such-dir-{Guid.NewGuid():N}", "x.json");
        await GlideFile.WriteAtomic(file, "x").Should().BeFalse();
    }

    [Test]
    public async Task CorruptFavourites_AreSetAsideBeforeBeingOverwritten()
    {
        var file = Path.Combine(Path.GetTempPath(), $"glide-fav-{Guid.NewGuid():N}.json");
        var original = FavoriteStore.FilePath;
        FavoriteStore.FilePath = file;
        FavoriteStore.ResetCache();
        try
        {
            // A truncated write from an older build.
            File.WriteAllText(file, "[\"abc\",\"de");
            await FavoriteStore.All().Count.Should().BeEqualTo(0);
            await File.ReadAllText($"{file}.bad").Should().BeEqualTo("[\"abc\",\"de");

            FavoriteStore.Toggle("x");
            await File.ReadAllText($"{file}.bad").Should().BeEqualTo("[\"abc\",\"de");
        }
        finally
        {
            FavoriteStore.FilePath = original;
            FavoriteStore.ResetCache();
            File.Delete(file);
            File.Delete($"{file}.bad");
        }
    }

    [Test]
    public async Task ExistingFavouritesFormat_StillReads()
    {
        var file = Path.Combine(Path.GetTempPath(), $"glide-fav-{Guid.NewGuid():N}.json");
        var original = FavoriteStore.FilePath;
        FavoriteStore.FilePath = file;
        FavoriteStore.ResetCache();
        try
        {
            File.WriteAllText(file, "[\"id1\",\"id2\"]");
            await FavoriteStore.IsFavorite("id2").Should().BeTrue();
            await File.Exists($"{file}.bad").Should().BeFalse();
        }
        finally
        {
            FavoriteStore.FilePath = original;
            FavoriteStore.ResetCache();
            File.Delete(file);
        }
    }
}
