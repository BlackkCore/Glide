using Avalonia.Data.Converters;

namespace v2rayN.Desktop.Converters;

/// <summary>
/// Display name of a routing preset. The three built-in presets ("V4-绕过大陆(Whitelist)" etc.) get a
/// plain, translated name ("Bypass China sites"); other names only lose their Chinese part.
/// Parameter "desc" returns a one-line explanation for the built-in presets instead (empty otherwise).
/// Display only; the stored name is unchanged.
/// </summary>
public class RoutingNameConverter : IValueConverter
{
    public static readonly RoutingNameConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var display = RoutingName.Display(value as string);
        var builtIn = display switch
        {
            "Whitelist" => (ResUI.TbRoutingWhitelist, ResUI.TipRoutingWhitelist),
            "Blacklist" => (ResUI.TbRoutingBlacklist, ResUI.TipRoutingBlacklist),
            "Global" => (ResUI.TbRoutingGlobal, ResUI.TipRoutingGlobal),
            _ => ((string?)null, (string?)null),
        };
        if (parameter as string == "desc")
        {
            return builtIn.Item2 ?? string.Empty;
        }
        return builtIn.Item1 ?? display;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return null;
    }
}
