using ServiceLib.Tests.CoreConfig;

namespace ServiceLib.Tests.Common;

[NotInParallel([nameof(FavoriteStore), nameof(RecentStore)])]
public class ProfileIdRemapTests
{
    private static ProfileItem Node(string id, string address, int port, string remarks, string password = "pw") => new()
    {
        IndexId = id,
        ConfigType = EConfigType.VLESS,
        Address = address,
        Port = port,
        Remarks = remarks,
        Password = password,
    };

    // Stand-in for ConfigHandler.CompareProfileItem: same endpoint and secret, optionally same name.
    private static bool Same(ProfileItem o, ProfileItem n, bool remarks) =>
        o.ConfigType == n.ConfigType && o.Address == n.Address && o.Port == n.Port && o.Password == n.Password
        && (!remarks || o.Remarks == n.Remarks);

    [Test]
    public async Task Build_ReorderedAndRenamed_PairsEachServer()
    {
        var olds = new[] { Node("o1", "1.1.1.1", 443, "DE 1 | 9GB"), Node("o2", "2.2.2.2", 443, "NL 1") };
        var news = new[] { Node("n2", "2.2.2.2", 443, "NL 1"), Node("n1", "1.1.1.1", 443, "DE 1 | 8GB") };

        var result = ProfileIdRemap.Build(olds, news, Same);

        await result.Map["o1"].Should().BeEqualTo("n1");
        await result.Map["o2"].Should().BeEqualTo("n2");
        await result.Dropped.Count.Should().BeEqualTo(0);
    }

    [Test]
    public async Task Build_RemovedServer_IsDropped()
    {
        var olds = new[] { Node("o1", "1.1.1.1", 443, "DE"), Node("o2", "2.2.2.2", 443, "NL") };
        var news = new[] { Node("n1", "1.1.1.1", 443, "DE") };

        var result = ProfileIdRemap.Build(olds, news, Same);

        await result.Map.Count.Should().BeEqualTo(1);
        await result.Dropped.Contains("o2").Should().BeTrue();
    }

    [Test]
    public async Task Build_IdenticalDuplicates_StayUnmapped()
    {
        var olds = new[] { Node("o1", "1.1.1.1", 443, "DE"), Node("o2", "1.1.1.1", 443, "DE") };
        var news = new[] { Node("n1", "1.1.1.1", 443, "DE"), Node("n2", "1.1.1.1", 443, "DE") };

        var result = ProfileIdRemap.Build(olds, news, Same);

        await result.Map.Count.Should().BeEqualTo(0);
        await result.Dropped.Count.Should().BeEqualTo(2);
    }

    [Test]
    public async Task Build_DuplicatesTellApartByName_PairByName()
    {
        // Same endpoint twice, different names: the exact pass pairs them, the rename pass is not needed.
        var olds = new[] { Node("o1", "1.1.1.1", 443, "A"), Node("o2", "1.1.1.1", 443, "B") };
        var news = new[] { Node("n2", "1.1.1.1", 443, "B"), Node("n1", "1.1.1.1", 443, "A") };

        var result = ProfileIdRemap.Build(olds, news, Same);

        await result.Map["o1"].Should().BeEqualTo("n1");
        await result.Map["o2"].Should().BeEqualTo("n2");
    }

    [Test]
    public async Task Build_SameNameNewAddress_PairsOnlyWhenNameIsUnique()
    {
        var olds = new[] { Node("o1", "1.1.1.1", 443, "Turkey"), Node("o2", "2.2.2.2", 443, "Iran"), Node("o3", "3.3.3.3", 443, "Iran") };
        var news = new[] { Node("n1", "9.9.9.9", 443, "Turkey"), Node("n2", "8.8.8.8", 443, "Iran"), Node("n3", "7.7.7.7", 443, "Iran") };

        var result = ProfileIdRemap.Build(olds, news, Same);

        await result.Map["o1"].Should().BeEqualTo("n1");
        await result.Map.ContainsKey("o2").Should().BeFalse();
        await result.Map.ContainsKey("o3").Should().BeFalse();
    }

    [Test]
    public async Task Build_DifferentServerWithSameEndpointButNewSecret_NotPairedExceptByName()
    {
        // Name changed and secret changed: nothing ties them together any more.
        var olds = new[] { Node("o1", "1.1.1.1", 443, "Old", "a") };
        var news = new[] { Node("n1", "1.1.1.1", 443, "New", "b") };

        var result = ProfileIdRemap.Build(olds, news, Same);

        await result.Map.Count.Should().BeEqualTo(0);
        await result.Dropped.Contains("o1").Should().BeTrue();
    }

