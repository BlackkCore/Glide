namespace ServiceLib.Tests.CoreConfig.Singbox;

/// <summary>
/// The 15 sing-box rule sets (bin\srss) are no longer shipped (no licence). Glide must
/// still produce a working config when none is on disk: sing-box downloads each rule set it needs itself.
/// </summary>
public class MissingRuleFileTests
{
    private static SingboxConfig Generate(string geoDomain)
    {
        var config = CoreConfigTestFactory.CreateConfig(ECoreType.sing_box);
        CoreConfigTestFactory.BindAppManagerConfig(config);
        var node = CoreConfigTestFactory.CreateSocksNode(ECoreType.sing_box, "n-main", "main");
        var context = CoreConfigTestFactory.CreateContext(config, node, ECoreType.sing_box) with
        {
            RoutingItem = new RoutingItem
            {
                Id = "r-missing-srs",
                Remarks = "missing-srs",
                RuleSet = JsonUtils.Serialize(new List<RulesItem>
                {
                    new() { Enabled = true, RuleType = ERuleType.Routing, OutboundTag = Global.ProxyTag, Domain = [geoDomain] },
                }),
                DomainStrategy = Global.AsIs,
                DomainStrategy4Singbox = string.Empty,
            },
        };
        var result = new CoreConfigSingboxService(context).GenerateClientConfigContent();
        if (!result.Success)
        {
            throw new InvalidOperationException(result.Msg);
        }
        return JsonUtils.Deserialize<SingboxConfig>(result.Data!.ToString())!;
    }

    [Test]
    [Arguments("geosite:google", "geosite-google")]
    [Arguments("geosite:category-ads-all", "geosite-category-ads-all")]
    public async Task NoLocalRuleFile_BecomesARemoteRuleSet_DownloadedThroughTheProxyClient(string domain, string tag)
    {
        var cfg = Generate(domain);

        var set = cfg.route.rule_set.FirstOrDefault(r => r.tag == tag);
        await set.Should().NotBeNull();
        await set!.type.Should().BeEqualTo("remote");
        await set.format.Should().BeEqualTo("binary");
        // the default source (a repository with a GPL-3.0 licence file): geo/<kind>/<name>.srs on the "sing" branch
        await set.url.Should().BeEqualTo($"https://raw.githubusercontent.com/MetaCubeX/meta-rules-dat/sing/geo/geosite/{tag["geosite-".Length..]}.srs");
        await set.http_client.Should().BeEqualTo(Global.SingboxSrsDownloadHttpClientTag);
        await (cfg.http_clients ?? []).Any(c => c.tag == Global.SingboxSrsDownloadHttpClientTag).Should().BeTrue();
    }

    [Test]
    public async Task NoLocalRuleFile_NeverProducesALocalRuleSetPointingAtAMissingFile()
    {
        var cfg = Generate("geosite:google");

        await cfg.route.rule_set.Any(r => r.type == "local").Should().BeFalse();
    }

    // The 15 rule sets Glide's presets use (the files that used to ship in binsrss).
    private static readonly string[] PresetRuleSets =
    [
        "geoip-cn", "geoip-facebook", "geoip-fastly", "geoip-google", "geoip-netflix", "geoip-private", "geoip-telegram", "geoip-twitter",
        "geosite-category-ads-all", "geosite-cn", "geosite-geolocation-cn", "geosite-gfw", "geosite-google", "geosite-greatfire", "geosite-private",
    ];

    [Test]
    public async Task DefaultSource_MapsEveryPresetRuleSetToItsFileOnTheSingBranch()
    {
        foreach (var name in PresetRuleSets)
        {
            var kind = name.StartsWith("geoip-") ? "geoip" : "geosite";
            var bare = name[(kind.Length + 1)..];
            var url = string.Format(Global.SingboxRulesetUrl, kind, name, bare);
            await url.Should().BeEqualTo($"https://raw.githubusercontent.com/MetaCubeX/meta-rules-dat/sing/geo/{kind}/{bare}.srs");
        }
    }

    [Test]
    public async Task ATemplateTheUserSavedEarlier_WithOnlyTwoPlaceholders_StillWorks()
    {
        var legacy = "https://example.invalid/rule-set-{0}/{1}.srs";
        await string.Format(legacy, "geosite", "geosite-cn", "cn").Should().BeEqualTo("https://example.invalid/rule-set-geosite/geosite-cn.srs");
    }
}
