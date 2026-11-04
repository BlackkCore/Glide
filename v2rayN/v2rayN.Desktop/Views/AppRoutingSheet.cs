using System.Diagnostics;
using DialogHostAvalonia;
using HAlign = Avalonia.Layout.HorizontalAlignment;
using Orientation = Avalonia.Layout.Orientation;
using VAlign = Avalonia.Layout.VerticalAlignment;

namespace v2rayN.Desktop.Views;

/// <summary>
/// "Choose apps for routing": pick running apps and send them through the proxy or direct, instead of
/// typing process rules. Writes two rules at the top of the current routing ("Glide: apps …").
/// Process rules are applied by sing-box, which Glide uses for "Capture all apps (TUN)".
/// </summary>
public sealed class AppRoutingSheet : UserControl
{
    private const string ProxyRemarks = "Glide: apps through proxy";
    private const string DirectRemarks = "Glide: apps direct";

    // Windows' own processes: never what people mean by "apps", and routing them only causes trouble.
    private static readonly HashSet<string> SystemProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "svchost", "dwm", "TextInputHost", "ApplicationFrameHost", "ShellExperienceHost",
        "StartMenuExperienceHost", "SearchHost", "SearchApp", "SystemSettings", "LockApp", "RuntimeBroker",
        "ShellHost", "Widgets", "WidgetService", "ctfmon", "sihost", "taskhostw", "CrossDeviceResume",
    };

    private sealed record AppEntry(string Exe, string Name, CheckBox Box);

    private readonly MainWindowViewModel _main;
    private readonly List<AppEntry> _entries = [];
    private readonly StackPanel _list = new() { Spacing = 2 };
    private readonly TextBox _search = new() { Watermark = ResUI.TbSearchApps };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };

    public AppRoutingSheet(MainWindowViewModel main)
    {
        _main = main;
        Width = 540;
        var panel = new StackPanel { Margin = new Thickness(24, 22), Spacing = 10 };
        var title = new TextBlock { Text = ResUI.TbAppRouting };
        title.Classes.Add("h2");
        panel.Children.Add(title);
        panel.Children.Add(Muted(ResUI.TipAppRouting));
        var tunNote = Muted(ResUI.TipAppRoutingTun);
        // Red while TUN is off: the rules are saved but nothing applies them yet.
        if (!AppManager.Instance.Config.TunModeItem.EnableTun)
        {
            tunNote.Classes.Remove("muted");
            tunNote.Bind(TextBlock.ForegroundProperty, tunNote.GetResourceObservable("AppBadFgBrush"));
            tunNote.FontWeight = FontWeight.SemiBold;
        }
        panel.Children.Add(tunNote);

        // Search, then a list of real checkboxes, then a way to add an app that isn't running.
        _search.TextChanged += (_, _) => Filter();
        panel.Children.Add(_search);
        var listBorder = new Border
        {
            Child = new ScrollViewer { Content = _list, MaxHeight = 280 },
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 6),
        };
        listBorder.Bind(Border.BorderBrushProperty, listBorder.GetResourceObservable("AppBorderBrush"));
        panel.Children.Add(listBorder);
        var browse = MakeButton(ResUI.TbBrowseForApp, BrowseAsync);
        browse.HorizontalAlignment = HAlign.Left;
        panel.Children.Add(browse);

        _status.Classes.Add("muted");
        panel.Children.Add(_status);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HAlign.Right };
        buttons.Children.Add(MakeButton(ResUI.TbThroughProxy, () => ApplyAsync(Global.ProxyTag, ProxyRemarks, DirectRemarks)));
        buttons.Children.Add(MakeButton(ResUI.TbDirect, () => ApplyAsync(Global.DirectTag, DirectRemarks, ProxyRemarks)));
        buttons.Children.Add(MakeButton(ResUI.TbRemoveAppRules, RemoveAsync));
        buttons.Children.Add(MakeButton(ResUI.TbClose, () => { Close(); return Task.CompletedTask; }));
        panel.Children.Add(buttons);
        Content = panel;
    }

    public async Task LoadAsync()
    {
        // Apps with a window, one entry per program, by program name (never window titles, which can
        // be private, like a browser tab). Glide, its engines and Windows' own processes left out.
        var self = Process.GetCurrentProcess().ProcessName;
        var running = Process.GetProcesses()
            .Where(p => { try { return p.MainWindowHandle != IntPtr.Zero && p.ProcessName != self && !SystemProcesses.Contains(p.ProcessName); } catch { return false; } })
            .GroupBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase)
            .Select(g => ($"{g.Key}.exe", ProgramName(g.First())))
            .ToList();
        // Apps that already have a rule stay in the list even when they aren't running.
        var routing = await ConfigHandler.GetDefaultRouting(AppManager.Instance.Config);
        var rules = JsonUtils.Deserialize<List<RulesItem>>(routing.RuleSet) ?? [];
        var ruled = rules.Where(r => r.Remarks is ProxyRemarks or DirectRemarks).SelectMany(r => r.Process ?? []);
        foreach (var exe in ruled.Where(e => running.All(r => !r.Item1.Equals(e, StringComparison.OrdinalIgnoreCase))))
        {
            running.Add((exe, Path.GetFileNameWithoutExtension(exe)));
        }
        _entries.Clear();
        foreach (var (exe, name) in running.OrderBy(r => r.Item2, StringComparer.CurrentCultureIgnoreCase))
        {
            AddEntry(exe, name, false);
        }
        await ShowStatusAsync();
    }

    private void AddEntry(string exe, string name, bool isChecked)
    {
        if (_entries.FirstOrDefault(e => e.Exe.Equals(exe, StringComparison.OrdinalIgnoreCase)) is { } existing)
        {
            existing.Box.IsChecked = isChecked || existing.Box.IsChecked == true;
            return;
        }
        var label = new StackPanel { Spacing = 0 };
        label.Children.Add(new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis });
        var exeLine = new TextBlock { Text = exe, FontSize = 11 };
        exeLine.Classes.Add("faint");
        label.Children.Add(exeLine);
        var box = new CheckBox { Content = label, IsChecked = isChecked, Margin = new Thickness(2, 1) };
        _entries.Add(new AppEntry(exe, name, box));
        _list.Children.Add(box);
        Filter();
    }

    private void Filter()
    {
        var term = _search.Text?.Trim() ?? "";
        foreach (var entry in _entries)
        {
            entry.Box.IsVisible = term.Length == 0
                || entry.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase)
                || entry.Exe.Contains(term, StringComparison.OrdinalIgnoreCase);
        }
    }

    // "Browse for app…": pick any program, running or not.
    private async Task BrowseAsync()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top == null)
        {
            return;
        }
        var files = await top.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = [new Avalonia.Platform.Storage.FilePickerFileType(ResUI.TbPrograms) { Patterns = ["*.exe"] }],
        });
        var path = files.FirstOrDefault()?.Path is { IsFile: true } uri ? uri.LocalPath : null;
        if (path.IsNullOrEmpty())
        {
            return;
        }
        string name;
        try
        {
            name = FileVersionInfo.GetVersionInfo(path).FileDescription is { Length: > 0 } d ? d : Path.GetFileNameWithoutExtension(path);
        }
        catch
        {
            name = Path.GetFileNameWithoutExtension(path);
        }
        AddEntry(Path.GetFileName(path), name, true);
    }

    private async Task ApplyAsync(string outboundTag, string remarks, string otherRemarks)
    {
        var picked = _entries.Where(e => e.Box.IsChecked == true).Select(e => e.Exe).ToList();
        if (picked.Count == 0)
        {
            return;
        }
        await EditRulesAsync(rules =>
        {
            var target = rules.FirstOrDefault(r => r.Remarks == remarks);
            if (target == null)
            {
                target = new RulesItem { Id = Utils.GetGuid(false), OutboundTag = outboundTag, Remarks = remarks, Process = [], Enabled = true };
                rules.Insert(0, target);
            }
            target.Process = (target.Process ?? []).Union(picked, StringComparer.OrdinalIgnoreCase).ToList();
            // An app goes one way only.
            if (rules.FirstOrDefault(r => r.Remarks == otherRemarks) is { Process: { } other } otherRule)
            {
                other.RemoveAll(p => picked.Contains(p, StringComparer.OrdinalIgnoreCase));
                if (other.Count == 0)
                {
                    rules.Remove(otherRule);
                }
            }
        });
        foreach (var entry in _entries)
        {
            entry.Box.IsChecked = false;
        }
    }

    private Task RemoveAsync()
    {
        return EditRulesAsync(rules => rules.RemoveAll(r => r.Remarks is ProxyRemarks or DirectRemarks));
    }

    private async Task EditRulesAsync(Action<List<RulesItem>> edit)
    {
        try
        {
            var config = AppManager.Instance.Config;
            var routing = await ConfigHandler.GetDefaultRouting(config);
            var rules = JsonUtils.Deserialize<List<RulesItem>>(routing.RuleSet) ?? [];
            edit(rules);
            routing.RuleSet = JsonUtils.Serialize(rules, false);
            routing.RuleNum = rules.Count;
            await ConfigHandler.SaveRoutingItem(config, routing);
            // Apply at once.
            _main.Reload();
            await ShowStatusAsync();
        }
        catch (Exception ex)
        {
            Logging.SaveLog("AppRouting", ex);
            NoticeManager.Instance.Enqueue(ResUI.OperationFailed);
        }
    }

    private async Task ShowStatusAsync()
    {
        var routing = await ConfigHandler.GetDefaultRouting(AppManager.Instance.Config);
        var rules = JsonUtils.Deserialize<List<RulesItem>>(routing.RuleSet) ?? [];
        string List(string remarks)
        {
            var apps = rules.FirstOrDefault(r => r.Remarks == remarks)?.Process;
            return apps is { Count: > 0 } ? string.Join(", ", apps) : ResUI.TbNone;
        }
        _status.Text = string.Format(ResUI.TipAppRulesNow, List(ProxyRemarks), List(DirectRemarks));
    }

    // The program's own name ("Telegram Desktop", "Google Chrome") from its file, else the process name.
    private static string ProgramName(Process process)
    {
        try
        {
            var description = process.MainModule?.FileVersionInfo.FileDescription;
            return description is { Length: > 0 } ? description : process.ProcessName;
        }
        catch
        {
            return process.ProcessName;
        }
    }

    private static TextBlock Muted(string text)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        block.Classes.Add("muted");
        return block;
    }

    private static Button MakeButton(string text, Func<Task> action)
    {
        var button = new Button { Content = text, MinWidth = 90 };
        button.Click += async (_, _) =>
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                // An exception escaping a click handler would close the app.
                Logging.SaveLog("AppRouting", ex);
            }
        };
        return button;
    }

    private static void Close()
    {
        if (DialogHost.IsDialogOpen(null))
        {
            DialogHost.Close(null);
        }
    }
}
