using System.Text.RegularExpressions;
using DialogHostAvalonia;
using v2rayN.Desktop.Common;

namespace v2rayN.Desktop.Views;

public partial class StatusBarView : ReactiveUserControl<StatusBarViewModel>
{
    private static Config _config;

    // "proxy : 1.2 KB/s↑ | 3.4 MB/s↓" (ResUI.SpeedDisplayText, same in every language)
    private static readonly Regex SpeedRegex = new(@"^\s*(?<tag>[^:]*?)\s*:\s*(?<up>.+?)↑\s*\|\s*(?<down>.+?)↓\s*$", RegexOptions.Compiled);

    // "[VLESS] Frankfurt 02(fra2***net:443)", or for groups "[PolicyGroup] [Xray]⚡ Auto (fastest)"
    // (ProfileItem.GetSummary). The core tag is skipped, and only a trailing "(…:port)" is the address,
    // so names with their own parentheses stay whole.
    private static readonly Regex SummaryRegex = new(@"^\[(?<type>[^\]]+)\]\s*(?:\[[^\]]+\])?\s*(?<name>.*?)(\((?<addr>[^()]*:\d+)\))?$", RegexOptions.Compiled);

    public StatusBarView()
    {
        InitializeComponent();

        _config = AppManager.Instance.Config;

        spRunningServer.Tapped += TxtRunningServerDisplay_Tapped;
        btnPower.Click += BtnPower_Click;
        btnStatusAction.Click += BtnStatusAction_Click;
        btnDiagnose.Click += (_, _) => DiagnoseSheet.Open(ref _diagnoseSheet);
        _monitor.Changed += RenderStatus;
        _sessionTimer.Tick += (_, _) => UpdateSessionTime();
        _tickTimer.Tick += (_, _) => SpeedTick();

        // Changing the proxy mode selects a segment, which asks to be scrolled into view and made the
        // whole column jump; the segments are always visible next to the power button anyway.
        cmbSystemProxy.AddHandler(RequestBringIntoViewEvent, (_, e) => e.Handled = true);

        this.WhenActivated(disposables =>
        {
            //status bar
            this.Bind(ViewModel, vm => vm.EnableTun, v => v.togEnableTun.IsChecked).DisposeWith(disposables);

            // The upstream delay test runs after every core (re)load; when its answer arrives the
            // core is up, so that is the moment to verify the connection again.
            this.WhenAnyValue(v => v.ViewModel.RunningInfoDisplay)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(info =>
                {
                    if (info.IsNotEmpty() && info != ResUI.Speedtesting)
                    {
                        _monitor.CheckSoon(400);
                    }
                })
                .DisposeWith(disposables);

            this.WhenAnyValue(v => v.ViewModel.EnableTun)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(_ => UpdateMonitor())
                .DisposeWith(disposables);

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

    private readonly ConnectionMonitor _monitor = new();
    private DiagnoseSheet? _diagnoseSheet;
    private bool _hasServer;

    private static bool IsProxyOn(int selected) => selected is (int)ESysProxyType.ForcedChange or (int)ESysProxyType.Pac;

    private void BtnPower_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel == null)
        {
            return;
        }
        if (!_hasServer)
        {
            NoticeManager.Instance.Enqueue(ResUI.TipAddServerFirst);
            return;
        }
        if (!IsProxyOn(ViewModel.SystemProxySelected) && _monitor.State == ConnState.PortConflict)
        {
            // Connecting can't work while another program holds the port: deal with that first.
            BtnStatusAction_Click(sender, e);
            return;
        }
        // Connect = send Windows traffic through Glide (and verify it); Disconnect = stop doing so.
        var isOn = IsProxyOn(ViewModel.SystemProxySelected);
        ViewModel.SystemProxySelected = isOn ? (int)ESysProxyType.ForcedClear : (int)ESysProxyType.ForcedChange;
    }

    private async void BtnStatusAction_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            switch (_monitor.State)
            {
                case ConnState.PortConflict when _monitor.Conflict is { } owner && ConnectionMonitor.CanClose(owner):
                    var message = string.Format(ResUI.TipConfirmCloseApp, owner.Name, owner.Path);
                    if (await UI.ShowYesNo(message, string.Format(ResUI.TbCloseApp, owner.Name), ResUI.TbCancel) != ButtonResult.Yes)
                    {
                        return;
                    }
                    await Task.Run(() => ConnectionMonitor.CloseOwner(owner));
                    ViewModel?.ReloadRequested.Publish();
                    _monitor.CheckSoon(2500);
                    break;

                case ConnState.EngineStopped:
                    ViewModel?.ReloadRequested.Publish();
                    _monitor.CheckSoon(2500);
                    break;

                default:
                    _monitor.CheckSoon(100);
                    break;
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("StatusBarView", ex);
        }
    }

