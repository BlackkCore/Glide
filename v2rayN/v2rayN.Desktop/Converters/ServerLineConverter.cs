using Avalonia.Data.Converters;

namespace v2rayN.Desktop.Converters;

/// <summary>
/// Second line of a server in the list view: "VLESS · ws · reality · import_sub", leaving out
/// whatever is empty so no stray separators show.
/// </summary>
public class ServerLineConverter : IValueConverter
{
    /// <summary>The delay text while a test runs ("testing…", not a number yet).</summary>
    public static readonly FuncValueConverter<string?, bool> IsTesting =
        new(value => value.IsNotEmpty() && !int.TryParse(value, out _));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not ProfileItemModel item)
        {
            return string.Empty;
        }
        // The row shows what helps to choose (protocol and transport) so it fits without being cut;
        // the tooltip (parameter "full") adds the security layer and the subscription.
        var full = parameter as string == "full";
        var parts = (full
                ? new[] { TypeName(item.ConfigType.ToString(), item.Remarks), item.Network, item.StreamSecurity, item.SubRemarks }
                : new[] { TypeName(item.ConfigType.ToString(), item.Remarks), item.Network })
            .Where(p => p.IsNotEmpty());
        return string.Join(full ? "  ·  " : " · ", parts);
    }

    /// <summary>
    /// The protocol label users see: "Auto" for Glide's Auto group, plain words for the other groups
    /// ("PolicyGroup" -> "Policy Group"), the protocol name otherwise.
    /// </summary>
    public static string TypeName(string type, string? name)
    {
        return type switch
        {
            nameof(EConfigType.PolicyGroup) => name?.TrimStart().StartsWith(AutoServer.Prefix.Trim()) == true
                ? ResUI.TbAutoBadge
                : ResUI.TbConfigTypePolicyGroup,
            nameof(EConfigType.ProxyChain) => ResUI.TbConfigTypeProxyChain,
            _ => type,
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return null;
    }
}
