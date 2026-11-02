using System.Text.RegularExpressions;
using DialogHostAvalonia;
using v2rayN.Desktop.Common;

namespace v2rayN.Desktop.Views;

public partial class StatusBarView : ReactiveUserControl<StatusBarViewModel>
{
    private static Config _config;

    // "proxy : 1.2 KB/s↑ | 3.4 MB/s↓" (ResUI.SpeedDisplayText, same in every language)
    private static readonly Regex SpeedRegex = new(@"^\s*(?<tag>[^:]*?)\s*:\s*(?<up>.+?)↑\s*\|\s*(?<down>.+?)↓\s*$", RegexOptions.Compiled);

    // "[VLESS] Frankfurt 02(fra2***net:443)" (ProfileItem.GetSummary)
    private static readonly Regex SummaryRegex = new(@"^\[(?<type>[^\]]+)\]\s*(?<name>.*?)(\((?<addr>[^()]*)\))?$", RegexOptions.Compiled);

    public StatusBarView()
    {
        InitializeComponent();

        _config = AppManager.Instance.Config;

        spRunningServer.Tapped += TxtRunningServerDisplay_Tapped;
        btnPower.Click += BtnPower_Click;

        this.WhenActivated(disposables =>
        {
            //status bar
            this.OneWayBind(ViewModel, vm => vm.RunningInfoDisplay, v => v.txtRunningInfoDisplay.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.EnableTun, v => v.togEnableTun.IsChecked).DisposeWith(disposables);

            this.Bind(ViewModel, vm => vm.SystemProxySelected, v => v.cmbSystemProxy.SelectedIndex).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SelectedRouting, v => v.cmbRoutings2.SelectedItem).DisposeWith(disposables);

            this.WhenAnyValue(v => v.ViewModel.SystemProxySelected)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(UpdatePowerState)
                .DisposeWith(disposables);

            this.WhenAnyValue(v => v.ViewModel.RunningServerDisplay)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(UpdateRunningServer)
                .DisposeWith(disposables);

            this.WhenAnyValue(v => v.ViewModel.SpeedProxyDisplay, v => v.ViewModel.SpeedDirectDisplay, (proxy, direct) => (proxy, direct))
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(t => UpdateSpeed(t.proxy, t.direct))
                .DisposeWith(disposables);

            this.WhenAnyValue(v => v.ViewModel.InboundDisplay)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(s => SetKeyValue(s, txtInboundKey, txtInboundValue))
                .DisposeWith(disposables);

            this.WhenAnyValue(v => v.ViewModel.InboundLanDisplay)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(s => SetKeyValue(s, txtInboundLanKey, txtInboundLanValue))
                .DisposeWith(disposables);

            ViewModel.SetClipboardDataInteraction.RegisterHandler(async interaction =>
            {
                var strData = interaction.Input;
                await AvaUtils.SetClipboardData(this, strData);
                interaction.SetOutput(RxVoid.Default);
            }).DisposeWith(disposables);

            ViewModel.PasswordInputInteraction.RegisterHandler(async interaction =>
            {
                var result = await PasswordInputAsync();
                interaction.SetOutput(result);
            }).DisposeWith(disposables);

            ViewModel.DispatcherRefreshIconInteraction.RegisterHandler(interaction =>
            {
                Dispatcher.UIThread.Post(RefreshIcon, DispatcherPriority.Default);
                interaction.SetOutput(RxVoid.Default);
            }).DisposeWith(disposables);
        });

        //spEnableTun.IsVisible = (Utils.IsWindows() || AppHandler.Instance.IsAdministrator);

        if (Utils.IsNonWindows() && cmbSystemProxy.Items.IsReadOnly == false)
        {
            cmbSystemProxy.Items.RemoveAt(cmbSystemProxy.Items.Count - 1);
        }

        // Because this view has not yet been initialized when DispatcherRefreshIconInteraction is first called.
        RefreshIcon();
    }

    private void RefreshIcon()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow.Icon = AvaUtils.GetAppIcon(_config.SystemProxyItem.SysProxyType);
            var iconslist = TrayIcon.GetIcons(Application.Current);
            iconslist[0].Icon = desktop.MainWindow.Icon;
            TrayIcon.SetIcons(Application.Current, iconslist);
        }
    }

    private async Task<string?> PasswordInputAsync()
    {
        var dialog = new SudoPasswordInputView();
        var obj = await DialogHost.Show(dialog);

        var password = obj?.ToString();
        if (password.IsNullOrEmpty())
        {
            togEnableTun.IsChecked = false;
            return password;
        }

        AppManager.Instance.LinuxSudoPwd = password;
        return password;
    }

    private void BtnPower_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel == null)
        {
            return;
        }
        // The big button switches the system proxy on (set) or off (clear).
        var isOn = ViewModel.SystemProxySelected is (int)ESysProxyType.ForcedChange or (int)ESysProxyType.Pac;
        ViewModel.SystemProxySelected = isOn ? (int)ESysProxyType.ForcedClear : (int)ESysProxyType.ForcedChange;
    }

    private void UpdatePowerState(int selected)
    {
        var isOn = selected is (int)ESysProxyType.ForcedChange or (int)ESysProxyType.Pac;
        btnPower.Classes.Set("on", isOn);
        bdPowerState.Classes.Set("on", isOn);
        txtPowerState.Text = selected switch
        {
            (int)ESysProxyType.ForcedChange => "Proxy on",
            (int)ESysProxyType.Pac => "PAC mode on",
            (int)ESysProxyType.Unchanged => "System proxy unchanged",
            _ => "Proxy off",
        };
    }

    private void UpdateRunningServer(string? summary)
    {
        summary ??= string.Empty;
        spRunningServer.SetValue(ToolTip.TipProperty, summary.IsNullOrEmpty() ? null : summary);

        var match = SummaryRegex.Match(summary);
        if (!match.Success)
        {
            txtServerName.Text = summary;
            bdServerType.IsVisible = false;
            txtServerAddress.Text = string.Empty;
            return;
        }

        var name = match.Groups["name"].Value.Trim();
        txtServerName.Text = name.IsNullOrEmpty() ? summary : name;
        txtServerType.Text = match.Groups["type"].Value;
        bdServerType.IsVisible = true;
        txtServerAddress.Text = match.Groups["addr"].Value;
    }

    private void UpdateSpeed(string? proxy, string? direct)
    {
        var p = SpeedRegex.Match(proxy ?? string.Empty);
        spSpeed.IsVisible = p.Success;
        if (!p.Success)
        {
            return;
        }
        txtSpeedUp.Text = p.Groups["up"].Value.Trim();
        txtSpeedDown.Text = p.Groups["down"].Value.Trim();

        var d = SpeedRegex.Match(direct ?? string.Empty);
        txtSpeedDirect.IsVisible = d.Success;
        if (d.Success)
        {
            txtSpeedDirect.Text = $"{d.Groups["tag"].Value}   ↑ {d.Groups["up"].Value.Trim()}   ↓ {d.Groups["down"].Value.Trim()}";
        }
    }

    private static void SetKeyValue(string? text, TextBlock key, TextBlock value)
    {
        // "Local:[mixed:10808]" -> "Local" | "mixed:10808"
        text ??= string.Empty;
        var idx = text.IndexOf(':');
        key.Text = idx > 0 ? text[..idx] : text;
        value.Text = idx > 0 ? text[(idx + 1)..].Trim('[', ']', ' ') : string.Empty;
    }

    private void TxtRunningServerDisplay_Tapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        ViewModel?.TestServerAvailability();
    }
}
