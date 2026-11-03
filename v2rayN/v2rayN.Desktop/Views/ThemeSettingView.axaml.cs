using Avalonia.Data;
using v2rayN.Desktop.ViewModels;

namespace v2rayN.Desktop.Views;

/// <summary>
/// ThemeSettingView.xaml
/// </summary>
public partial class ThemeSettingView : ReactiveUserControl<ThemeSettingViewModel>
{
    private bool _syncingTheme;

    /// <summary>
    /// Raised when the user picks a theme in the popup. The main window applies it with its
    /// circular reveal; without a listener the theme is applied straight away.
    /// </summary>
    public event Action<ETheme, Control>? ThemeRequested;

    public ThemeSettingView()
    {
        InitializeComponent();
        ViewModel = new ThemeSettingViewModel();

        cmbCurrentFontSize.ItemsSource = Enumerable.Range(Global.MinFontSize, Global.MinFontSizeCount).ToList();
        cmbCurrentLanguage.ItemsSource = Global.LanguageOptions;
        cmbCurrentLanguage.DisplayMemberBinding = new Binding(nameof(LanguageOption.Display));
        cmbCurrentLanguage.SelectedValueBinding = new Binding(nameof(LanguageOption.Value));
        lstTheme.SelectionChanged += LstTheme_SelectionChanged;

        this.WhenActivated(disposables =>
        {
            this.WhenAnyValue(v => v.ViewModel.CurrentTheme)
                .Subscribe(SelectThemeItem)
                .DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.CurrentFontSize, v => v.cmbCurrentFontSize.SelectedValue).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.CurrentLanguage, v => v.cmbCurrentLanguage.SelectedValue).DisposeWith(disposables);
        });
    }

    // Older configs may still name one of Semi's extra themes; those show as "System".
    private void SelectThemeItem(string? theme)
    {
        _syncingTheme = true;
        lstTheme.SelectedItem = lstTheme.Items.OfType<ListBoxItem>().FirstOrDefault(i => (string?)i.Tag == theme)
                                ?? lstTheme.Items.OfType<ListBoxItem>().First();
        _syncingTheme = false;
    }

    private void LstTheme_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingTheme || ViewModel is null || lstTheme.SelectedItem is not ListBoxItem { Tag: string tag } item
            || tag == ViewModel.CurrentTheme || !Enum.TryParse<ETheme>(tag, out var theme))
        {
            return;
        }
        if (ThemeRequested is { } handler)
        {
            handler(theme, item);
        }
        else
        {
            ViewModel.CurrentTheme = tag;
        }
    }
}
