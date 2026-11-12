using DialogHostAvalonia;
using ServiceLib.Services;
using v2rayN.Desktop.Common;
using HAlign = Avalonia.Layout.HorizontalAlignment;
using VAlign = Avalonia.Layout.VerticalAlignment;

namespace v2rayN.Desktop.Views;

/// <summary>
/// "Allow Store apps to use the proxy": lists the installed Windows Store apps and lets the user choose which may
/// reach this computer's loopback address (where Glide's proxy listens). Windows' own CheckNetIsolation makes the
/// change, after one administrator prompt; see LoopbackExempt.
/// </summary>
public sealed class LoopbackSheet : UserControl
{
    private readonly List<(CheckBox Box, LoopbackApp App)> _rows = [];
    private readonly StackPanel _list = new() { Spacing = 2 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _apply = new() { Content = ResUI.TbLoopbackApply, MinWidth = 110, IsEnabled = false };
    private readonly Button _all = new() { Content = ResUI.TbLoopbackSelectAll, IsEnabled = false };
    private readonly Button _none = new() { Content = ResUI.TbLoopbackSelectNone, IsEnabled = false };
    private readonly TextBox _search = new() { PlaceholderText = ResUI.TbLoopbackSearch, IsEnabled = false };

    public LoopbackSheet()
    {
        Width = 520;
        var panel = new StackPanel { Margin = new Thickness(24, 22), Spacing = 10 };

        var title = new TextBlock { Text = ResUI.TbSettingsSetUWP, TextWrapping = TextWrapping.Wrap };
        title.Classes.Add("h2");
        panel.Children.Add(title);
        panel.Children.Add(Muted(ResUI.TipLoopbackIntro));
        panel.Children.Add(_search);

        panel.Children.Add(new ScrollViewer
        {
            MaxHeight = 300,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = _list,
        });
        _status.Classes.Add("muted");
        _status.Text = ResUI.TipLoopbackLoading;
        panel.Children.Add(_status);
        panel.Children.Add(Muted(ResUI.TipLoopbackAdmin));

        var buttons = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 4, 0, 0) };
        var left = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8 };
        left.Children.Add(_all);
        left.Children.Add(_none);
        DockPanel.SetDock(left, Dock.Left);
        buttons.Children.Add(left);
        var right = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8 };
        _apply.Classes.Add("Primary");
        _apply.Bind(Button.ThemeProperty, _apply.GetResourceObservable("SolidButton"));
        right.Children.Add(_apply);
        var close = new Button { Content = ResUI.TbClose, MinWidth = 96, IsCancel = true };
        close.Click += (_, _) => Close();
        right.Children.Add(close);
        DockPanel.SetDock(right, Dock.Right);
        buttons.Children.Add(right);
        panel.Children.Add(buttons);
        Content = panel;

        _all.Click += (_, _) => SetAll(true);
        _none.Click += (_, _) => SetAll(false);
        _apply.Click += async (_, _) => await ApplyAsync();
        _search.TextChanged += (_, _) => Filter();
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        List<LoopbackApp> apps;
        try
        {
            apps = await Task.Run(LoopbackExempt.ListApps);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(LoopbackSheet), ex);
            apps = [];
        }
        Populate(apps);
    }

    private void Populate(List<LoopbackApp> apps)
    {
        _rows.Clear();
        _list.Children.Clear();
        foreach (var app in apps)
        {
            var name = new TextBlock { Text = app.DisplayName, TextWrapping = TextWrapping.Wrap };
            var family = new TextBlock { Text = app.FamilyName, FlowDirection = FlowDirection.LeftToRight, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
            family.Classes.Add("muted");
            var box = new CheckBox { IsChecked = app.Exempt, Content = new StackPanel { Children = { name, family } } };
            Avalonia.Automation.AutomationProperties.SetName(box, app.DisplayName);
            _rows.Add((box, app));
            _list.Children.Add(box);
        }
        var any = _rows.Count > 0;
        _all.IsEnabled = _none.IsEnabled = _apply.IsEnabled = _search.IsEnabled = any;
        _status.Text = any ? string.Empty : ResUI.TipLoopbackNone;
    }

    private void SetAll(bool value)
    {
        foreach (var (box, _) in _rows.Where(r => r.Box.IsVisible))
        {
            box.IsChecked = value;
        }
    }

    private void Filter()
    {
        var q = _search.Text?.Trim() ?? string.Empty;
        foreach (var (box, app) in _rows)
        {
            box.IsVisible = q.Length == 0
                || app.DisplayName.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                || app.FamilyName.Contains(q, StringComparison.OrdinalIgnoreCase);
        }
    }

    private async Task ApplyAsync()
    {
        var add = _rows.Where(r => r.Box.IsChecked == true && !r.App.Exempt).Select(r => r.App.FamilyName).ToList();
        var remove = _rows.Where(r => r.Box.IsChecked != true && r.App.Exempt).Select(r => r.App.FamilyName).ToList();
        if (add.Count == 0 && remove.Count == 0)
        {
            _status.Text = ResUI.TipLoopbackNoChanges;
            return;
        }
        _apply.IsEnabled = _all.IsEnabled = _none.IsEnabled = false;
        try
        {
            var applied = OperatingSystem.IsWindows() ? await LoopbackExempt.ApplyAsync(add, remove) : 0;
            Populate(await Task.Run(LoopbackExempt.ListApps));
            _status.Text = applied > 0 ? string.Format(ResUI.TipLoopbackDone, applied) : ResUI.TipLoopbackFailed;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(LoopbackSheet), ex);
            _status.Text = ResUI.TipLoopbackFailed;
            _apply.IsEnabled = _all.IsEnabled = _none.IsEnabled = true;
        }
    }

    /// <summary>Shows the sheet (nothing happens if another sheet is open).</summary>
    public static void Open()
    {
        if (DialogHost.IsDialogOpen(null))
        {
            return;
        }
        var sheet = new LoopbackSheet();
        _ = DialogHost.Show(sheet);
        AppMotion.PlayEntrance(sheet, 6, AppMotion.RevealMs);
    }

    private static TextBlock Muted(string text)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        block.Classes.Add("muted");
        return block;
    }

    private static void Close()
    {
        if (DialogHost.IsDialogOpen(null))
        {
            DialogHost.Close(null);
        }
    }
}
