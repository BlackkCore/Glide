using Avalonia.Data.Converters;

namespace v2rayN.Desktop.Converters;

/// <summary>
/// A subscription's link -> just its host ("sub.example.com"), shown under the name in the
/// subscription picker so subscriptions with the same name can be told apart. No path, token or key.
/// </summary>
public static class SubHostConverter
{
    public static readonly FuncValueConverter<string?, string?> Host = new(url =>
    {
        var text = url?.Trim();
        if (text.IsNullOrEmpty())
        {
            return null;
        }
        // Several links may be given one per line; the first one names the provider.
        var first = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)[0].Trim();
        return Uri.TryCreate(first, UriKind.Absolute, out var uri) && uri.Host.IsNotEmpty() ? uri.Host : null;
    });
}