    [Test]
    public async Task Stores_Remap_FollowAndDrop()
    {
        var fav = Path.Combine(Path.GetTempPath(), $"glide-fav-{Guid.NewGuid():N}.json");
        var rec = Path.Combine(Path.GetTempPath(), $"glide-rec-{Guid.NewGuid():N}.json");
        var (favOrig, recOrig) = (FavoriteStore.FilePath, RecentStore.FilePath);
        FavoriteStore.FilePath = fav;
        RecentStore.FilePath = rec;
        FavoriteStore.ResetCache();
        RecentStore.ResetCache();
        try
        {
            FavoriteStore.Toggle("o1");
            FavoriteStore.Toggle("o2");
            FavoriteStore.Toggle("manual");
            RecentStore.Add("o2");
            RecentStore.Add("manual");
            RecentStore.Add("o1");

            ProfileIdRemap.Apply(new ProfileIdRemap.Result(
                new Dictionary<string, string> { ["o1"] = "n1" },
                new HashSet<string> { "o2" }));

            FavoriteStore.ResetCache();
            RecentStore.ResetCache();
            await string.Join(",", FavoriteStore.All().OrderBy(x => x)).Should().BeEqualTo("manual,n1");
            await string.Join(",", RecentStore.All()).Should().BeEqualTo("n1,manual");
        }
        finally
        {
            FavoriteStore.FilePath = favOrig;
            RecentStore.FilePath = recOrig;
            FavoriteStore.ResetCache();
            RecentStore.ResetCache();
            File.Delete(fav);
            File.Delete(rec);
        }
    }

    [Test]
    public async Task AddBatchServers_Refresh_KeepsFavouriteOnSameServer()
    {
        var config = CoreConfigTestFactory.CreateConfig();
        CoreConfigTestFactory.BindAppManagerConfig(config);
        SQLiteHelper.Instance.CreateTable<SubItem>();
        SQLiteHelper.Instance.CreateTable<ProfileItem>();
        var subId = $"sub-remap-{Guid.NewGuid():N}";
        await SQLiteHelper.Instance.ReplaceAsync(new SubItem { Id = subId, Remarks = "sub", Url = "http://127.0.0.1/sub" });

        const string a = "vless://00000000-0000-0000-0000-00000000000a@10.0.0.1:443?encryption=none&type=tcp#node-a";
        const string b = "vless://00000000-0000-0000-0000-00000000000b@10.0.0.2:443?encryption=none&type=tcp#node-b";
        const string bRenamed = "vless://00000000-0000-0000-0000-00000000000b@10.0.0.2:443?encryption=none&type=tcp#node-b%20renamed";

        var fav = Path.Combine(Path.GetTempPath(), $"glide-fav-{Guid.NewGuid():N}.json");
        var original = FavoriteStore.FilePath;
        FavoriteStore.FilePath = fav;
        FavoriteStore.ResetCache();
        try
        {
            await ConfigHandler.AddBatchServers(config, $"{a}\n{b}", subId, true);
            var first = await AppManager.Instance.ProfileItems(subId) ?? [];
            var oldB = first.Single(p => p.Address == "10.0.0.2");
            FavoriteStore.Toggle(oldB.IndexId);

            // Reordered, renamed, and a fresh set of ids.
            await ConfigHandler.AddBatchServers(config, $"{bRenamed}\n{a}", subId, true);
            var second = await AppManager.Instance.ProfileItems(subId) ?? [];
            var newB = second.Single(p => p.Address == "10.0.0.2");

            await (newB.IndexId != oldB.IndexId).Should().BeTrue();
            await FavoriteStore.IsFavorite(newB.IndexId).Should().BeTrue();
            await FavoriteStore.IsFavorite(oldB.IndexId).Should().BeFalse();
            await FavoriteStore.All().Count.Should().BeEqualTo(1);
        }
        finally
        {
            FavoriteStore.FilePath = original;
            FavoriteStore.ResetCache();
            File.Delete(fav);
        }
    }

    [Test]
    public async Task AddBatchServers_RemovedServer_StarIsNotMovedToManualServerWithSameName()
    {
        var config = CoreConfigTestFactory.CreateConfig();
        CoreConfigTestFactory.BindAppManagerConfig(config);
        SQLiteHelper.Instance.CreateTable<SubItem>();
        SQLiteHelper.Instance.CreateTable<ProfileItem>();
        var subId = $"sub-remap-manual-{Guid.NewGuid():N}";
        await SQLiteHelper.Instance.ReplaceAsync(new SubItem { Id = subId, Remarks = "sub", Url = "http://127.0.0.1/sub" });
        var manual = CoreConfigTestFactory.CreateVmessNode(ECoreType.Xray, $"manual-{Guid.NewGuid():N}", "node-b");
        manual.Subid = subId;
        manual.IsSub = false;
        await SQLiteHelper.Instance.InsertAsync(manual);

        const string a = "vless://00000000-0000-0000-0000-00000000000a@10.0.0.1:443?encryption=none&type=tcp#node-a";
        const string b = "vless://00000000-0000-0000-0000-00000000000b@10.0.0.2:443?encryption=none&type=tcp#node-b";

        var fav = Path.Combine(Path.GetTempPath(), $"glide-fav-{Guid.NewGuid():N}.json");
        var original = FavoriteStore.FilePath;
        FavoriteStore.FilePath = fav;
        FavoriteStore.ResetCache();
        try
        {
            await ConfigHandler.AddBatchServers(config, $"{a}\n{b}", subId, true);
            var subB = (await AppManager.Instance.ProfileItems(subId) ?? []).Single(p => p.Address == "10.0.0.2");
            FavoriteStore.Toggle(subB.IndexId);

            // The provider drops node-b.
            await ConfigHandler.AddBatchServers(config, a, subId, true);

            await FavoriteStore.IsFavorite(manual.IndexId).Should().BeFalse();
            await FavoriteStore.All().Count.Should().BeEqualTo(0);
        }
        finally
        {
            FavoriteStore.FilePath = original;
            FavoriteStore.ResetCache();
            File.Delete(fav);
        }
    }
}
