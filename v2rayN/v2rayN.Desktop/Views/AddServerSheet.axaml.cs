using DialogHostAvalonia;
using v2rayN.Desktop.Common;

namespace v2rayN.Desktop.Views;

/// <summary>
/// The "Add server" sheet. Everything goes through the main view model's existing import paths
/// (AddBatchServers for pasted text, the QR scanners, the per-protocol forms); this only gathers
/// the input and shows what was found before importing.
/// </summary>
public partial class AddServerSheet : UserControl
{
    private const string Tag = "AddServerSheet";

    // The everyday protocols; custom configs, policy groups and chains stay in the Servers menu.
    private static readonly EConfigType[] ManualTypes =
    [
        EConfigType.VLESS, EConfigType.VMess, EConfigType.Trojan, EConfigType.Shadowsocks,
        EConfigType.Hysteria2, EConfigType.TUIC, EConfigType.WireGuard, EConfigType.Anytls,
        EConfigType.Naive, EConfigType.MASQUE, EConfigType.SOCKS, EConfigType.HTTP,
    ];

    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private ImportPreview.Result _preview = new(0, []);

    public MainWindowViewModel? Main { get; set; }

    public AddServerSheet()
    {
        InitializeComponent();

        txtImport.TextChanged += (_, _) =>
        {
            _previewTimer.Stop();
            _previewTimer.Start();
        };
        _previewTimer.Tick += (_, _) =>
        {
            _previewTimer.Stop();
            UpdatePreview();
        };

        btnPaste.Click += async (_, _) => await Run(async () =>
        {
            if (TopLevel.GetTopLevel(this) is Window window && await AvaUtils.GetClipboardData(window) is { Length: > 0 } text)
            {
                txtImport.Text = text;
                UpdatePreview();
            }
        });
        btnCancel.Click += (_, _) => Close();
        btnImport.Click += async (_, _) => await Run(ImportAsync);
        btnScanScreen.Click += async (_, _) => await CloseThen(m => m.AddServerViaScanAsync());
        btnScanImage.Click += async (_, _) => await CloseThen(m => m.AddServerViaImageAsync());

        var manual = new MenuFlyout();
        foreach (var type in ManualTypes)
        {
            var item = new MenuItem { Header = type.ToString() };
            item.Click += async (_, _) => await CloseThen(m => m.AddServerAsync(type));
            manual.Items.Add(item);
        }
        btnManual.Flyout = manual;
    }

    /// <summary>Clears what was typed last time; called before each showing.</summary>
    public void Reset(string? text = null)
    {
        txtImport.Text = text ?? string.Empty;
        UpdatePreview();
        Dispatcher.UIThread.Post(() => txtImport.Focus(), DispatcherPriority.Loaded);
    }

    private void UpdatePreview()
    {
        _preview = ImportPreview.Analyze(txtImport.Text);
        var hasText = txtImport.Text?.Trim().IsNotEmpty() == true;
        var servers = _preview.Servers.Count;
        var subscriptions = _preview.Subscriptions;

        var found = new List<string>();
        if (servers > 0)
        {
            found.Add(string.Format(ResUI.TipImportServers, servers, _preview.Protocols));
        }
        if (subscriptions > 0)
        {
            found.Add(string.Format(ResUI.TipImportSubs, subscriptions));
        }
        txtPreview.Text = found.Count > 0 ? string.Join("  ·  ", found) : hasText ? ResUI.TipImportUnknown : string.Empty;
        txtPreview.Classes.Set("muted", found.Count == 0);

        btnImport.IsEnabled = hasText;
        btnImport.Content = servers > 0 && subscriptions == 0 ? string.Format(ResUI.TbAddServers, servers)
            : subscriptions > 0 && servers == 0 ? ResUI.TbAddSubscription
            : ResUI.TbImport;
    }

    private async Task ImportAsync()
    {
        var text = txtImport.Text;
        var hasSubscription = ImportPreview.Analyze(text).Subscriptions > 0;
        var main = Main;
        Close();
        if (main == null || text.IsNullOrEmpty())
        {
            return;
        }
        await main.AddServerViaClipboardAsync(text);
        MainWindow.RememberClipboardOffered(text);
        // A new subscription is empty until it's updated: fetch its servers right away.
        if (hasSubscription)
        {
            await main.UpdateSubscriptionProcess("", false);
        }
    }

    private async Task CloseThen(Func<MainWindowViewModel, Task> action)
    {
        var main = Main;
        Close();
        if (main != null)
        {
            await Run(() => action(main));
        }
    }

    private static void Close()
    {
        if (DialogHost.IsDialogOpen(null))
        {
            DialogHost.Close(null);
        }
    }

    // Click handlers are async void underneath: an exception escaping here would close the app.
    private static async Task Run(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Logging.SaveLog(Tag, ex);
            NoticeManager.Instance.Enqueue(ResUI.OperationFailed);
        }
    }
}
