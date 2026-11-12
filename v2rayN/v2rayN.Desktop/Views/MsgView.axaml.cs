using v2rayN.Desktop.Common;

namespace v2rayN.Desktop.Views;

public partial class MsgView : ReactiveUserControl<MsgViewModel>
{
    //private const int KeepLines = 30;

    public MsgView()
    {
        InitializeComponent();
        txtMsg.TextArea.TextView.Options.EnableHyperlinks = false;

        // Narrow log card: the toggles keep their tooltips but drop the labels, so the header stays on one row.
        // The labels show only when they really fit next to the copy/clear buttons, measured in the current
        // language and font size (Persian needs about 50px more than English).
        SizeChanged += (_, e) => UpdateToggleLabels(e.NewSize.Width);

        this.WhenActivated(disposables =>
        {
            this.Bind(ViewModel, vm => vm.MsgFilter, v => v.cmbMsgFilter.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.AutoRefresh, v => v.togAutoRefresh.IsChecked).DisposeWith(disposables);

            ViewModel.ShowMsgInteraction.RegisterHandler(interaction =>
            {
                var msg = interaction.Input;
                ShowMsg(msg);
                interaction.SetOutput(RxVoid.Default);
            }).DisposeWith(disposables);
        });

        TextEditorKeywordHighlighter.Attach(txtMsg, Global.LogLevelColors.ToDictionary(
                kv => kv.Key,
                kv => (IBrush)new SolidColorBrush(Color.Parse(kv.Value))
            ));
    }

    private void UpdateToggleLabels(double width)
    {
        if (width <= 0)
        {
            return;
        }
        static double TextWidth(TextBlock t) => new FormattedText(
            t.Text ?? string.Empty,
            System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(t.FontFamily, t.FontStyle, t.FontWeight),
            t.FontSize,
            null).WidthIncludingTrailingWhitespace;

        // Toolbar, then two groups of label + 8 + switch, 14 apart (as laid out in MsgView.axaml), plus a margin.
        var tools = bdMsgTools.Bounds.Width > 0 ? bdMsgTools.Bounds.Width : 70;
        var switchW = togAutoRefresh.Bounds.Width > 0 ? togAutoRefresh.Bounds.Width : 48;
        var need = tools + 14 + TextWidth(lblAutoRefresh) + 8 + switchW + 14 + TextWidth(lblScrollToEnd) + 8 + switchW + 6;
        var wide = width >= need;
        lblAutoRefresh.IsVisible = wide;
        lblScrollToEnd.IsVisible = wide;
    }

    private void ShowMsg(object msg)
    {
        //var lineCount = txtMsg.LineCount;
        //if (lineCount > ViewModel?.NumMaxMsg)
        //{
        //    var cutLine = txtMsg.Document.GetLineByNumber(lineCount - KeepLines);
        //    txtMsg.Document.Remove(0, cutLine.Offset);
        //}
        if (txtMsg.LineCount > ViewModel?.NumMaxMsg)
        {
            ClearMsg();
        }

        txtMsg.AppendText(msg.ToString());
        if (togScrollToEnd.IsChecked ?? true)
        {
            txtMsg.ScrollToEnd();
            // Let layout finish without a nested dispatcher loop during a native window drag.
            Dispatcher.UIThread.Post(() =>
            {
                if (togScrollToEnd.IsChecked ?? true)
                {
                    txtMsg.ScrollTo(txtMsg.LineCount, 0, AvaloniaEdit.Rendering.VisualYPosition.TextBottom, txtMsg.Bounds.Height, 0);
                }
            }, DispatcherPriority.Background);
        }
    }

    public void ClearMsg()
    {
        txtMsg.Clear();
        txtMsg.AppendText("----- Message cleared -----\n");
    }

    private void menuMsgViewSelectAll_Click(object? sender, RoutedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            txtMsg.TextArea.Focus();
            txtMsg.SelectAll();
        }, DispatcherPriority.Render);
    }

    private async void menuMsgViewCopy_Click(object? sender, RoutedEventArgs e)
    {
        var data = txtMsg.SelectedText.TrimEx();
        await AvaUtils.SetClipboardData(this, data);
    }

    private async void menuMsgViewCopyAll_Click(object? sender, RoutedEventArgs e)
    {
        var data = txtMsg.Text.TrimEx();
        await AvaUtils.SetClipboardData(this, data);
    }

    private void menuMsgViewClear_Click(object? sender, RoutedEventArgs e)
    {
        ClearMsg();
    }
}
