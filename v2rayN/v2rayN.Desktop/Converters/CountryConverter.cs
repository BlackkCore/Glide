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
        var remarks = value as string;
        return (parameter as string) switch
        {
            "has" => CountryDetector.Detect(remarks) != null,
            "text" => CountryDetector.RemoveFlags(remarks),
            _ => CountryDetector.Detect(remarks),
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return null;
    }
}
