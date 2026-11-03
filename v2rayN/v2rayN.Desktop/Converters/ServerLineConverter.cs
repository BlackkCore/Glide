using Avalonia.Data.Converters;

namespace v2rayN.Desktop.Converters;

/// <summary>
/// Second line of a server in the list view: "VLESS · ws · reality · import_sub", leaving out
/// whatever is empty so no stray separators show.
/// </summary>
public class ServerLineConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not ProfileItemModel item)
        {
            return string.Empty;
        }
        var parts = new[] { item.ConfigType.ToString(), item.Network, item.StreamSecurity, item.SubRemarks }
            .Where(p => p.IsNotEmpty());
        return string.Join("  ·  ", parts);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return null;
    }
}
