using Avalonia.Data.Converters;
using v2rayN.Desktop.Common;

namespace v2rayN.Desktop.Converters;

/// <summary>
/// A server's remarks -> its round flag (null when the country is unknown or has no drawn flag).
/// Parameter "has" -> whether there is a flag; "chip" -> whether to fall back to the code chip
/// (a country was found but has no drawn flag).
/// </summary>
public class FlagConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // A whole server row: its name first, else its address (literal IPs only, looked up offline).
        var code = value is ProfileItemModel item
            ? CountryDetector.Detect(item.Remarks) ?? GeoIpCountry.Lookup(item.Address)
            : CountryDetector.Detect(value as string);
        var flag = FlagImages.Get(code);
        return (parameter as string) switch
        {
            "has" => flag != null,
            "chip" => code != null && flag == null,
            _ => flag,
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return null;
    }
}
