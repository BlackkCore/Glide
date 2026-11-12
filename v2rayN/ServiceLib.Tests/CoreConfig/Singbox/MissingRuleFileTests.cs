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
        await (set.url ?? "").Contains(tag).Should().BeTrue();
        await set.http_client.Should().BeEqualTo(Global.SingboxSrsDownloadHttpClientTag);
        await (cfg.http_clients ?? []).Any(c => c.tag == Global.SingboxSrsDownloadHttpClientTag).Should().BeTrue();
    }

    [Test]
    public async Task NoLocalRuleFile_NeverProducesALocalRuleSetPointingAtAMissingFile()
    {
        var cfg = Generate("geosite:google");

        await cfg.route.rule_set.Any(r => r.type == "local").Should().BeFalse();
    }
}
