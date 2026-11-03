using Avalonia.Controls.Shapes;
using DialogHostAvalonia;
using v2rayN.Desktop.Common;

namespace v2rayN.Desktop.Views;

public partial class DiagnoseSheet : UserControl
{
    private const string Tag = "DiagnoseSheet";
    private CancellationTokenSource? _run;

    public DiagnoseSheet()
    {
        InitializeComponent();
        btnClose.Click += (_, _) => Close();
        btnRunAgain.Click += async (_, _) => await RunAsync();
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
        _ = shown.RunAsync();
    }

    public async Task RunAsync()
    {
        _run?.Cancel();
        _run = new CancellationTokenSource();
        var token = _run.Token;

        spSteps.Children.Clear();
        txtVerdict.Text = ResUI.TbStatusChecking;
        btnRunAgain.IsEnabled = false;
        try
        {
            var verdict = await ConnectionDoctor.RunAsync(AddRow, token);
            if (!token.IsCancellationRequested)
            {
                txtVerdict.Text = verdict;
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
            txtVerdict.Text = ResUI.OperationFailed;
        }
        finally
        {
            btnRunAgain.IsEnabled = true;
        }
    }

    private void AddRow(ConnectionDoctor.Step step)
    {
        var brushKey = step.Outcome switch
        {
            ConnectionDoctor.Outcome.Ok => "AppGoodDotBrush",
            ConnectionDoctor.Outcome.Failed => "AppBadDotBrush",
            _ => "AppFaintBrush",
        };
        var dot = new Ellipse { Width = 9, Height = 9, Margin = new Thickness(0, 5, 0, 0), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        // A live resource binding: resolves once the dot is in the tree and follows theme changes.
        dot.Bind(Shape.FillProperty, dot.GetResourceObservable(brushKey));

        var text = new StackPanel { Spacing = 1 };
        text.Children.Add(new TextBlock { Text = step.Name, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        if (step.Detail.IsNotEmpty())
        {
            var detail = new TextBlock { Text = step.Detail, TextWrapping = TextWrapping.Wrap };
            detail.Classes.Add("muted");
            text.Children.Add(detail);
        }

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
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
