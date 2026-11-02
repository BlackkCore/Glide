using Avalonia.Data.Converters;

namespace v2rayN.Desktop.Converters;

public class DelayTextConverter : IValueConverter
{
    // "-1" -> "Timeout"; numbers stay as they are (the column header already says "ms"),
    // and any other text (test status) is shown as is.
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value?.ToString() ?? string.Empty;
        return int.TryParse(text, out var delay) && delay < 0 ? "Timeout" : text;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return null;
    }
}
