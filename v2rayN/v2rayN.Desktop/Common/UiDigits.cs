namespace v2rayN.Desktop.Common;

/// <summary>
/// Numbers people read (delays, timers, speeds) in the interface's own digits: Persian digits (۰–۹) in the
/// Persian interface, ASCII otherwise. Addresses, ports and IPs are never passed through this.
/// </summary>
public static class UiDigits
{
    public static string Localize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }
        return CountryNames.PersianUi ? OriginLocationService.ToPersianDigits(text) : text;
    }

    /// <summary>For bindings: the delay pill in the server list ("235" becomes "۲۳۵" in Persian).</summary>
    public static readonly Avalonia.Data.Converters.FuncValueConverter<string?, string> Converter = new(Localize);
}
