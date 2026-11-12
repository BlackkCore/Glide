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
        chkTun.IsCheckedChanged += ChkTun_IsCheckedChanged;
        // The tray's Connect / Disconnect does exactly what the big button does (TUN or Windows proxy).
        ToggleRequested += () => Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                if (_hasServer)
                {
                    await SetConnectedAsync(!IsConnectedNow);
                }
            }
            catch (Exception ex)
            {
                Logging.SaveLog("StatusBarView", ex);
            }
        });
        // Ctrl+K's Connect / Disconnect: also the big button's logic, so TUN is respected both ways.
        ConnectRequested += on => Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                if (_hasServer && on != IsConnectedNow)
                {
                    await SetConnectedAsync(on);
                }
                else if (!_hasServer && on)
                {
                    AppEvents.AddServerSheetRequested.Publish();
                }
            }
            catch (Exception ex)
            {
                Logging.SaveLog("StatusBarView", ex);
            }
        });
        // Quitting while connected still keeps this session for the "Last session" cards.
        AppEvents.AppExitRequested
            .AsObservable()
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(_ =>
            {
                if (_sessionTimer.IsEnabled)
                {
                    SaveLastSession();
                }
            });
        // The shield says TUN will need a restart as administrator first.
        iconTunAdmin.IsVisible = Utils.IsWindows() && !Utils.IsAdministrator();
        // A one-time tip under the checkbox explains what TUN is for.
        bdTunTip.IsVisible = !File.Exists(TunTipSeenMarker);
        btnTunTipOk.Click += (_, _) => DismissTunTip();
        btnStatusAction.Click += BtnStatusAction_Click;
        btnDiagnose.Click += (_, _) => DiagnoseSheet.Open(ref _diagnoseSheet);
        _advancedDisclosure = new AppMotion.Disclosure(spAdvanced, open => spAdvanced.IsVisible = open);
        btnAdvanced.Click += (_, _) => SetAdvancedOpen(!_advancedDisclosure.IsOpen);
        _monitor.Changed += RenderStatus;
        SizeChanged += (_, e) => UpdateHeroLayout(e.NewSize.Width);
        txtSpeedDown.Text = "—";
        txtSpeedUp.Text = "—";
        globe.YouCaption = ResUI.TbYou;
        globe.ServerCaption = ResUI.TbServerLabel;
        _sessionTimer.Tick += (_, _) => UpdateSessionTime();

        // Changing the proxy mode selects a segment, which asks to be scrolled into view and made the
        // whole column jump; the segments are always visible next to the power button anyway.
        cmbSystemProxy.AddHandler(RequestBringIntoViewEvent, (_, e) => e.Handled = true);

        this.WhenActivated(disposables =>
        {
            // The TUN checkbox is the connection mode (remembered), not TUN itself: Connect applies it.
            SetTunCheckbox(PreferTun || ViewModel.EnableTun);
            ResumeTunAfterAdminRestart();

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

            // Every core reload (TUN on/off, routing, "Check again") briefly frees the port: that is a
            // restart in progress, not a stopped engine.
            ViewModel.ReloadRequested
                .AsObservable()
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(_ => _monitor.ExpectRestart())
                .DisposeWith(disposables);

            this.WhenAnyValue(v => v.ViewModel.EnableTun)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(tun =>
                {
                    _monitor.ExpectRestart();
                    // TUN switched on from anywhere (startup, tray) means the mode is TUN.
                    if (tun)
                    {
                        SetTunCheckbox(true);
                    }
                    UpdatePowerState(ViewModel?.SystemProxySelected ?? (int)ESysProxyType.ForcedClear);
                })
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
                .Subscribe(s =>
                {
                    SetKeyValue(s, txtInboundKey, txtInboundValue);
                    UpdateAdvancedSummary();
                })
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

            void OnOriginChanged() => Dispatcher.UIThread.Post(UpdateGlobeTooltip);
            GlobeOrigin.Changed += OnOriginChanged;
            // The session clock and the speed ticker are started and stopped by the connection state, not by
            // this view being shown, so they are left alone here (stopping them froze the clock after a re-show).
            disposables.Add(new ActionDisposable(() => GlobeOrigin.Changed -= OnOriginChanged));
            UpdateGlobeTooltip();
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
            // The tray icon gets a red dot while the status card shows a problem.
            // The same red as every other error dot (the theme's AppBadDotBrush).
            var bad = Application.Current.TryGetResource("AppBadDotBrush", Application.Current.ActualThemeVariant, out var brush)
                      && brush is ISolidColorBrush solid ? solid.Color : Color.Parse("#E5484D");
            iconslist[0].Icon = _iconProblem
                ? AvaUtils.GetAppIcon(_config.SystemProxyItem.SysProxyType, bad)
                : desktop.MainWindow.Icon;
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
            SetTunCheckbox(false);
            SaveTunPreference(false);
            return password;
        }

        AppManager.Instance.LinuxSudoPwd = password;
        return password;
    }

    private readonly ConnectionMonitor _monitor = new();
    private DiagnoseSheet? _diagnoseSheet;
    private bool _hasServer;

    private static bool IsProxyOn(int selected) => selected is (int)ESysProxyType.ForcedChange or (int)ESysProxyType.Pac;

    /// <summary>Traffic goes through Glide: by the Windows proxy, by TUN, or both.</summary>
    private bool IsConnectedNow => ViewModel is { } vm && (IsProxyOn(vm.SystemProxySelected) || vm.EnableTun);

    private async void BtnPower_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (ViewModel == null)
            {
                return;
            }
            if (!_hasServer)
            {
                // No server yet: go straight to adding one instead of a toast that says so.
                AppEvents.AddServerSheetRequested.Publish();
                return;
            }
            if (!IsConnectedNow && _monitor.State == ConnState.PortConflict)
            {
                // Connecting can't work while another program holds the port: deal with that first.
                BtnStatusAction_Click(sender, e);
                return;
            }
            // Connect = send traffic through Glide the chosen way (and verify it); Disconnect = stop all of it.
            await SetConnectedAsync(!IsConnectedNow);
        }
        catch (Exception ex)
        {
            Logging.SaveLog("StatusBarView", ex);
        }
    }

    // ===== Connection mode: the TUN checkbox under Connect =====

    private static string TunPreferenceMarker => Utils.GetConfigPath("glide-prefer-tun");

    // Written just before restarting as administrator for TUN, so TUN comes on by itself afterwards.
    private static string TunAfterRestartMarker => Utils.GetConfigPath("glide-tun-after-restart");

    private static bool PreferTun => File.Exists(TunPreferenceMarker);

    private static string TunTipSeenMarker => Utils.GetConfigPath("glide-tip-tun-seen");

    private void DismissTunTip()
    {
        bdTunTip.IsVisible = false;
        try
        {
            File.WriteAllText(TunTipSeenMarker, "1");
        }
        catch
        {
            // A read-only folder only means the tip comes back next time.
        }
    }

    private bool _syncingTun;

    private static void SaveTunPreference(bool tun)
    {
        try
        {
            if (tun)
            {
                File.WriteAllText(TunPreferenceMarker, "1");
            }
            else if (File.Exists(TunPreferenceMarker))
            {
                File.Delete(TunPreferenceMarker);
            }
        }
        catch
        {
            // A read-only folder only means the choice isn't remembered.
        }
    }

    private void SetTunCheckbox(bool tun)
    {
        if (chkTun.IsChecked == tun)
        {
            return;
        }
        _syncingTun = true;
        chkTun.IsChecked = tun;
        _syncingTun = false;
    }

    private async void ChkTun_IsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (_syncingTun)
        {
            return;
        }
        try
        {
            var tun = chkTun.IsChecked == true;
            SaveTunPreference(tun);
            if (tun && bdTunTip.IsVisible)
            {
                DismissTunTip();
            }
            UpdateAdvancedSummary();
            // Already connected: switch over now (TUN <-> Windows proxy) without disconnecting.
            if (IsConnectedNow && !await SetConnectedAsync(true))
            {
                // The restart as administrator was declined: stay as before.
                SetTunCheckbox(!tun);
                SaveTunPreference(!tun);
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("StatusBarView", ex);
        }
    }

    /// <summary>
    /// Connects the way the checkbox says (TUN, or the Windows proxy), or disconnects everything.
    /// Returns false when connecting through TUN was cancelled (administrator restart declined).
    /// </summary>
    private async Task<bool> SetConnectedAsync(bool on)
    {
        if (ViewModel is not { } vm)
        {
            return false;
        }
        if (!on)
        {
            if (vm.EnableTun)
            {
                vm.EnableTun = false;
            }
            vm.SystemProxySelected = (int)ESysProxyType.ForcedClear;
            return true;
        }
        if (chkTun.IsChecked == true)
        {
            if (!vm.EnableTun)
            {
                if (!await ConfirmAdminForTunAsync())
                {
                    return false;
                }
                vm.EnableTun = true;
            }
            // TUN already carries every app; the Windows proxy isn't needed on top of it.
            if (IsProxyOn(vm.SystemProxySelected))
            {
                vm.SystemProxySelected = (int)ESysProxyType.ForcedClear;
            }
            return true;
        }
        if (vm.EnableTun)
        {
            vm.EnableTun = false;
        }
        vm.SystemProxySelected = (int)ESysProxyType.ForcedChange;
        return true;
    }

    // TUN needs administrator rights on Windows; Glide restarts elevated, and the marker turns TUN on then.
    private static async Task<bool> ConfirmAdminForTunAsync()
    {
        if (!Utils.IsWindows() || Utils.IsAdministrator())
        {
            return true;
        }
        if (await UI.ShowYesNo(ResUI.TipTunNeedsAdmin, ResUI.menuRebootAsAdmin, ResUI.TbCancel) != ButtonResult.Yes)
        {
            return false;
        }
        try
        {
            File.WriteAllText(TunAfterRestartMarker, "1");
        }
        catch
        {
            // Without the marker TUN simply isn't switched on after the restart.
        }
        // Restart here rather than through EnableTun, so a declined Windows prompt can be handled:
        // Glide then keeps running as it was, with TUN off.
        if (await AppManager.Instance.RebootAsAdmin())
        {
            return false; // This copy is exiting; the elevated one turns TUN on.
        }
        try
        {
            File.Delete(TunAfterRestartMarker);
        }
        catch
        {
            // A leftover marker only matters on an elevated start, and is removed then.
        }
        NoticeManager.Instance.Enqueue(ResUI.TipAdminRestartCancelled);
        return false;
    }

    private static bool _tunResumeChecked;

    private void ResumeTunAfterAdminRestart()
    {
        if (_tunResumeChecked || ViewModel is not { } vm)
        {
            return;
        }
        _tunResumeChecked = true;
        try
        {
            if (!File.Exists(TunAfterRestartMarker))
            {
                return;
            }
            File.Delete(TunAfterRestartMarker);
            if (Utils.IsWindows() && Utils.IsAdministrator())
            {
                SetTunCheckbox(true);
                SaveTunPreference(true);
                vm.EnableTun = true;
                // TUN carries every app now; the Windows proxy from before the restart isn't needed.
                if (IsProxyOn(vm.SystemProxySelected))
                {
                    vm.SystemProxySelected = (int)ESysProxyType.ForcedClear;
                }
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("StatusBarView", ex);
        }
    }

    private async void BtnStatusAction_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            switch (_monitor.State)
            {
                case ConnState.PortConflict when _monitor.Conflict is { } owner && ConnectionMonitor.CanClose(owner):
                    var message = string.Format(ResUI.TipConfirmCloseApp, owner.Name, owner.Path);
                    // Say exactly what else closes with it (only that client's own proxy programs).
                    var companions = await Task.Run(() => ConnectionMonitor.CompanionNames(owner));
                    if (companions.Count > 0)
                    {
                        message += "\n\n" + string.Format(ResUI.TipAlsoClosing, string.Join(", ", companions.Select(Ltr)));
                    }
                    if (await UI.ShowYesNo(message, string.Format(ResUI.TbCloseApp, owner.Name), ResUI.TbCancel) != ButtonResult.Yes)
                    {
                        return;
                    }
                    await Task.Run(() => ConnectionMonitor.CloseOwner(owner));
                    ViewModel?.ReloadRequested.Publish();
                    _monitor.CheckSoon(2500);
                    break;

                case ConnState.EngineStopped or ConnState.NotConnected when _missingGeo.Count > 0:
                    await DownloadSiteListsAsync();
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

    private bool _advancedAutoOpened;
    private readonly AppMotion.Disclosure _advancedDisclosure;

    private void SetAdvancedOpen(bool open)
    {
        _advancedDisclosure.SetOpen(open, IsLoaded);
        btnAdvanced.Classes.Set("open", open);
    }

    // The closed Advanced row still says what's set: "Windows proxy: On · mixed:10808".
    private void UpdateAdvancedSummary()
    {
        var mode = (cmbSystemProxy.SelectedItem as ListBoxItem)?.Content as string;
        var port = txtInboundValue.Text;
        var parts = new List<string>();
        if (ViewModel?.EnableTun == true)
        {
            parts.Add("TUN");
        }
        if (mode.IsNotEmpty())
        {
            parts.Add($"{ResUI.menuSystemproxy}: {mode}");
        }
        if (port.IsNotEmpty())
        {
            // Left-to-right marks keep "mixed:10808" in order inside Persian text.
            parts.Add($"‎{port}‎");
        }
        txtAdvancedSummary.Text = string.Join(" · ", parts);
        txtAdvancedSummary.IsVisible = parts.Count > 0;
        ToolTip.SetTip(btnAdvanced, parts.Count > 0
            ? $"{ResUI.TipAdvancedConnection}\n{txtAdvancedSummary.Text}"
            : ResUI.TipAdvancedConnection);
    }

    private bool? _heroNarrow;

    // Wide: text beside the globe. Narrow: the text and the Connect button come first and a smaller globe
    // follows, so the main button is never pushed below the fold.
    private void UpdateHeroLayout(double width)
    {
        var narrow = width < 620;
        // The headline and the globe follow the room they have, so neither is cut off or oversized.
        txtHeadline.FontSize = narrow ? 34 : width >= 900 ? 46 : width >= 760 ? 40 : 36;
        Dispatcher.UIThread.Post(BalanceHeadline, DispatcherPriority.Render);
        if (narrow)
        {
            globe.Width = globe.Height = Math.Clamp(width - 8, 200, 250);
        }
        else
        {
            var textColumn = width >= 760 ? 380.0 : 340.0;
            globe.Width = globe.Height = Math.Clamp(width - textColumn - 12, 200, 400);
        }
        if (_heroNarrow == narrow && gridHero.ColumnDefinitions.Count > 0)
        {
            if (!narrow)
            {
                gridHero.ColumnDefinitions[0].Width = new GridLength(width >= 760 ? 380 : 340);
            }
            return;
        }
        _heroNarrow = narrow;
        if (narrow)
        {
            gridHero.ColumnDefinitions = new ColumnDefinitions("*");
            gridHero.RowDefinitions = new RowDefinitions("Auto,Auto");
            Grid.SetRow(spHeroText, 0);
            Grid.SetRow(pnlGlobe, 1);
            Grid.SetColumn(pnlGlobe, 0);
            pnlGlobe.Margin = new Thickness(0, 18, 0, 0);
            gridHero.MinHeight = 0;
        }
        else
        {
            gridHero.RowDefinitions = new RowDefinitions();
            gridHero.ColumnDefinitions = new ColumnDefinitions(width >= 760 ? "380,*" : "340,*");
            Grid.SetRow(pnlGlobe, 0);
            Grid.SetColumn(pnlGlobe, 1);
            Grid.SetRow(spHeroText, 0);
            pnlGlobe.Margin = default;
            gridHero.MinHeight = 0;
        }
    }

    private void UpdatePowerState(int selected)
    {
        // Unchanged and PAC are set from Advanced: show it open so the choice isn't hidden.
        if (!_advancedAutoOpened && selected is (int)ESysProxyType.Unchanged or (int)ESysProxyType.Pac)
        {
            _advancedAutoOpened = true;
            SetAdvancedOpen(true);
        }
        UpdateAdvancedSummary();
        // On while traffic goes through Glide either way: the Windows proxy or TUN.
        var isOn = IsProxyOn(selected) || (ViewModel?.EnableTun ?? false);
        TrayConnectedChanged?.Invoke(isOn);
        btnPower.Classes.Set("on", isOn);
        txtPowerLabel.Text = isOn ? ResUI.TbDisconnect : ResUI.TbConnect;
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
        _monitor.Update(_hasServer, inUse, txtServerName.Text ?? string.Empty, _config.IndexId);
        RenderStatus();
    }

    // Pill, detail line and action button for the current connection state.
    private bool _exitIpOffered;
    private bool _iconProblem;

    // Site lists ("geosite:ir") the routing or DNS needs but geosite.dat / geoip.dat lack. Xray won't
    // start without them, so the card names them and offers to download the lists.
    private List<string> _missingGeo = [];
    private bool _geoChecked;
    private bool _geoDownloading;

    private async Task RefreshMissingGeoAsync()
    {
        try
        {
            var missing = await GeoDataCheck.FindMissingAsync(AppManager.Instance.Config);
            if (_monitor.State is ConnState.EngineStopped or ConnState.NotConnected)
            {
                _missingGeo = missing;
                RenderStatus();
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("RefreshMissingGeo", ex);
        }
    }

    private async Task DownloadSiteListsAsync()
    {
        if (_geoDownloading)
        {
            return;
        }
        _geoDownloading = true;
        // Reloading the engine is for an engine that stopped because of the missing lists; before the first connection nothing is started.
        var wasStopped = _monitor.State == ConnState.EngineStopped;
        List<string> lookupFailures = [];
        btnStatusAction.IsEnabled = false;
        btnStatusAction.Content = ResUI.Downloading;
        try
        {
            // Downloads go straight to the source while Glide's own engine is down.
            using var downloadService = await ServiceLib.Services.DownloadService.CreateDownloadServiceWithCheck();
            await new ServiceLib.Services.UpdateService(downloadService, AppManager.Instance.Config, (ok, msg) =>
            {
                Logging.SaveLog($"SiteLists: {msg}");
                if (!ok && msg.StartsWith("Could not look up", StringComparison.Ordinal))
                {
                    lookupFailures.Add(msg);
                }
                return Task.CompletedTask;
            }).UpdateGeoFileAll();
            var stillMissing = await GeoDataCheck.FindMissingAsync(AppManager.Instance.Config);
            NoticeManager.Instance.Enqueue(stillMissing.Count == 0
                ? (wasStopped ? ResUI.TipSiteListsReady : ResUI.TipRoutingDataReady)
                : string.Format(ResUI.TipSiteListsStillMissing, string.Join(", ", stillMissing)));
            if (stillMissing.Count > 0 && lookupFailures.Count > 0)
            {
                // Offline or DNS trouble: say so plainly (the reason names the host and says it is the network).
                NoticeManager.Instance.Enqueue(lookupFailures[0]);
            }
            _missingGeo = stillMissing;
            if (stillMissing.Count == 0)
            {
                if (wasStopped)
                {
                    ViewModel?.ReloadRequested.Publish();
                    _monitor.CheckSoon(2500);
                }
                else
                {
                    _monitor.CheckSoon(100);
                }
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("DownloadSiteLists", ex);
            NoticeManager.Instance.Enqueue(ResUI.TipSiteListsFailed);
        }
        finally
        {
            _geoDownloading = false;
            btnStatusAction.IsEnabled = true;
            RenderStatus();
        }
    }

    // The exit IP needs an IP lookup service, which is off by default (a lookup contacts that site).
    // The first time the connection works, ask once instead of hiding it in advanced settings.
    private async Task OfferExitIpAsync()
    {
        if (_exitIpOffered)
        {
            return;
        }
        _exitIpOffered = true;
        try
        {
            var config = AppManager.Instance.Config;
            var marker = Utils.GetConfigPath("glide-exitip-asked");
            if (config.SpeedTestItem.IPAPIUrl.IsNotEmpty() || File.Exists(marker))
            {
                return;
            }
            File.WriteAllText(marker, "");
            if (await UI.ShowYesNo(ResUI.TipExitIpOffer, ResUI.TbShowExitIp, ResUI.TbNotNow) != ButtonResult.Yes)
            {
                return;
            }
            config.SpeedTestItem.IPAPIUrl = Global.IPAPIUrls[0];
            await ConfigHandler.SaveConfig(config);
            _monitor.CheckSoon(100);
        }
        catch (Exception ex)
        {
            Logging.SaveLog("OfferExitIp", ex);
        }
    }

    private void RenderStatus()
    {
        var selected = ViewModel?.SystemProxySelected ?? (int)ESysProxyType.ForcedClear;
        var state = _monitor.State;
        // Each time the engine is found stopped, or idle before the first connection, check once whether a missing
        // site list is the reason (or will be: Glide ships without the IP-to-country lists and downloads them).
        if (state is not (ConnState.EngineStopped or ConnState.NotConnected))
        {
            _geoChecked = false;
            _missingGeo = [];
        }
        else if (!_geoChecked)
        {
            _geoChecked = true;
            RefreshMissingGeoAsync().Forget(nameof(RefreshMissingGeoAsync));
        }
        var geoMissing = state is ConnState.EngineStopped or ConnState.NotConnected && _missingGeo.Count > 0;
        UpdateGlobeDestination();
        bdPowerState.Classes.Set("on", state == ConnState.Connected);
        bdPowerState.Classes.Set("warn", state == ConnState.Checking);
        bdPowerState.Classes.Set("bad", state is ConnState.NoInternet or ConnState.PortConflict or ConnState.EngineStopped);

        _powerStateLabel = state switch
        {
            ConnState.NoServer => ResUI.TbNoServerSelected,
            ConnState.Checking => ResUI.TbStatusChecking,
            // Left-to-right marks keep "234 ms" in order inside a right-to-left (Persian) label.
            ConnState.Connected => _monitor.DelayMs > 0 ? $"{ResUI.TbStatusConnected} · ‎{UiDigits.Localize(_monitor.DelayMs.ToString(CultureInfo.InvariantCulture))} ms‎" : ResUI.TbStatusConnected,
            ConnState.NoInternet => ResUI.TbStatusNoInternet,
            ConnState.PortConflict => ResUI.TbStatusPortBusy,
            ConnState.EngineStopped => ResUI.TbStatusEngineStopped,
            _ => selected == (int)ESysProxyType.Unchanged ? ResUI.TbSysProxyUnchanged : ResUI.TbStatusNotConnected,
        };

        // The big line says the state in a word; the pill above it keeps the detail (delay, timer).
        // In every other state the headline already says the same words, so the pill only shows while
        // connected, where it adds the delay ("Connected · 48 ms").
        RefreshHeadline();
        bdPowerState.IsVisible = state == ConnState.Connected;
        if (state != _lastRenderedState && IsLoaded)
        {
            AppMotion.PlayEntrance(txtHeadline, 0, AppMotion.FastMs, initialOpacity: 0.6);
            AppMotion.PlayEntrance(bdPowerState, 0, AppMotion.FastMs);
        }
        txtPingValue.Text = state == ConnState.Connected && _monitor.DelayMs > 0
            ? $"{UiDigits.Localize(_monitor.DelayMs.ToString(CultureInfo.InvariantCulture))} ms"
            : "—";
        // Distinguish live samples from the last session with the dots, while keeping the values readable.
        spSpeed.Classes.Set("inactiveStats", state != ConnState.Connected);
        var delayLevel = state == ConnState.Connected && _monitor.DelayMs > 0
            ? Converters.DelayColorConverter.Level(_monitor.DelayMs) : 0;
        dotPing.Classes.Set("fast", delayLevel == 1);
        dotPing.Classes.Set("slow", delayLevel == 2);
        dotPing.Classes.Set("bad", delayLevel == 3);

        txtRunningInfoDisplay.Text = state switch
        {
            ConnState.NoServer => ResUI.TipAddServerToConnect,
            ConnState.Connected => ExitLine(),
            ConnState.NoInternet => ResUI.TipNoInternetThroughServer,
            ConnState.PortConflict => string.Format(ResUI.TipPortBusy, AppManager.Instance.GetLocalPort(EInboundProtocol.socks), Ltr(_monitor.Conflict?.Name)),
            ConnState.EngineStopped when geoMissing => string.Format(ResUI.TipSiteListsMissing, string.Join(", ", _missingGeo.Select(Ltr))),
            ConnState.NotConnected when geoMissing => ResUI.TipFirstRunDataNeeded,
            ConnState.EngineStopped => ResUI.TipEngineStoppedHint,
            // One short line while idle; how the Windows proxy is set stays in Advanced's summary.
            ConnState.NotConnected => ResUI.TbSubOff,
            _ => string.Empty,
        };
        txtRunningInfoDisplay.IsVisible = txtRunningInfoDisplay.Text.IsNotEmpty();
        var exitFlag = state == ConnState.Connected && ExitCountry() is { } exitCode ? FlagImages.Get(exitCode) : null;
        imgExitFlag.Source = exitFlag;
        bdExitFlag.IsVisible = exitFlag != null && txtRunningInfoDisplay.IsVisible;
        if (state != _lastRenderedState && IsLoaded)
        {
            AppMotion.PlayEntrance(pnlExitDetails, 0, AppMotion.FastMs);
        }

        btnStatusAction.Content = state switch
        {
            _ when _geoDownloading => ResUI.Downloading,
            ConnState.EngineStopped or ConnState.NotConnected when geoMissing => ResUI.TbDownloadSiteLists,
            ConnState.PortConflict when _monitor.Conflict is { } owner && ConnectionMonitor.CanClose(owner) => string.Format(ResUI.TbCloseApp, owner.Name),
            ConnState.NoInternet or ConnState.EngineStopped => ResUI.TbCheckAgain,
            _ => null,
        };
        btnStatusAction.IsVisible = btnStatusAction.Content != null;
        if (state != _lastRenderedState && IsLoaded && btnStatusAction.IsVisible)
        {
            AppMotion.PlayEntrance(btnStatusAction, 0, AppMotion.FastMs, initialOpacity: 0.6);
        }
        if (state == ConnState.Connected)
        {
            OfferExitIpAsync().Forget(nameof(OfferExitIpAsync));
        }
        var problem = state is ConnState.NoInternet or ConnState.PortConflict or ConnState.EngineStopped;
        if (problem != _iconProblem)
        {
            _iconProblem = problem;
            RefreshIcon();
        }
        // Pressing the button can still cancel a connection while it is being checked.
        btnPower.Classes.Set("checking", state == ConnState.Checking);
        if (IsConnectedNow)
        {
            txtPowerLabel.Text = state == ConnState.Checking ? ResUI.TbConnectingNow : ResUI.TbDisconnect;
            // While checking, a click cancels: the tooltip and the name screen readers announce say so.
            var action = state == ConnState.Checking ? ResUI.TbCancelConnecting : ResUI.TbDisconnect;
            ToolTip.SetTip(btnPower, action);
            Avalonia.Automation.AutomationProperties.SetName(btnPower, action);
        }
        else
        {
            Avalonia.Automation.AutomationProperties.SetName(btnPower, ResUI.TbConnect);
        }
        if (state == ConnState.Connected && _lastRenderedState is ConnState.Checking or ConnState.NotConnected)
        {
            // A verified connection counts towards "your usual at this hour".
            UsageHabits.Record(_config.IndexId);
        }
        _lastRenderedState = state;
        btnDiagnose.IsVisible = state is ConnState.NotConnected or ConnState.NoInternet or ConnState.PortConflict or ConnState.EngineStopped;

        // The session clock runs only while the connection is verified.
        if (state == ConnState.Connected && !_sessionTimer.IsEnabled)
        {
            _sessionStart = DateTime.Now;
            _sessionDown = _sessionUp = 0;
            _lastSpeedAt = DateTime.Now;
            _sessionTimer.Start();
        }
        else if (state != ConnState.Connected && state != ConnState.NoInternet)
        {
            if (_sessionTimer.IsEnabled)
            {
                SaveLastSession();
            }
            _sessionTimer.Stop();
        }
        ShowIdleNumbers();
        UpdateSessionTime();
        TrayStatusChanged?.Invoke(state, _powerStateLabel, state == ConnState.NoServer ? string.Empty : txtServerName.Text ?? string.Empty);
    }

    private ConnState _lastRenderedState = ConnState.NotConnected;

    // ===== Last session: shown in the speed cards while not connected =====

    private sealed class LastSessionInfo
    {
        public long DurationSeconds { get; set; }
        public long DownKB { get; set; }
        public long UpKB { get; set; }
        public long EndedUnix { get; set; }
    }

    private static string LastSessionFile => Utils.GetConfigPath("glide-last-session.json");

    private double _sessionDown;
    private double _sessionUp;
    private DateTime _lastSpeedAt;
    private LastSessionInfo? _lastSession;
    private bool _lastSessionLoaded;
    private bool _showingLastSession;

    private LastSessionInfo? LastSession
    {
        get
        {
            if (!_lastSessionLoaded)
            {
                _lastSessionLoaded = true;
                try
                {
                    // Plain JSON nodes: the app's JSON helper only knows its own pre-generated types.
                    if (GlideFile.Read(LastSessionFile, text => System.Text.Json.Nodes.JsonNode.Parse(text) as System.Text.Json.Nodes.JsonObject) is { } o)
                    {
                        long Num(string k) => o[k] is { } n ? n.GetValue<long>() : 0;
                        _lastSession = new LastSessionInfo
                        {
                            DurationSeconds = Num("DurationSeconds"),
                            DownKB = Num("DownKB"),
                            UpKB = Num("UpKB"),
                            EndedUnix = Num("EndedUnix"),
                        };
                    }
                }
                catch
                {
                    _lastSession = null;
                }
            }
            return _lastSession;
        }
    }

    private void SaveLastSession()
    {
        var seconds = (long)Math.Max(0, (DateTime.Now - _sessionStart).TotalSeconds);
        if (seconds < 10)
        {
            return; // A blink of a connection isn't worth remembering.
        }
        _lastSession = new LastSessionInfo
        {
            DurationSeconds = seconds,
            DownKB = (long)Math.Min(_sessionDown, long.MaxValue / 2.0),
            UpKB = (long)Math.Min(_sessionUp, long.MaxValue / 2.0),
            EndedUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };
        _lastSessionLoaded = true;
        try
        {
            var json = new System.Text.Json.Nodes.JsonObject
            {
                ["DurationSeconds"] = _lastSession.DurationSeconds,
                ["DownKB"] = _lastSession.DownKB,
                ["UpKB"] = _lastSession.UpKB,
                ["EndedUnix"] = _lastSession.EndedUnix,
            };
            GlideFile.WriteAtomic(LastSessionFile, json.ToJsonString());
        }
        catch
        {
            // A read-only folder only means it isn't remembered.
        }
    }

    // Not connected: "Last session · 25:13" over the cards, the data it moved in Download / Upload, and the
    // selected server's last tested delay in Delay. Dashes where there is nothing to show yet.
    private void ShowIdleNumbers()
    {
        var idle = _monitor.State is ConnState.NotConnected or ConnState.NoServer or ConnState.EngineStopped or ConnState.PortConflict;
        if (!idle)
        {
            txtLastSession.IsVisible = false;
            ToolTip.SetTip(txtLastSession, null);
            // Connecting or connected: the last session's totals give way to live numbers at once.
            if (_showingLastSession)
            {
                _showingLastSession = false;
                txtSpeedDown.Text = "—";
                txtSpeedUp.Text = "—";
            }
            return;
        }
        if (LastSession is { DurationSeconds: > 0 } last)
        {
            var span = TimeSpan.FromSeconds(last.DurationSeconds);
            var clock = span.ToString(span.TotalHours >= 1 ? @"hh\:mm\:ss" : @"mm\:ss", CultureInfo.InvariantCulture);
            txtLastSession.Text = string.Format(ResUI.TbLastSession, UiDigits.Localize(clock));
            txtLastSession.IsVisible = true;
            ToolTip.SetTip(txtLastSession, ResUI.TipIdleNumbers);
            // Amounts are kept in KB, the unit the speed reports and Utils.HumanFy use.
            txtSpeedDown.Text = UiDigits.Localize(Utils.HumanFy(last.DownKB));
            txtSpeedUp.Text = UiDigits.Localize(Utils.HumanFy(last.UpKB));
            _showingLastSession = true;
        }
        else
        {
            txtLastSession.IsVisible = false;
            txtSpeedDown.Text = "—";
            txtSpeedUp.Text = "—";
        }
        ToolTip.SetTip(spSpeed, null);
        var tested = _config.IndexId.IsNotEmpty() ? ProfileExManager.Instance.GetDelay(_config.IndexId) : 0;
        txtPingValue.Text = tested > 0 ? $"{UiDigits.Localize(tested.ToString(CultureInfo.InvariantCulture))} ms" : "—";
    }

    // "Exit IP: Netherlands · 203.0.113.50" instead of the raw "(NL) 203.0.113.50".
    private string ExitLine()
    {
        if (_monitor.ExitIp.IsNullOrEmpty())
        {
            return string.Empty;
        }
        var ip = ExitIpAddress();
        var country = CountryName(ExitCountry());
        if (ip.IsNullOrEmpty())
        {
            return $"{ResUI.TbExitIp}: {Ltr(_monitor.ExitIp)}";
        }
        return country.IsNotEmpty()
            ? $"{ResUI.TbExitIp}: {country} · {Ltr(ip)}"
            : $"{ResUI.TbExitIp}: {Ltr(ip)}";
    }

    // Latin names and addresses inside a Persian sentence are isolated, so the sentence around
    // them keeps its order instead of the names pulling punctuation and neighbours out of place.
    private static string Ltr(string? text)
        => string.IsNullOrEmpty(text) ? string.Empty : $"⁦{text}⁩";

    /// <summary>The status line at the top of the tray menu follows the card: state, label, server.</summary>
    public static event Action<ConnState, string, string>? TrayStatusChanged;

    /// <summary>True while traffic goes through Glide (Windows proxy or TUN): the tray's Connect item follows it.</summary>
    public static event Action<bool>? TrayConnectedChanged;

    private static event Action? ToggleRequested;

    /// <summary>Connect or disconnect from the tray, the same way as the big button.</summary>
    public static void RequestToggleConnection() => ToggleRequested?.Invoke();

    private static event Action<bool>? ConnectRequested;

    /// <summary>Connect (true) or disconnect (false) the same way as the big button, TUN included.</summary>
    public static void RequestConnection(bool on) => ConnectRequested?.Invoke(on);

    private readonly DispatcherTimer _sessionTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private DateTime _sessionStart;
    private string _powerStateLabel = string.Empty;

    private void UpdateSessionTime()
    {
        if (!_sessionTimer.IsEnabled || _monitor.State != ConnState.Connected)
        {
            txtPowerState.Text = _powerStateLabel;
            spSession.IsVisible = false;
            return;
        }
        var elapsed = DateTime.Now - _sessionStart;
        txtPowerState.Text = _powerStateLabel;
        txtSessionClock.Text = UiDigits.Localize(elapsed.ToString(elapsed.TotalHours >= 1 ? @"hh\:mm\:ss" : @"mm\:ss", CultureInfo.InvariantCulture));
        spSession.IsVisible = true;
    }

    private string? _countryName;
    // The country the server's name points to (its flag or country word); only a fallback.
    private string? _nameCountry;

    private static string? CountryName(string? code) => code.IsNullOrEmpty() ? null : CountryNames.Get(code);

    // The exit check answers like "(US) 203.0.113.7"; the two letters are the country the traffic leaves from.
    private static readonly Regex ExitCountryRegex = new(@"^\s*\(([A-Za-z]{2})\)\s*(?<ip>\S+)?", RegexOptions.Compiled);

    private string? ExitCountry()
    {
        var m = ExitCountryRegex.Match(_monitor.ExitIp ?? string.Empty);
        return m.Success ? m.Groups[1].Value.ToUpperInvariant() : null;
    }

    private string? ExitIpAddress()
    {
        var m = ExitCountryRegex.Match(_monitor.ExitIp ?? string.Empty);
        return m.Success && m.Groups["ip"].Success ? m.Groups["ip"].Value.Trim() : null;
    }

    private string _runningSummary = string.Empty;

    private void UpdateGlobeTooltip()
    {
        var lines = new List<string>(2);
        var originTip = GlobeOrigin.SourceTooltip;
        if (GlobeOrigin.Code is { } yc)
        {
            var country = CountryNames.Get(yc);
            lines.Add(originTip.IsNotEmpty()
                ? $"{ResUI.TbYou}: {country} ({originTip})"
                : $"{ResUI.TbYou}: {country}");
        }
        else if (originTip.IsNotEmpty())
        {
            lines.Add(originTip);
        }
        if (_hasServer && _runningSummary.IsNotEmpty())
        {
            lines.Add(_runningSummary);
        }
        ToolTip.SetTip(pnlGlobe, lines.Count > 0 ? string.Join("\n", lines) : null);
    }

    // Before connecting the globe shows only where you are. Once connected, the other end is where the
    // traffic really leaves from (the exit IP), whatever the server is called; the name is the fallback.
    private void UpdateGlobeDestination()
    {
        // While the connection is being checked, or has dropped, the route still shows towards the server's own country.
        var state = _monitor.State;
        var exitCountry = ExitCountry();
        // Tell the location lookup whether TUN is on (then it must go around the tunnel through the physical
        // adapter). The exit check's real answer (never the country guessed from the server's name) lets the
        // lookup reject anything that came back through the server.
        GlobeOrigin.SetProxyState(
            ViewModel?.EnableTun ?? false,
            state == ConnState.Connected ? exitCountry : null,
            state == ConnState.Connected ? ExitIpAddress() : null);

        var code = state == ConnState.Connected ? (exitCountry ?? _nameCountry)
            : state is ConnState.Checking or ConnState.NoInternet ? _nameCountry
            : null;
        globe.SetConnection(state, code, code == null ? null : FlagImages.Get(code));
        _countryName = CountryName(code);
        RefreshHeadline();
        UpdateGlobeTooltip();
    }

    // "Connected to the Netherlands": English gets its article, Persian names take none.
    private static string HeadlinePlace(string name)
        => Global.ResolveUiLanguage(AppManager.Instance.Config?.UiItem?.CurrentLanguage) == "en" ? CountryGrammar.WithArticle(name) : name;

    // The big line: the country you are connected to, the invitation when off, else the state.
    private void RefreshHeadline()
    {
        _headlineText = _monitor.State switch
        {
            ConnState.Connected => _countryName.IsNotEmpty() ? string.Format(ResUI.TbHeadlineOn, HeadlinePlace(_countryName!)) : ResUI.TbStatusConnected,
            ConnState.NotConnected => ResUI.TbHeadlineOff,
            _ => _powerStateLabel,
        };
        BalanceHeadline();
    }

    private string _headlineText = string.Empty;

    // When the headline needs two lines, break it where both lines come out about as long as each other
    // ("Ready when / you are"), instead of leaving one word alone on the second line.
    private void BalanceHeadline()
    {
        var text = _headlineText;
        var avail = txtHeadline.Bounds.Width > 0 ? txtHeadline.Bounds.Width : spHeroText.Bounds.Width;
        if (avail <= 0 || !text.Contains(' '))
        {
            txtHeadline.Text = text;
            return;
        }
        var typeface = new Typeface(txtHeadline.FontFamily, txtHeadline.FontStyle, txtHeadline.FontWeight);
        double Width(string s) => new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, txtHeadline.FontSize, null).WidthIncludingTrailingWhitespace;
        if (Width(text) <= avail)
        {
            txtHeadline.Text = text;
            return;
        }
        var words = text.Split(' ');
        string? best = null;
        var bestDiff = double.MaxValue;
        for (var i = 1; i < words.Length; i++)
        {
            var first = string.Join(' ', words[..i]);
            var second = string.Join(' ', words[i..]);
            var w1 = Width(first);
            var w2 = Width(second);
            if (w1 <= avail && w2 <= avail && Math.Abs(w1 - w2) < bestDiff)
            {
                bestDiff = Math.Abs(w1 - w2);
                best = first + "\n" + second;
            }
        }
        // No even two-line split fits (a long country name): let the text wrap on its own.
        txtHeadline.Text = best ?? text;
    }

    private void UpdateRunningServer(string? summary)
    {
        summary ??= string.Empty;
        _runningSummary = summary;
        _hasServer = summary.IsNotEmpty() && summary != ResUI.CheckServerSettings;
        spRunningServer.SetValue(ToolTip.TipProperty, _hasServer ? summary : null);

        var match = SummaryRegex.Match(summary);
        // With no server the headline already says so; a second "No server selected" line only repeats it.
        spRunningServer.IsVisible = _hasServer || match.Success;
        if (!match.Success)
        {
            txtServerName.Text = _hasServer ? summary : ResUI.TbNoServerSelected;
            bdServerType.IsVisible = false;
            bdServerCountry.IsVisible = false;
            bdServerFlag.IsVisible = false;
            _nameCountry = null;
            UpdateGlobeDestination();
            txtServerAddress.Text = string.Empty;
            UpdateMonitor();
            return;
        }

        var name = match.Groups["name"].Value.Trim();
        var country = CountryDetector.Detect(name);
        ShowServerCountry(country);
        if (country == null)
        {
            // The name doesn't say where it is: try its address (literal IPs, looked up offline).
            ServerCountryFromAddressAsync(summary).Forget(nameof(ServerCountryFromAddressAsync));
        }
        name = CountryDetector.RemoveFlags(name);
        txtServerName.Text = name.IsNullOrEmpty() ? summary : name;
        txtServerType.Text = Converters.ServerLineConverter.TypeName(match.Groups["type"].Value, match.Groups["name"].Value);
        bdServerType.IsVisible = true;
        txtServerAddress.Text = match.Groups["addr"].Value;
        UpdateMonitor();
    }

    // A round flag when there is one drawn for the country, else the two-letter code.
    private void ShowServerCountry(string? country)
    {
        var flag = FlagImages.Get(country);
        _nameCountry = country;
        UpdateGlobeDestination();
        imgServerFlag.Source = flag;
        bdServerFlag.IsVisible = flag != null;
        ToolTip.SetTip(bdServerFlag, country);
        bdServerCountry.IsVisible = country != null && flag == null;
        txtServerCountry.Text = country;
    }

    private async Task ServerCountryFromAddressAsync(string summary)
    {
        try
        {
            if (await AppManager.Instance.GetProfileItem(_config.IndexId) is not { } server)
            {
                return;
            }
            var code = await Task.Run(() => GeoIpCountry.Lookup(server.Address));
            // Only if the same server is still the chosen one.
            if (code != null && _runningSummary == summary)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (_runningSummary == summary)
                    {
                        ShowServerCountry(code);
                    }
                });
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("StatusBarView", ex);
        }
    }

    private void UpdateSpeed(string? proxy, string? direct)
    {
        if (_monitor.State is not (ConnState.Checking or ConnState.Connected or ConnState.NoInternet))
        {
            return;
        }
        var p = SpeedRegex.Match(proxy ?? string.Empty);
        // The three cards stay up while idle, so the layout doesn't jump when traffic starts.
        spSpeed.IsVisible = true;
        if (!p.Success)
        {
            txtSpeedDown.Text = "—";
            txtSpeedUp.Text = "—";
            ToolTip.SetTip(spSpeed, null);
            ShowIdleNumbers();
            return;
        }
        var up = SpeedSize.ParseSize(p.Groups["up"].Value);
        var down = SpeedSize.ParseSize(p.Groups["down"].Value);
        // Add up what this session moved (rate x time since the last report) for "Last session" later.
        if (_monitor.State == ConnState.Connected && _sessionTimer.IsEnabled)
        {
            var seconds = Math.Clamp((DateTime.Now - _lastSpeedAt).TotalSeconds, 0, 5);
            _sessionDown += down * seconds;
            _sessionUp += up * seconds;
        }
        _lastSpeedAt = DateTime.Now;
        txtSpeedUp.Text = UiDigits.Localize($"{Utils.HumanFy((long)Math.Round(up))}/s");
        txtSpeedDown.Text = UiDigits.Localize($"{Utils.HumanFy((long)Math.Round(down))}/s");

        // Traffic that skips the proxy is kept out of the cards; hovering them shows it.
        var d = SpeedRegex.Match(direct ?? string.Empty);
        ToolTip.SetTip(spSpeed, d.Success
            ? $"{d.Groups["tag"].Value}   ↑ {d.Groups["up"].Value.Trim()}   ↓ {d.Groups["down"].Value.Trim()}"
            : null);
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
