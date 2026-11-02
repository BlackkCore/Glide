using Avalonia.Data.Converters;

namespace v2rayN.Desktop.Converters;

public class DelayColorConverter : IValueConverter
{
    // Mid-tone colors that read well on both the light and the dark surfaces.
    private static readonly IBrush Fast = new SolidColorBrush(Color.Parse("#22A35A"));
    private static readonly IBrush Slow = new SolidColorBrush(Color.Parse("#D48A1C"));
    private static readonly IBrush Bad = new SolidColorBrush(Color.Parse("#E0464E"));
    private static readonly IBrush Neutral = new SolidColorBrush(Color.Parse("#8C92A0"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // Accepts the numeric Delay or the DelayVal text; non-numbers (e.g. "testing") stay neutral.
        if (!int.TryParse(value?.ToString(), out var delay))
        {
            return Neutral;
        }

        return delay switch
        {
            <= 0 => Bad,
            <= 200 => Fast,
            <= 500 => Slow,
            _ => Bad
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return null;
    }
}
