using Avalonia.VisualTree;
using DialogHostAvalonia;
using HAlign = Avalonia.Layout.HorizontalAlignment;
using VAlign = Avalonia.Layout.VerticalAlignment;

namespace v2rayN.Desktop.Views;

/// <summary>
/// The logs, large: the main window's own log card (Activity, the core's log, and mihomo's tabs) is
/// lent to this sheet while it is open and handed back when it closes, so no message is missed and
/// what was already logged stays. Sized to most of the window, with the text a little larger.
/// </summary>
public sealed class LogsSheet : UserControl
{
    private readonly Border _host = new();

    public LogsSheet(double windowWidth, double windowHeight)
    {
        Width = Math.Clamp(windowWidth * 0.82, 560, 1180);
        Height = Math.Clamp(windowHeight * 0.82, 420, 900);

        var title = new TextBlock { Text = ResUI.TbLogs, VerticalAlignment = VAlign.Center };
        title.Classes.Add("h2");
        var close = new Button { Content = ResUI.TbClose, MinWidth = 96, HorizontalAlignment = HAlign.Right, IsCancel = true };
        close.Click += (_, _) => Close();

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        header.Children.Add(title);

        var root = new DockPanel { Margin = new Thickness(22, 18, 22, 20) };
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(_host);
        Content = root;

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
            }
        };
    }

    /// <summary>Shows <paramref name="logCard"/> (already detached from its old parent) until the sheet closes.</summary>
    public async Task ShowAsync(Control logCard)
    {
        _host.Child = logCard;
        // The log text one step larger here, where there is room to read it.
        foreach (var editor in logCard.GetVisualDescendants().OfType<AvaloniaEdit.TextEditor>())
        {
            editor.FontSize = 13;
        }
        try
        {
            await DialogHost.Show(this);
        }
        finally
        {
            _host.Child = null;
        }
    }

    private static void Close()
    {
        if (DialogHost.IsDialogOpen(null))
        {
            DialogHost.Close(null);
        }
    }
}
