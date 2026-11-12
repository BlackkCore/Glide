using Avalonia.Data.Converters;

namespace v2rayN.Desktop.Converters;

public static class DelayColorConverter
{
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

    // The view applies its theme brushes to each latency class.
    public static readonly FuncValueConverter<object?, bool> IsFast = new(v => Level(v) == 1);
    public static readonly FuncValueConverter<object?, bool> IsSlow = new(v => Level(v) == 2);
    public static readonly FuncValueConverter<object?, bool> IsBad = new(v => Level(v) == 3);
}
