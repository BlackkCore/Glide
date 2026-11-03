using Avalonia.Data.Converters;

namespace v2rayN.Desktop.Converters;

/// <summary>
/// Shows a routing preset's remarks without the Chinese part ("V4-绕过大陆(Whitelist)" -> "Whitelist").
/// Display only; the stored name is unchanged.
/// </summary>
public class RoutingNameConverter : IValueConverter
{
    public static readonly RoutingNameConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return RoutingName.Display(value as string);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return null;
    }
}
