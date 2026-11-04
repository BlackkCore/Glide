using Avalonia.Data.Converters;

namespace v2rayN.Desktop.Converters;

public class DelayColorConverter : IValueConverter
{
    // Mid-tone colors that read well on both the light and the dark surfaces:
    // cyan = fast (the "live" colour), amber = slow, red = failed or very slow.
    private static readonly IBrush Fast = new SolidColorBrush(Color.Parse("#0FA5C4"));
    private static readonly IBrush Slow = new SolidColorBrush(Color.Parse("#D9930F"));
    private static readonly IBrush Bad = new SolidColorBrush(Color.Parse("#E5484D"));
    private static readonly IBrush Neutral = new SolidColorBrush(Color.Parse("#7D8494"));

    /// <summary>0 = not a number (testing, empty), 1 = fast, 2 = slow, 3 = failed or very slow.</summary>
    public static int Level(object? value)
    {
        if (!int.TryParse(value?.ToString(), out var delay))
        {
            return 0;
        }
        return delay switch
        {
            <= 0 => 3,
            <= 200 => 1,
            <= 500 => 2,
            _ => 3
        };
    }

    // For the tinted delay pill in the list view (Classes.fast / .slow / .bad).
    public static readonly FuncValueConverter<object?, bool> IsFast = new(v => Level(v) == 1);
    public static readonly FuncValueConverter<object?, bool> IsSlow = new(v => Level(v) == 2);
    public static readonly FuncValueConverter<object?, bool> IsBad = new(v => Level(v) == 3);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return Level(value) switch
        {
            1 => Fast,
            2 => Slow,
            3 => Bad,
            _ => Neutral,
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return null;
    }
}
