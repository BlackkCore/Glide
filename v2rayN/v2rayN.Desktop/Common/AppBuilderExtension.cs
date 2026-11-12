namespace v2rayN.Desktop.Common;

public static class AppBuilderExtension
{
    private static readonly string DefaultFontFamilyName =
        Path.Combine(Global.AvaAssets, "Fonts#Manrope");

    private static readonly string CjkFontFamilyName =
        Path.Combine(Global.AvaAssets, "Fonts#Noto Sans SC");

    private static readonly string PersianFontFamilyName =
        Path.Combine(Global.AvaAssets, "Fonts#Vazirmatn");

    // In the Persian UI Vazirmatn is the main font, so digits, punctuation and Latin words inside
    // Persian text are drawn in the same family as the letters around them. Changing the language
    // already asks for a restart, so choosing the font once at startup is enough.
    private static bool IsPersianUi()
    {
        try
        {
            return AppManager.Instance.Config?.UiItem?.CurrentLanguage?
                .StartsWith("fa", StringComparison.OrdinalIgnoreCase) ?? false;
        }
        catch
        {
            return false;
        }
    }

    public static AppBuilder WithFontByDefault(this AppBuilder appBuilder)
    {
        var fallbacks = new List<FontFallback>();

        // Other languages keep Manrope, which has no Persian/Arabic letters; Vazirmatn draws them in a matching style.
        var persian = IsPersianUi();
        fallbacks.Add(new FontFallback
        {
            FontFamily = new FontFamily(persian ? DefaultFontFamilyName : PersianFontFamilyName)
        });

        var notoSansSc = new FontFamily(CjkFontFamilyName);

        fallbacks.Add(new FontFallback
        {
            FontFamily = notoSansSc
        });

        if (OperatingSystem.IsWindows())
        {
            AddFontFallback(fallbacks, "Segoe UI Emoji");
            AddFontFallback(fallbacks, "Segoe UI Symbol");
        }
        else if (OperatingSystem.IsMacOS())
        {
            AddFontFallback(fallbacks, "Apple Color Emoji");
            AddFontFallback(fallbacks, "Apple Symbols");
        }
        else if (OperatingSystem.IsLinux())
        {
            AddFontFallback(fallbacks, "Noto Color Emoji");
            AddFontFallback(fallbacks, "Noto Sans Symbols");
        }

        return appBuilder.With(new FontManagerOptions
        {
            DefaultFamilyName = persian ? PersianFontFamilyName : DefaultFontFamilyName,
            FontFallbacks = fallbacks.ToArray()
        });
    }

    private static void AddFontFallback(List<FontFallback> fallbacks, string fontFamilyName)
    {
        fallbacks.Add(new FontFallback
        {
            FontFamily = new FontFamily(fontFamilyName)
        });
    }
}
