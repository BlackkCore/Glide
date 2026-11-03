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

    private readonly MainWindowViewModel _main;
    private readonly ListBox _apps = new() { SelectionMode = SelectionMode.Multiple | SelectionMode.Toggle, MaxHeight = 300 };
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
        panel.Children.Add(Muted(ResUI.TipAppRoutingTun));
        panel.Children.Add(_apps);
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
        // Apps with a window, one entry per program, Glide and its engines left out.
        var self = Process.GetCurrentProcess().ProcessName;
        _apps.ItemsSource = Process.GetProcesses()
            .Where(p => { try { return p.MainWindowHandle != IntPtr.Zero && p.ProcessName != self; } catch { return false; } })
            .GroupBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ListBoxItem
            {
                Tag = $"{g.Key}.exe",
                Content = $"{Shorten(SafeTitle(g.First()))}  ({g.Key}.exe)",
            })
            .OrderBy(i => i.Content?.ToString(), StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        await ShowStatusAsync();
    }

    private async Task ApplyAsync(string outboundTag, string remarks, string otherRemarks)
    {
        var picked = _apps.SelectedItems?.OfType<ListBoxItem>().Select(i => (string)i.Tag!).ToList() ?? [];
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
        _apps.SelectedItems?.Clear();
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

    private static string SafeTitle(Process process)
    {
        try
        {
            return process.MainWindowTitle.IsNotEmpty() ? process.MainWindowTitle : process.ProcessName;
        }
        catch
        {
            return process.ProcessName;
        }
    }

    private static string Shorten(string text)
    {
        return text.Length > 40 ? $"{text[..39]}…" : text;
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
