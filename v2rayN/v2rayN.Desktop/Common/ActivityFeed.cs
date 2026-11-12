using System.Collections.ObjectModel;

namespace v2rayN.Desktop.Common;

/// <summary>
/// "Activity": the plain-language notices Glide shows (connected, server switched, servers added,
/// updates…), newest first, so the raw engine log can stay folded away. Kept in memory only.
/// </summary>
public static class ActivityFeed
{
    private const int MaxItems = 200;

    public static ObservableCollection<string> Items { get; } = [];

    public static void Add(string? message)
    {
        if (message.IsNullOrEmpty())
        {
            return;
        }
        var line = $"{DateTime.Now:HH:mm}  {message.Trim()}";
        Dispatcher.UIThread.Post(() =>
        {
            Items.Insert(0, line);
            while (Items.Count > MaxItems)
            {
                Items.RemoveAt(Items.Count - 1);
            }
        });
    }

    /// <summary>The list for one of the log pane's Activity tabs (each layout needs its own control).</summary>
    public static Control Create()
    {
        var empty = new TextBlock
        {
            Text = ResUI.TipNoActivity,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(4, 8),
        };
        empty.Classes.Add("muted");
        var list = new ItemsControl
        {
            ItemsSource = Items,
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<string>((text, _) =>
                new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 3) }),
        };
        void Refresh() => empty.IsVisible = Items.Count == 0;
        Items.CollectionChanged += (_, _) => Refresh();
        Refresh();
        var panel = new StackPanel();
        panel.Children.Add(empty);
        panel.Children.Add(list);
        return new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
    }
}
