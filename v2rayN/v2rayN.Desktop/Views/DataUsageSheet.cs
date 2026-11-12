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

    // Upstream keeps only today and the total, so each day's figure is kept here for the monthly sum:
    // {"2026-11-04": "123456"} (KB), the last ~2 months.
    private static string DaysFile => Utils.GetConfigPath("glide-usage-days.json");

    /// <summary>Remembers today's traffic (called every minute while statistics are on).</summary>
    public static void RecordToday(long todayKb)
    {
        try
        {
            if (!AppManager.Instance.Config.GuiItem.EnableStatistics)
            {
                return;
            }
            var days = LoadDays();
            days[DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)] = todayKb.ToString(CultureInfo.InvariantCulture);
            foreach (var old in days.Keys.Where(k => string.CompareOrdinal(k, DateTime.Today.AddDays(-62).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) < 0).ToList())
            {
                days.Remove(old);
            }
            GlideFile.WriteAtomic(DaysFile, JsonUtils.Serialize(days, false));
        }
        catch
        {
            // Only a convenience.
        }
    }

    /// <summary>This calendar month's traffic in KB.</summary>
    public static long MonthKb()
    {
        var month = DateTime.Today.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var days = LoadDays();
        days[DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)] = TodayKb.ToString(CultureInfo.InvariantCulture);
        return days.Where(d => d.Key.StartsWith(month)).Sum(d => long.TryParse(d.Value, out var kb) ? kb : 0);
    }

    private static Dictionary<string, string> LoadDays()
    {
        return GlideFile.Read(DaysFile, JsonUtils.Deserialize<Dictionary<string, string>>) ?? [];
    }

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
                FontWeight = FontWeight.Medium,
                Text = $"‎{string.Format(ResUI.TbUsageToday, Size(stats.Sum(s => s.TodayUp)), Size(stats.Sum(s => s.TodayDown)))}",
            });
            panel.Children.Add(new TextBlock { Text = $"‎{string.Format(ResUI.TbUsageMonth, Size(MonthKb()))}" });
            panel.Children.Add(Muted(string.Format(ResUI.TbUsageTotal, Size(stats.Sum(s => s.TotalUp)), Size(stats.Sum(s => s.TotalDown)))));

            var names = (await AppManager.Instance.ProfileItems("") ?? []).GroupBy(p => p.IndexId).ToDictionary(g => g.Key, g => g.First().Remarks);
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(0, 4, 0, 0) };
            var row = 0;
            foreach (var s in stats.Where(s => s.TotalUp + s.TotalDown > 0)
                         .OrderByDescending(s => s.TodayUp + s.TodayDown).ThenByDescending(s => s.TotalUp + s.TotalDown).Take(8))
            {
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                AddCell(grid, row, 0, names.GetValueOrDefault(s.IndexId) ?? s.IndexId, false);
                AddCell(grid, row, 1, Size(s.TodayUp + s.TodayDown), true);
                AddCell(grid, row, 2, Size(s.TotalUp + s.TotalDown), true);
                row++;
            }
            // Counting is on but nothing went through yet: say so instead of an empty table.
            panel.Children.Add(row > 0 ? grid : Muted(ResUI.TipNoUsageYet));
        }

        // Daily limit (also checked by MainWindow every minute).
        var limitRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        limitRow.Children.Add(new TextBlock { Text = ResUI.TbUsageLimit, VerticalAlignment = VAlign.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 360 });
        // The limit can only be checked while usage is counted, so it waits until counting is on.
        var limit = new TextBox { Width = 80, Text = LimitGb > 0 ? LimitGb.ToString(CultureInfo.InvariantCulture) : "0", FlowDirection = FlowDirection.LeftToRight, IsEnabled = config.GuiItem.EnableStatistics };
        limit.TextChanged += (_, _) =>
        {
            // Persian or Arabic digits (۵، ٥) and the Persian decimal sign count too.
            var text = new string((limit.Text ?? string.Empty).Trim().Select(c => c switch
            {
                >= '۰' and <= '۹' => (char)('0' + (c - '۰')),
                >= '٠' and <= '٩' => (char)('0' + (c - '٠')),
                '٫' or '/' => '.',
                _ => c,
            }).ToArray());
            var valid = text.Length > 0 && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var gb) && gb >= 0;
            // A wrong value is pointed out next to the box instead of being ignored silently.
            DataValidationErrors.SetErrors(limit, valid || text.Length == 0 ? null : [ResUI.TipUsageLimitInvalid]);
            if (!valid)
            {
                return;
            }
            try
            {
                File.WriteAllText(LimitFile, double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture));
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

    // Amounts in the interface's digits (Persian digits in Persian), like the speed cards.
    private static string Size(long kb) => Common.UiDigits.Localize(Utils.HumanFy(kb));

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