    private void UpdatePowerState(int selected)
    {
        var isOn = IsProxyOn(selected);
        if (isOn && !btnPower.Classes.Contains("on") && IsLoaded && AppMotion.Enabled)
        {
            // A ring spreads out once when the proxy comes on; removed again so the next switch replays it.
            btnPower.Classes.Add("pulse");
            DispatcherTimer.RunOnce(() => btnPower.Classes.Remove("pulse"), TimeSpan.FromMilliseconds(900));
        }
        btnPower.Classes.Set("on", isOn);
        ToolTip.SetTip(btnPower, isOn ? ResUI.TbDisconnect : ResUI.TbConnect);
        UpdateMonitor();
    }

    private void UpdateMonitor()
    {
        if (ViewModel == null)
        {
            return;
        }
        var inUse = IsProxyOn(ViewModel.SystemProxySelected) || ViewModel.EnableTun;
        _monitor.Update(_hasServer, inUse, txtServerName.Text ?? string.Empty);
        RenderStatus();
    }

    // Pill, detail line and action button for the current connection state.
    private void RenderStatus()
    {
        var selected = ViewModel?.SystemProxySelected ?? (int)ESysProxyType.ForcedClear;
        var state = _monitor.State;
        bdPowerState.Classes.Set("on", state == ConnState.Connected);
        bdPowerState.Classes.Set("warn", state == ConnState.Checking);
        bdPowerState.Classes.Set("bad", state is ConnState.NoInternet or ConnState.PortConflict or ConnState.EngineStopped);

        _powerStateLabel = state switch
        {
            ConnState.NoServer => ResUI.TbNoServerSelected,
            ConnState.Checking => ResUI.TbStatusChecking,
            // Left-to-right marks keep "234 ms" in order inside a right-to-left (Persian) label.
            ConnState.Connected => _monitor.DelayMs > 0 ? $"{ResUI.TbStatusConnected} · ‎{_monitor.DelayMs} ms‎" : ResUI.TbStatusConnected,
            ConnState.NoInternet => ResUI.TbStatusNoInternet,
            ConnState.PortConflict => ResUI.TbStatusPortBusy,
            ConnState.EngineStopped => ResUI.TbStatusEngineStopped,
            _ => selected == (int)ESysProxyType.Unchanged ? ResUI.TbSysProxyUnchanged : ResUI.TbStatusNotConnected,
        };

        txtRunningInfoDisplay.Text = state switch
        {
            ConnState.NoServer => ResUI.TipAddServerToConnect,
            ConnState.Connected => _monitor.ExitIp.IsNotEmpty() ? $"{ResUI.TbExitIp}: {_monitor.ExitIp}" : string.Empty,
            ConnState.NoInternet => ResUI.TipNoInternetThroughServer,
            ConnState.PortConflict => string.Format(ResUI.TipPortBusy, AppManager.Instance.GetLocalPort(EInboundProtocol.socks), _monitor.Conflict?.Name),
            ConnState.EngineStopped => ResUI.TipEngineStoppedHint,
            ConnState.NotConnected => selected == (int)ESysProxyType.Unchanged ? ResUI.TipUnchangedHint : ResUI.TipNotConnectedHint,
            _ => string.Empty,
        };
        txtRunningInfoDisplay.IsVisible = txtRunningInfoDisplay.Text.IsNotEmpty();

        btnStatusAction.Content = state switch
        {
            ConnState.PortConflict when _monitor.Conflict is { } owner && ConnectionMonitor.CanClose(owner) => string.Format(ResUI.TbCloseApp, owner.Name),
            ConnState.NoInternet or ConnState.EngineStopped => ResUI.TbCheckAgain,
            _ => null,
        };
        btnStatusAction.IsVisible = btnStatusAction.Content != null;
        btnDiagnose.IsVisible = state is ConnState.NotConnected or ConnState.NoInternet or ConnState.PortConflict or ConnState.EngineStopped;

        // The session clock runs only while the connection is verified.
        if (state == ConnState.Connected && !_sessionTimer.IsEnabled)
        {
            _sessionStart = DateTime.Now;
            _sessionTimer.Start();
        }
        else if (state != ConnState.Connected && state != ConnState.NoInternet)
        {
            _sessionTimer.Stop();
        }
        UpdateSessionTime();
    }

