namespace ServiceLib.Common;

/// <summary>
/// "Auto (fastest)" for a subscription: upstream's all-servers policy group (Xray's leastPing
/// balancer keeps measuring every server and uses the fastest one that works), made once per
/// subscription and named so Glide can find it again. Not a subscription item (IsSub = false),
/// so updating the subscription keeps it.
/// </summary>
public static class AutoServer
{
    public const string Prefix = "⚡ ";

    public static bool IsAuto(ProfileItem? item)
    {
        return item?.ConfigType == EConfigType.PolicyGroup && item.Remarks?.StartsWith(Prefix) == true;
    }

    /// <summary>The subscription's Auto server, made now if it doesn't exist yet; null on failure.</summary>
    public static async Task<string?> EnsureAsync(Config config, SubItem? subscription)
    {
        if (subscription == null || subscription.Id.IsNullOrEmpty())
        {
            return null;
        }
        var existing = (await AppManager.Instance.ProfileItems(subscription.Id))?.FirstOrDefault(IsAuto);
        if (existing != null)
        {
            return existing.IndexId;
        }

        var created = await ConfigHandler.AddGroupAllServer(config, subscription);
        var indexId = created.Success ? created.Data?.ToString() : null;
        var profile = indexId.IsNullOrEmpty() ? null : await AppManager.Instance.GetProfileItem(indexId);
        if (profile == null)
        {
            return null;
        }
        profile.Remarks = $"{Prefix}{ResUI.TbAutoFastest}";
        await ConfigHandler.AddServerCommon(config, profile, true);
        return indexId;
    }
}
