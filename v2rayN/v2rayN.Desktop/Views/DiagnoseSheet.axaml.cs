using Avalonia.Controls.Shapes;
using DialogHostAvalonia;
using v2rayN.Desktop.Common;

namespace v2rayN.Desktop.Views;

public partial class DiagnoseSheet : UserControl
{
    private new const string Tag = "DiagnoseSheet";
    private CancellationTokenSource? _run;

    public DiagnoseSheet()
    {
        InitializeComponent();
        btnClose.Click += (_, _) => Close();
        btnRunAgain.Click += async (_, _) => await RunAsync();
        btnDnsLeak.Click += async (_, _) => await CheckDnsLeakAsync();
    }

    private async Task CheckDnsLeakAsync()
    {
        _run?.Cancel();
        _run = new CancellationTokenSource();
        var token = _run.Token;
        btnDnsLeak.IsEnabled = false;
        btnRunAgain.IsEnabled = false;
        spSteps.Children.Clear();
        txtVerdict.Text = ResUI.TbStatusChecking;
        try
        {
            var result = await ConnectionDoctor.CheckDnsLeakAsync(token);
            if (token.IsCancellationRequested)
            {
                return;
            }
            if (!result.Done)
            {
                txtVerdict.Text = ResUI.TipDnsLeakFailed;
                return;
            }
            AddRow(new(ResUI.TipDnsLeakServers, ConnectionDoctor.Outcome.Ok, string.Join(Environment.NewLine, result.Servers)));
            txtVerdict.Text = result.Conclusion ?? string.Empty;
        }
        catch (OperationCanceledException)
        {
            // Closed while running.
        }
        catch (Exception ex)
        {
            // An exception escaping here would reach the dispatcher and close the app.
            Logging.SaveLog(Tag, ex);
            txtVerdict.Text = ResUI.TipDnsLeakFailed;
        }
        finally
        {
            btnDnsLeak.IsEnabled = true;
            btnRunAgain.IsEnabled = true;
        }
    }

    /// <summary>Shows the sheet and starts the check (nothing happens if another sheet is open).</summary>
    public static void Open(ref DiagnoseSheet? sheet)
    {
        if (DialogHost.IsDialogOpen(null))
        {
            return;
        }
        sheet ??= new DiagnoseSheet();
        var shown = sheet;
        DialogHost.Show(shown);
        AppMotion.PlayEntrance(shown, 6, AppMotion.RevealMs);
        shown.RunAsync().Forget(nameof(DiagnoseSheet));
    }

    public async Task RunAsync()
    {
        _run?.Cancel();
        _run = new CancellationTokenSource();
        var token = _run.Token;

        spSteps.Children.Clear();
        txtVerdict.Text = ResUI.TbStatusChecking;
        btnRunAgain.IsEnabled = false;
        btnDnsLeak.IsEnabled = false;
        try
        {
            var verdict = await ConnectionDoctor.RunAsync(AddRow, token);
            if (!token.IsCancellationRequested)
            {
                txtVerdict.Text = verdict;
                // The verdict often repeats the last failed step's explanation word for word; say it once.
                foreach (var detail in spSteps.Children
                    .OfType<Grid>()
                    .SelectMany(g => g.Children.OfType<StackPanel>())
                    .SelectMany(p => p.Children.OfType<TextBlock>())
                    .Where(t => t.Classes.Contains("muted") && t.Text == verdict))
                {
                    detail.IsVisible = false;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Closed while running.
        }
        catch (Exception ex)
        {
            // An exception escaping here would reach the dispatcher and close the app.
            Logging.SaveLog(Tag, ex);
            txtVerdict.Text = ResUI.TipDiagnoseFailed;
        }
        finally
        {
            btnRunAgain.IsEnabled = true;
            btnDnsLeak.IsEnabled = true;
        }
    }

    private void AddRow(ConnectionDoctor.Step step)
    {
        // Passed / failed / skipped is told by the mark's shape as well as its colour (and spoken by
        // screen readers), since most passed steps have no text of their own.
        var (brushKey, glyph, state) = step.Outcome switch
        {
            ConnectionDoctor.Outcome.Ok => ("AppGoodDotBrush", "✓", ResUI.TbStepPassed),
            ConnectionDoctor.Outcome.Failed => ("AppBadDotBrush", "✕", ResUI.TbStepFailed),
            _ => ("AppFaintBrush", "–", ResUI.TbStepSkipped),
        };
        var dot = new TextBlock
        {
            Text = glyph,
            Width = 14,
            FontWeight = FontWeight.SemiBold,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
        };
        // A live resource binding: resolves once the mark is in the tree and follows theme changes.
        dot.Bind(TextBlock.ForegroundProperty, dot.GetResourceObservable(brushKey));
        ToolTip.SetTip(dot, state);

        var text = new StackPanel { Spacing = 1 };
        text.Children.Add(new TextBlock { Text = step.Name, FontWeight = FontWeight.Medium, TextWrapping = TextWrapping.Wrap });
        if (step.Detail.IsNotEmpty())
        {
            var detail = new TextBlock { Text = step.Detail, TextWrapping = TextWrapping.Wrap };
            detail.Classes.Add("muted");
            text.Children.Add(detail);
        }

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        Avalonia.Automation.AutomationProperties.SetName(row, $"{step.Name}: {state}");
        Grid.SetColumn(text, 1);
        text.Margin = new Thickness(10, 0, 0, 0);
        row.Children.Add(dot);
        row.Children.Add(text);
        spSteps.Children.Add(row);
    }

    private void Close()
    {
        _run?.Cancel();
        if (DialogHost.IsDialogOpen(null))
        {
            DialogHost.Close(null);
        }
    }
}
