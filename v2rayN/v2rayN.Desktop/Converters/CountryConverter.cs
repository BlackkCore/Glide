using Avalonia.Data.Converters;

namespace v2rayN.Desktop.Converters;

/// <summary>
/// For the country chip in the server list, from a server's remarks:
/// no parameter -> the country code ("DE"), "has" -> whether one was found,
/// "text" -> the remarks without a leading flag emoji (Windows draws flags as plain letters).
/// </summary>
public class CountryConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // A whole server row: its name first, else its address (literal IPs only, looked up offline).
        var item = value as ProfileItemModel;
        var remarks = item?.Remarks ?? value as string;
        var code = CountryDetector.Detect(remarks) ?? (item != null ? GeoIpCountry.Lookup(item.Address) : null);
        return (parameter as string) switch
        {
            "has" => code != null,
            "text" => ServerNameDisplay.Clean(remarks),
            _ => code,
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return null;
    }
}