    private readonly DispatcherTimer _sessionTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private DateTime _sessionStart;
    private string _powerStateLabel = string.Empty;

    private void UpdateSessionTime()
    {
        if (!_sessionTimer.IsEnabled || _monitor.State != ConnState.Connected)
        {
            txtPowerState.Text = _powerStateLabel;
            return;
        }
        var elapsed = DateTime.Now - _sessionStart;
        var time = elapsed.TotalHours >= 1 ? elapsed.ToString(@"h\:mm\:ss") : elapsed.ToString(@"m\:ss");
        txtPowerState.Text = $"{_powerStateLabel} · ‎{time}‎";
    }

    private void UpdateRunningServer(string? summary)
    {
        summary ??= string.Empty;
        _hasServer = summary.IsNotEmpty() && summary != ResUI.CheckServerSettings;
        spRunningServer.SetValue(ToolTip.TipProperty, _hasServer ? summary : null);

        var match = SummaryRegex.Match(summary);
        if (!match.Success)
        {
            txtServerName.Text = _hasServer ? summary : ResUI.TbNoServerSelected;
            bdServerType.IsVisible = false;
            bdServerCountry.IsVisible = false;
            txtServerAddress.Text = string.Empty;
            UpdateMonitor();
            return;
        }

        var name = match.Groups["name"].Value.Trim();
        var country = CountryDetector.Detect(name);
        bdServerCountry.IsVisible = country != null;
        txtServerCountry.Text = country;
        name = CountryDetector.RemoveFlags(name);
        txtServerName.Text = name.IsNullOrEmpty() ? summary : name;
        txtServerType.Text = Converters.ServerLineConverter.TypeName(match.Groups["type"].Value, match.Groups["name"].Value);
        bdServerType.IsVisible = true;
        txtServerAddress.Text = match.Groups["addr"].Value;
        UpdateMonitor();
    }

    private void UpdateSpeed(string? proxy, string? direct)
    {
        var p = SpeedRegex.Match(proxy ?? string.Empty);
        spSpeed.IsVisible = p.Success;
        if (!p.Success)
        {
            _tickTimer.Stop();
            _upShown = _downShown = 0;
            sparkSpeed.Clear();
            return;
        }
        var up = SpeedSparkline.ParseSize(p.Groups["up"].Value);
        var down = SpeedSparkline.ParseSize(p.Groups["down"].Value);
        sparkSpeed.Push(up, down);
        StartSpeedTick(up, down);

        var d = SpeedRegex.Match(direct ?? string.Empty);
        txtSpeedDirect.IsVisible = d.Success;
        if (d.Success)
        {
            txtSpeedDirect.Text = $"{d.Groups["tag"].Value}   ↑ {d.Groups["up"].Value.Trim()}   ↓ {d.Groups["down"].Value.Trim()}";
        }
    }

    // The speed numbers glide to each new value instead of jumping (skipped when Windows animations are off).
    private const double TickMs = 450;

    private readonly DispatcherTimer _tickTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private DateTime _tickStart;
    private double _upFrom, _upTo, _upShown, _downFrom, _downTo, _downShown;

    private void StartSpeedTick(double up, double down)
    {
        _upFrom = _upShown;
        _downFrom = _downShown;
        _upTo = up;
        _downTo = down;
        if (!AppMotion.Enabled)
        {
            SetSpeedText(1);
            return;
        }
        _tickStart = DateTime.Now;
        _tickTimer.Start();
        SetSpeedText(0);
    }

    private void SpeedTick()
    {
        var t = Math.Clamp((DateTime.Now - _tickStart).TotalMilliseconds / TickMs, 0, 1);
        SetSpeedText(1 - Math.Pow(1 - t, 3));
        if (t >= 1)
        {
            _tickTimer.Stop();
        }
    }

    private void SetSpeedText(double progress)
    {
        _upShown = _upFrom + ((_upTo - _upFrom) * progress);
        _downShown = _downFrom + ((_downTo - _downFrom) * progress);
        txtSpeedUp.Text = $"{Utils.HumanFy((long)Math.Round(_upShown))}/s";
        txtSpeedDown.Text = $"{Utils.HumanFy((long)Math.Round(_downShown))}/s";
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
