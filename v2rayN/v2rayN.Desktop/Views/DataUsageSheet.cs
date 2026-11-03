using DialogHostAvalonia;
using HAlign = Avalonia.Layout.HorizontalAlignment;
using Orientation = Avalonia.Layout.Orientation;
using VAlign = Avalonia.Layout.VerticalAlignment;

namespace v2rayN.Desktop.Views;

/// <summary>
/// "Data usage": today's and the total traffic, the servers that used most, and an optional daily
/// limit warning. Reads upstream's per-server statistics (in KB), which are off by default.
/// </summary>
public sealed class DataUsageSheet : UserControl
{
    private static string LimitFile => Utils.GetConfigPath("glide-usage-limit");

    /// <summary>The daily limit in GB, 0 when off.</summary>
    public static double LimitGb
    {
        get
        {
            try
            {
                return File.Exists(LimitFile)
                    && double.TryParse(File.ReadAllText(LimitFile), NumberStyles.Float, CultureInfo.InvariantCulture, out var gb)
                    && gb > 0 ? gb : 0;
            }
            catch
            {
                return 0;
            }
        }
    }

    /// <summary>Today's traffic over all servers, in KB (the unit of upstream's statistics).</summary>
    public static long TodayKb => StatisticsManager.Instance.ServerStat.Sum(s => s.TodayUp + s.TodayDown);

    public DataUsageSheet()
    {
        Width = 540;
    }

    public async Task LoadAsync()
    {
        var config = AppManager.Instance.Config;
        var panel = new StackPanel { Margin = new Thickness(24, 22), Spacing = 12 };
        var title = new TextBlock { Text = ResUI.TbDataUsage };
        title.Classes.Add("h2");
        panel.Children.Add(title);

        if (!config.GuiItem.EnableStatistics)
        {
            panel.Children.Add(Muted(ResUI.TipDataUsageOff));
            var turnOn = new Button { Content = ResUI.TbTurnOn, HorizontalAlignment = HAlign.Left };
            turnOn.Click += async (_, _) =>
            {
                config.GuiItem.EnableStatistics = true;
                await ConfigHandler.SaveConfig(config);
                Close();
                // MainWindow turns this into "restart now?".
                NoticeManager.Instance.Enqueue(ResUI.NeedRebootTips);
            };
            panel.Children.Add(turnOn);
        }
        else
        {
            var stats = StatisticsManager.Instance.ServerStat.ToList();
            panel.Children.Add(new TextBlock
            {
                FontWeight = FontWeight.SemiBold,
                Text = $"‎{string.Format(ResUI.TbUsageToday, Utils.HumanFy(stats.Sum(s => s.TodayUp)), Utils.HumanFy(stats.Sum(s => s.TodayDown)))}",
            });
            panel.Children.Add(Muted(string.Format(ResUI.TbUsageTotal, Utils.HumanFy(stats.Sum(s => s.TotalUp)), Utils.HumanFy(stats.Sum(s => s.TotalDown)))));

            var names = (await AppManager.Instance.ProfileItems("") ?? []).GroupBy(p => p.IndexId).ToDictionary(g => g.Key, g => g.First().Remarks);
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(0, 4, 0, 0) };
            var row = 0;
            foreach (var s in stats.Where(s => s.TotalUp + s.TotalDown > 0)
                         .OrderByDescending(s => s.TodayUp + s.TodayDown).ThenByDescending(s => s.TotalUp + s.TotalDown).Take(8))
            {
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                AddCell(grid, row, 0, names.GetValueOrDefault(s.IndexId) ?? s.IndexId, false);
                AddCell(grid, row, 1, Utils.HumanFy(s.TodayUp + s.TodayDown), true);
                AddCell(grid, row, 2, Utils.HumanFy(s.TotalUp + s.TotalDown), true);
                row++;
            }
            panel.Children.Add(grid);
        }

        // Daily limit (also checked by MainWindow every minute).
        var limitRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        limitRow.Children.Add(new TextBlock { Text = ResUI.TbUsageLimit, VerticalAlignment = VAlign.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 360 });
        var limit = new TextBox { Width = 80, Text = LimitGb > 0 ? LimitGb.ToString(CultureInfo.InvariantCulture) : "0", FlowDirection = FlowDirection.LeftToRight };
        limit.TextChanged += (_, _) =>
        {
            try
            {
                if (double.TryParse(limit.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var gb) && gb >= 0)
                {
                    File.WriteAllText(LimitFile, gb.ToString(CultureInfo.InvariantCulture));
                }
            }
            catch
            {
                // Only a preference.
            }
        };
        limitRow.Children.Add(limit);
        panel.Children.Add(limitRow);

        var close = new Button { Content = ResUI.TbClose, HorizontalAlignment = HAlign.Right, MinWidth = 96 };
        close.Click += (_, _) => Close();
        panel.Children.Add(close);
        Content = panel;
    }

    private static TextBlock Muted(string text)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        block.Classes.Add("muted");
        return block;
    }

    private static void AddCell(Grid grid, int row, int column, string text, bool number)
    {
        var cell = new TextBlock
        {
            Text = number ? $"‎{text}" : text,
            Margin = new Thickness(column == 0 ? 0 : 18, 3, 0, 3),
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = number ? HAlign.Right : HAlign.Stretch,
        };
        Grid.SetRow(cell, row);
        Grid.SetColumn(cell, column);
        grid.Children.Add(cell);
    }

    private static void Close()
    {
        if (DialogHost.IsDialogOpen(null))
        {
            DialogHost.Close(null);
        }
    }
}
