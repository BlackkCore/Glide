using Avalonia.Data.Converters;
using v2rayN.Desktop.Common;

namespace v2rayN.Desktop.Converters;

public class DelayTextConverter : IValueConverter
{
    // "-1" -> "Timeout" (translated); numbers stay numbers (the column header already says "ms"), in
    // Persian digits in the Persian interface; any other text (test status) is shown as is.
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value?.ToString() ?? string.Empty;
        if (int.TryParse(text, out var delay))
        {
            return delay < 0 ? ResUI.TbDelayTimeout : UiDigits.Localize(text);
        }
        return text;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return null;
    }
}
