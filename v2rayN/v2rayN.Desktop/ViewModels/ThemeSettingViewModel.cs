using Avalonia.Controls.Notifications;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Markup.Xaml.Styling;
using AvaloniaEdit;
using Semi.Avalonia;

namespace v2rayN.Desktop.ViewModels;

public partial class ThemeSettingViewModel : MyReactiveObject
{
    [Reactive] public partial string CurrentTheme { get; set; }

    [Reactive] public partial int CurrentFontSize { get; set; }

    [Reactive] public partial string CurrentLanguage { get; set; }

    public ThemeSettingViewModel()
    {
        _config = AppManager.Instance.Config;

        BindingUI();
        RestoreUI();
    }

    private void RestoreUI()
    {
        ModifyTheme();
        ModifyFontFamily();
        ModifyFontSize();
    }

    private void BindingUI()
    {
        CurrentTheme = _config.UiItem.CurrentTheme;
        CurrentFontSize = _config.UiItem.CurrentFontSize;
        CurrentLanguage = _config.UiItem.CurrentLanguage;

        this.WhenAnyValue(x => x.CurrentTheme)
            .Where(y => y.IsNotEmpty())
            .SubscribeAsync(async _ =>
            {
                if (_config.UiItem.CurrentTheme != CurrentTheme)
                {
                    _config.UiItem.CurrentTheme = CurrentTheme;
                    ModifyTheme();
                    await ConfigHandler.SaveConfig(_config);
                }
            });

        this.WhenAnyValue(x => x.CurrentFontSize)
            .Where(y => y > 0)
            .SubscribeAsync(async _ =>
            {
                if (_config.UiItem.CurrentFontSize != CurrentFontSize && CurrentFontSize >= Global.MinFontSize)
                {
                    _config.UiItem.CurrentFontSize = CurrentFontSize;
                    ModifyFontSize();
                    await ConfigHandler.SaveConfig(_config);
                }
            });

        this.WhenAnyValue(x => x.CurrentLanguage)
            .Where(y => !y.IsNullOrEmpty())
            .SubscribeAsync(async _ =>
            {
                if (CurrentLanguage.IsNotEmpty() && _config.UiItem.CurrentLanguage != CurrentLanguage)
                {
                    _config.UiItem.CurrentLanguage = CurrentLanguage;
                    Thread.CurrentThread.CurrentUICulture = new(CurrentLanguage);
                    await ConfigHandler.SaveConfig(_config);
                    NoticeManager.Instance.Enqueue(ResUI.NeedRebootTips);
                }
            });
    }

    private void ModifyTheme()
    {
        var app = Application.Current;
        if (app is not null)
        {
            app.RequestedThemeVariant = CurrentTheme switch
            {
                nameof(ETheme.Dark) => ThemeVariant.Dark,
                nameof(ETheme.Light) => ThemeVariant.Light,
                nameof(ETheme.Aquatic) => SemiTheme.Aquatic,
                nameof(ETheme.Desert) => SemiTheme.Desert,
                nameof(ETheme.Dusk) => SemiTheme.Dusk,
                nameof(ETheme.NightSky) => SemiTheme.NightSky,
                _ => ThemeVariant.Default,
            };
        }
    }

    private const string UiFontSizeKey = "AppUiFontSize";
    private const string UiRowHeightKey = "AppUiRowHeight";
    private static bool _fontSizeStylesAdded;

    private void ModifyFontSize()
    {
        var app = Application.Current;
        if (app is null)
        {
            return;
        }
        // Runs first while the main window is being built. Adding styles to an open window
        // re-templates its controls, which crashed the log tab, so later changes only update
        // the resources these styles read.
        EnsureFontSizeStyles(app);

        double size = CurrentFontSize;
        if (size < Global.MinFontSize)
        {
            return;
        }
        app.Resources[UiFontSizeKey] = size;
        app.Resources[UiRowHeightKey] = 26 + size;

        // The design's type scale follows at half strength, so headings and labels keep their
        // proportions while still getting smaller or larger with the user's choice.
        var factor = 1 + ((size - 13) / 26);
        foreach (var (key, baseSize) in TypeScale)
        {
            app.Resources[key] = Math.Round(baseSize * factor * 2) / 2;
        }
    }

    private static readonly (string Key, double Size)[] TypeScale =
    [
        ("AppFontCaption", 11),
        ("AppFontSmall", 12),
        ("AppFontBody", 13),
        ("AppFontTitle", 15),
        ("AppFontHeading", 20),
    ];

    private static void EnsureFontSizeStyles(Application app)
    {
        if (_fontSizeStylesAdded)
        {
            return;
        }
        _fontSizeStylesAdded = true;

        Style style = new(x => Selectors.Or(
            x.OfType<Button>(),
            x.OfType<TextBox>(),
            x.OfType<TextBlock>(),
            x.OfType<SelectableTextBlock>(),
            x.OfType<Menu>(),
            x.OfType<ContextMenu>(),
            x.OfType<DataGridRow>(),
            x.OfType<ListBoxItem>(),
            x.OfType<HeaderedContentControl>(),
            x.OfType<TextEditor>()
        ));
        style.Add(new Setter(TemplatedControl.FontSizeProperty, new DynamicResourceExtension(UiFontSizeKey)));
        InsertBeforeAppStyles(app, style);

        //DataGrid (the server list keeps its own row height)
        var rowStyle = new Style(x => x.OfType<DataGrid>().Not(y => y.Class("servers")));
        rowStyle.Add(new Setter(DataGrid.RowHeightProperty, new DynamicResourceExtension(UiRowHeightKey)));
        app.Styles.Add(rowStyle);
    }

    // User font settings go before the app's own styles: plain text follows them,
    // while text the design styles explicitly (headings, labels, the server list) keeps its size.
    private static void InsertBeforeAppStyles(Application app, IStyle style)
    {
        var firstInclude = app.Styles.OfType<StyleInclude>().FirstOrDefault();
        if (firstInclude is null)
        {
            app.Styles.Add(style);
            return;
        }
        app.Styles.Insert(app.Styles.IndexOf(firstInclude), style);
    }

    private void ModifyFontFamily()
    {
        var currentFontFamily = _config.UiItem.CurrentFontFamily;
        if (currentFontFamily.IsNullOrEmpty())
        {
            return;
        }

        try
        {
            Style style = new(x => Selectors.Or(
                x.OfType<Button>(),
                x.OfType<TextBox>(),
                x.OfType<TextBlock>(),
                x.OfType<SelectableTextBlock>(),
                x.OfType<Menu>(),
                x.OfType<ContextMenu>(),
                x.OfType<DataGridRow>(),
                x.OfType<ListBoxItem>(),
                x.OfType<HeaderedContentControl>(),
                x.OfType<WindowNotificationManager>(),
                x.OfType<TextEditor>()
            ));
            style.Add(new Setter()
            {
                Property = TemplatedControl.FontFamilyProperty,
                Value = new FontFamily(currentFontFamily),
            });
            if (Application.Current is { } app)
            {
                InsertBeforeAppStyles(app, style);
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("ModifyFontFamily", ex);
        }
    }
}
