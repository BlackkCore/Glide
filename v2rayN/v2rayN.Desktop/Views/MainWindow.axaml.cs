using Avalonia.Controls.Notifications;
using DialogHostAvalonia;
using v2rayN.Desktop.Base;
using v2rayN.Desktop.Common;
using v2rayN.Desktop.Manager;

namespace v2rayN.Desktop.Views;

public partial class MainWindow : WindowBase<MainWindowViewModel>
{
    private static Config _config;
    private readonly SingleReplaceableDisposable _layoutBindingsDisposable = new();
    private readonly WindowNotificationManager? _manager;
    private CheckUpdateView? _checkUpdateView;
    private BackupAndRestoreView? _backupAndRestoreView;
    private AddServerSheet? _addServerSheet;
    private bool _blCloseByUser = false;

    public MainWindow()
    {
        InitializeComponent();

        _config = AppManager.Instance.Config;
        // Toasts sit at the bottom right, over the log area, so they never cover the server toolbar
        // (Add server, Test all) the way the top-right corner did.
        _manager = new WindowNotificationManager(TopLevel.GetTopLevel(this))
        {
            MaxItems = 3,
            Position = NotificationPosition.BottomRight,
            Margin = new Thickness(16, 0, 24, 24),
        };

        KeyDown += MainWindow_KeyDown;
        // Bigger tap targets while the window is used with a finger, normal ones with a mouse.
        AddHandler(PointerPressedEvent, (_, e) => Classes.Set("touch", e.Pointer.Type == PointerType.Touch),
            RoutingStrategies.Tunnel, handledEventsToo: true);
        menuSettingsSetUWP.Click += MenuSettingsSetUWP_Click;
        menuPromotion.Click += MenuPromotion_Click;
        menuCheckUpdate.Click += MenuCheckUpdate_Click;
        // Subscriptions: one "update all" and one "update this", with the proxy as a remembered checkbox.
        menuSubViaProxy.IsChecked = File.Exists(SubViaProxyFile);
        menuSubViaProxy.Click += (_, _) =>
        {
            try
            {
                if (menuSubViaProxy.IsChecked)
                {
                    File.WriteAllText(SubViaProxyFile, "1");
                }
                else if (File.Exists(SubViaProxyFile))
                {
                    File.Delete(SubViaProxyFile);
                }
            }
            catch (Exception ex)
            {
                Logging.SaveLog("SubViaProxy", ex);
            }
        };
        menuSubUpdate.Click += (_, _) => (menuSubViaProxy.IsChecked ? ViewModel?.SubUpdateViaProxyCmd : ViewModel?.SubUpdateCmd)?.Execute().Subscribe();
        menuSubGroupUpdate.Click += (_, _) => (menuSubViaProxy.IsChecked ? ViewModel?.SubGroupUpdateViaProxyCmd : ViewModel?.SubGroupUpdateCmd)?.Execute().Subscribe();
        menuAppearance.Click += (_, _) => (conTheme.Content as ThemeSettingView)?.OpenAt(menuNavSettings);
        SetSubmenuIcons();
        menuBackupAndRestore.Click += MenuBackupAndRestore_Click;
        menuAddServerSheet.Click += (_, _) => ShowAddServerSheet();
        menuAppRouting.Click += async (_, _) => await ShowAppRoutingAsync();
        Activated += async (_, _) => await OfferClipboardAsync();
        menuDataUsage.Click += async (_, _) => await ShowDataUsageAsync();
        menuExportDiagnostics.Click += async (_, _) =>
        {
            try
            {
                var path = await UI.SaveFileDialog("zip");
                if (path.IsNullOrEmpty())
                {
                    return;
                }
                if (!path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    path += ".zip";
                }
                await Task.Run(() => DiagnosticsExport.Create(path));
                NoticeManager.Instance.Enqueue(string.Format(ResUI.TipDiagnosticsSaved, path));
            }
            catch (Exception ex)
            {
                Logging.SaveLog("ExportDiagnostics", ex);
                NoticeManager.Instance.Enqueue(ResUI.OperationFailed);
            }
        };
        // Daily data limit (Data usage): one warning a day, checked every minute.
        var usageTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        usageTimer.Tick += (_, _) => CheckUsageLimit();
        usageTimer.Start();
        menuImportFromOriginal.Click += async (_, _) => await ImportFromOriginalAsync(true);
        menuClose.Click += MenuClose_Click;
        btnToggleLogs.Click += (_, _) => SetLogsHidden(!_logsHidden);

        conTheme.Content ??= new ThemeSettingView();
        if (conTheme.Content is ThemeSettingView appearance)
        {
            appearance.ThemeRequested += (theme, origin) => PlayThemeReveal(() => SetTheme(theme), origin);
        }
        btnThemeLight.Click += (_, _) => PlayThemeReveal(() => SetTheme(ETheme.Light), btnThemeLight);
        btnThemeDark.Click += (_, _) => PlayThemeReveal(() => SetTheme(ETheme.Dark), btnThemeDark);
        btnThemeAuto.Click += (_, _) => PlayThemeReveal(() => SetTheme(ETheme.FollowSystem), btnThemeAuto);
        btnThemeCompact.Click += (_, _) => PlayThemeReveal(() => SetTheme(IsDarkTheme() ? ETheme.Light : ETheme.Dark), btnThemeCompact);
        ActualThemeVariantChanged += (_, _) => UpdateThemeToggle();
        UpdateThemeToggle();

        if (Utils.IsWindows())
        {
            // Windows 11 style: the sidebar and cards run up under the system caption buttons,
            // and the empty strip along the top drags the window.
            ExtendClientAreaToDecorationsHint = true;
            ExtendClientAreaTitleBarHeightHint = bdTitleBar.Height;
            bdTitleBar.IsVisible = true;
            Avalonia.Controls.Chrome.WindowDecorationProperties.SetElementRole(bdTitleBar, WindowDecorationsElementRole.TitleBar);
            _titleBarInset = 16;
        }

        txtVersion.Text = $"v{Utils.GetVersionInfo()}";
        Opened += (_, _) => PlayCardEntrance();
        SizeChanged += (_, e) => UpdateResponsiveLayout(e.NewSize.Width);

        this.WhenActivated(disposables =>
        {
            //servers
            this.BindCommand(ViewModel, vm => vm.AddVmessServerCmd, v => v.menuAddVmessServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddVlessServerCmd, v => v.menuAddVlessServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddShadowsocksServerCmd, v => v.menuAddShadowsocksServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddSocksServerCmd, v => v.menuAddSocksServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddHttpServerCmd, v => v.menuAddHttpServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddTrojanServerCmd, v => v.menuAddTrojanServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddHysteria2ServerCmd, v => v.menuAddHysteria2Server).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddTuicServerCmd, v => v.menuAddTuicServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddWireguardServerCmd, v => v.menuAddWireguardServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddAnytlsServerCmd, v => v.menuAddAnytlsServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddNaiveServerCmd, v => v.menuAddNaiveServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddMasqueServerCmd, v => v.menuAddMasqueServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddCustomServerCmd, v => v.menuAddCustomServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddCustomOutboundServerCmd, v => v.menuAddCustomOutboundServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddPolicyGroupServerCmd, v => v.menuAddPolicyGroupServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddProxyChainServerCmd, v => v.menuAddProxyChainServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddServerViaClipboardCmd, v => v.menuAddServerViaClipboard).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddServerViaScanCmd, v => v.menuAddServerViaScan).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddServerViaImageCmd, v => v.menuAddServerViaImage).DisposeWith(disposables);

            //sub
            this.BindCommand(ViewModel, vm => vm.SubSettingCmd, v => v.menuSubSetting).DisposeWith(disposables);

            //setting
            this.BindCommand(ViewModel, vm => vm.OptionSettingCmd, v => v.menuOptionSetting).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RoutingSettingCmd, v => v.menuRoutingSetting).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.DNSSettingCmd, v => v.menuDNSSetting).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.FullConfigTemplateCmd, v => v.menuFullConfigTemplate).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.GlobalHotkeySettingCmd, v => v.menuGlobalHotkeySetting).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RebootAsAdminCmd, v => v.menuRebootAsAdmin).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.ClearServerStatisticsCmd, v => v.menuClearServerStatistics).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.OpenTheFileLocationCmd, v => v.menuOpenTheFileLocation).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RegionalPresetDefaultCmd, v => v.menuRegionalPresetsDefault).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RegionalPresetRussiaCmd, v => v.menuRegionalPresetsRussia).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RegionalPresetIranCmd, v => v.menuRegionalPresetsIran).DisposeWith(disposables);

            this.BindCommand(ViewModel, vm => vm.ReloadCmd, v => v.menuReload).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.BlReloadEnabled, v => v.menuReload.IsEnabled).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.BlNewUpdate, v => v.dotNewUpdate.IsVisible).DisposeWith(disposables);

            this.OneWayBind(ViewModel, vm => vm.StatusBarViewModel, v => v.contentStatusBarView.Content).DisposeWith(disposables);

            _layoutBindingsDisposable.DisposeWith(disposables);

            this.WhenAnyValue(v => v.ViewModel.MainGirdOrientation)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(UpdateLayout)
                .DisposeWith(disposables);

            ViewModel.ReadTextFromClipboardInteraction.RegisterHandler(async interaction =>
            {
                var result = await AvaUtils.GetClipboardData(this);
                interaction.SetOutput(result);
            }).DisposeWith(disposables);

            ViewModel.ScanScreenInteraction.RegisterHandler(async interaction =>
            {
                ShowHideWindow(false);
                await Task.Delay(200);
                var result = QRCodeAvaloniaUtils.CaptureScreen();
                ShowHideWindow(true);
                interaction.SetOutput(result);
            }).DisposeWith(disposables);

            ViewModel.BrowseImageFileInteraction.RegisterHandler(async interaction =>
            {
                var result = await UI.OpenFileDialog(null);
                interaction.SetOutput(result);
            }).DisposeWith(disposables);

            ViewModel.ShowHideWindowInteraction.RegisterHandler(interaction =>
            {
                ShowHideWindow(interaction.Input);
                interaction.SetOutput(RxVoid.Default);
            }).DisposeWith(disposables);

            AppEvents.SendSnackMsgRequested
              .AsObservable()
              .ObserveOn(RxSchedulers.MainThreadScheduler)
              .Subscribe(async content => await DelegateSnackMsg(content))
              .DisposeWith(disposables);

            AppEvents.AppExitRequested
              .AsObservable()
              .ObserveOn(RxSchedulers.MainThreadScheduler)
              .Subscribe(_ => StorageUI())
              .DisposeWith(disposables);

            AppEvents.ShutdownRequested
              .AsObservable()
              .ObserveOn(RxSchedulers.MainThreadScheduler)
              .Subscribe(Shutdown)
              .DisposeWith(disposables);

            AppEvents.AddServerSheetRequested
              .AsObservable()
              .ObserveOn(RxSchedulers.MainThreadScheduler)
              .Subscribe(_ => ShowAddServerSheet())
              .DisposeWith(disposables);
        });

        if (Utils.IsWindows())
        {
            // Just the app's name; the version is under the logo, and admin mode is only worth a mention when on.
            Title = Utils.IsAdministrator() ? $"{Global.AppDisplayName} ({ResUI.TbAdministrator})" : Global.AppDisplayName;

            if (!Design.IsDesignMode)
            {
                ThreadPool.RegisterWaitForSingleObject(Program.ProgramStarted, OnProgramStarted, null, -1, false);
                HotkeyManager.Instance.Init(_config, OnHotkeyHandler);
            }
        }
        else
        {
            Title = Global.AppDisplayName;
            menuAddServerViaScan.IsVisible = false;
        }

        if (_config.UiItem.AutoHideStartup && Utils.IsWindows())
        {
            WindowState = WindowState.Minimized;
        }
    }

    #region Event

    private void OnProgramStarted(object state, bool timeout)
    {
        Dispatcher.UIThread.Post(() =>
                ShowHideWindow(true),
            DispatcherPriority.Default);
    }

    private async Task DelegateSnackMsg(string content)
    {
        if (TryGetRestartNoticeCulture(content, out var culture))
        {
            // Settings that only apply after a restart: offer to do it right away.
            await PromptRestartAsync(culture);
            return;
        }
        if (content == ResUI.CheckServerSettings && _config.IndexId.IsNullOrEmpty())
        {
            // No server yet: the connection card already says so and shows how to add one.
            return;
        }
        // The engine announces the server as "[VLESS] name(host:port)" or "[PolicyGroup] [Xray]⚡ Auto":
        // show "Active: name" instead of the technical summary.
        var summary = ServerSummaryRegex.Match(content);
        if (summary.Success && Enum.TryParse<EConfigType>(summary.Groups["type"].Value, out _))
        {
            content = $"{ResUI.TipActiveServer}: {summary.Groups["name"].Value.Trim()}";
        }
        _manager?.Show(new Avalonia.Controls.Notifications.Notification(null, content, NotificationType.Information));
    }

    private static readonly System.Text.RegularExpressions.Regex ServerSummaryRegex =
        new(@"^\[(?<type>[A-Za-z0-9_]+)\]\s*(?:\[[^\]]+\])?\s*(?<name>[^\r\n]+?)(\([^()]*:\d+\))?$");

    private static bool TryGetRestartNoticeCulture(string content, out CultureInfo culture)
    {
        culture = CultureInfo.CurrentUICulture;
        if (content == ResUI.NeedRebootTips)
        {
            return true;
        }
        // A language change switches the culture on a background thread, so this notice
        // can arrive in the newly chosen language rather than the window's current one.
        var language = _config.UiItem.CurrentLanguage;
        if (language.IsNullOrEmpty())
        {
            return false;
        }
        try
        {
            culture = new CultureInfo(language);
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
        return content == ResUI.ResourceManager.GetString(nameof(ResUI.NeedRebootTips), culture);
    }

    private bool _restartPromptOpen;

    private async Task PromptRestartAsync(CultureInfo culture)
    {
        if (_restartPromptOpen)
        {
            return;
        }
        _restartPromptOpen = true;
        try
        {
            var message = ResUI.ResourceManager.GetString(nameof(ResUI.TipRestartRequired), culture) ?? ResUI.TipRestartRequired;
            var restartNow = ResUI.ResourceManager.GetString(nameof(ResUI.TbRestartNow), culture) ?? ResUI.TbRestartNow;
            var later = ResUI.ResourceManager.GetString(nameof(ResUI.TbRestartLater), culture) ?? ResUI.TbRestartLater;
            if (await UI.ShowYesNo(message, restartNow, later) == ButtonResult.Yes)
            {
                _blCloseByUser = true;
                StorageUI();
                await AppManager.Instance.RestartApp();
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("PromptRestartAsync", ex);
        }
        finally
        {
            _restartPromptOpen = false;
        }
    }

    private void OnHotkeyHandler(EGlobalHotkey e)
    {
        switch (e)
        {
            case EGlobalHotkey.ShowForm:
                Dispatcher.UIThread.Post(() => ShowHideWindow(null));
                break;

            case EGlobalHotkey.SystemProxyClear:
            case EGlobalHotkey.SystemProxySet:
            case EGlobalHotkey.SystemProxyUnchanged:
            case EGlobalHotkey.SystemProxyPac:
                AppEvents.SysProxyChangeRequested.Publish((ESysProxyType)((int)e - 1));
                break;
        }
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (_blCloseByUser)
        {
            return;
        }

        Logging.SaveLog("OnClosing -> " + e.CloseReason.ToString());

        switch (e.CloseReason)
        {
            case WindowCloseReason.OwnerWindowClosing or WindowCloseReason.WindowClosing:
                e.Cancel = true;
                ShowHideWindow(false);
                break;

            case WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown:
                await AppManager.Instance.AppExitAsync(false);
                break;
        }

        base.OnClosing(e);
    }

    private async void MainWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers is KeyModifiers.Control or KeyModifiers.Meta)
        {
            switch (e.Key)
            {
                case Key.V:
                    await AddServerViaClipboardAsync();
                    break;

                case Key.S:
                    await ScanScreenTaskAsync();
                    break;

                case Key.K:
                    await ShowQuickSwitcherAsync();
                    break;
            }
        }
        else
        {
            if (e.Key == Key.F5)
            {
                ViewModel?.Reload();
            }
        }
    }

    private void MenuPromotion_Click(object? sender, RoutedEventArgs e)
    {
        ProcUtils.ProcessStart($"{Utils.Base64Decode(Global.PromotionUrl)}?t={DateTime.Now.Ticks}");
    }

    private bool IsDarkTheme()
    {
        var variant = ActualThemeVariant;
        return variant == ThemeVariant.Dark || variant?.InheritVariant == ThemeVariant.Dark;
    }

    private void UpdateThemeToggle()
    {
        var isDark = IsDarkTheme();
        // The saved choice decides which segment is lit; "Auto" follows Windows.
        var saved = (conTheme.Content as ThemeSettingView)?.ViewModel?.CurrentTheme;
        var auto = saved is null || saved == nameof(ETheme.FollowSystem) || !Enum.IsDefined(typeof(ETheme), saved);
        btnThemeAuto.Classes.Set("active", auto);
        btnThemeLight.Classes.Set("active", !auto && !isDark);
        btnThemeDark.Classes.Set("active", !auto && isDark);
        iconThemeCompactMoon.IsVisible = !isDark;
        iconThemeCompactSun.IsVisible = isDark;
    }

    private void SetTheme(ETheme theme)
    {
        if (conTheme.Content is ThemeSettingView themeView && themeView.ViewModel != null)
        {
            // Goes through the existing theme setting, so the choice is saved to the config.
            themeView.ViewModel.CurrentTheme = theme.ToString();
        }
        UpdateThemeToggle();
    }

    protected override bool AnimateContentOnOpen => false;

    private DispatcherTimer? _revealTimer;

    // Theme switch: a picture of the old theme covers the window, the theme changes underneath,
    // and the picture fades away (a crossfade).
    private RenderTargetBitmap? _revealSnapshot;

    private void PlayThemeReveal(Action applyTheme, Control origin)
    {
        var bounds = gridRoot.Bounds;
        if (!AppMotion.Enabled || !IsLoaded || bounds.Width < 1 || bounds.Height < 1)
        {
            applyTheme();
            return;
        }

        // Clicked again mid-reveal: picture exactly what is on screen now (the overlay is inside gridRoot,
        // so the half-grown circle is included) and start a fresh circle from the new click.
        _revealTimer?.Stop();
        _revealId++;
        imgThemeSnapshot.Transitions = null;
        imgThemeSnapshot.Opacity = 1;
        var wasDark = IsDarkTheme();
        var scaling = RenderScaling;
        var snapshot = new RenderTargetBitmap(
            new PixelSize((int)Math.Ceiling(bounds.Width * scaling), (int)Math.Ceiling(bounds.Height * scaling)),
            new Vector(96 * scaling, 96 * scaling));
        snapshot.Render(gridRoot);
        _revealSnapshot?.Dispose();
        _revealSnapshot = snapshot;
        imgThemeSnapshot.Source = snapshot;
        // In right-to-left layouts the picture comes back mirrored; flipping it also moves the
        // hole to the side the theme button is really on.
        imgThemeSnapshot.RenderTransform = FlowDirection == FlowDirection.RightToLeft ? new ScaleTransform(-1, 1) : null;
        imgThemeSnapshot.Clip = null;
        imgThemeSnapshot.IsVisible = true;

        Classes.Add("themeSwap");
        applyTheme();

        // Same theme as before (e.g. Light clicked while already light): nothing to reveal.
        if (IsDarkTheme() == wasDark)
        {
            EndThemeReveal();
            return;
        }

        // Wait until the new theme has been drawn once underneath (restyling the window is the slow
        // part), then let the picture of the old theme dissolve. The fade itself is a plain opacity
        // transition, so it stays smooth no matter how big the window is.
        var id = ++_revealId;
        RequestAnimationFrame(_ => RequestAnimationFrame(_ =>
        {
            if (id != _revealId)
            {
                return;
            }
            imgThemeSnapshot.Transitions =
            [
                new Avalonia.Animation.DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(300), Easing = new Avalonia.Animation.Easings.CubicEaseInOut() },
            ];
            imgThemeSnapshot.Opacity = 0;
            DispatcherTimer.RunOnce(() =>
            {
                if (id == _revealId)
                {
                    EndThemeReveal();
                }
            }, TimeSpan.FromMilliseconds(340));
        }));
    }

    private int _revealId;

    private void EndThemeReveal()
    {
        _revealTimer?.Stop();
        Classes.Remove("themeSwap");
        imgThemeSnapshot.IsVisible = false;
        imgThemeSnapshot.Transitions = null;
        imgThemeSnapshot.Opacity = 1;
        imgThemeSnapshot.Source = null;
        imgThemeSnapshot.Clip = null;
        _revealSnapshot?.Dispose();
        _revealSnapshot = null;
    }

    private DispatcherTimer? _sidebarTimer;

    // The sidebar slides between full (240) and icon-only (76) width instead of jumping.
    private void AnimateSidebarWidth(double target)
    {
        var column = gridRoot.ColumnDefinitions[0];
        _sidebarTimer?.Stop();
        var from = column.ActualWidth > 0 ? column.ActualWidth : column.Width.Value;
        if (!AppMotion.Enabled || !IsLoaded || Math.Abs(from - target) < 1)
        {
            column.Width = new GridLength(target);
            return;
        }
        var start = DateTime.Now;
        _sidebarTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _sidebarTimer.Tick += (_, _) =>
        {
            var t = Math.Clamp((DateTime.Now - start).TotalMilliseconds / 300, 0, 1);
            column.Width = new GridLength(from + ((target - from) * (1 - Math.Pow(1 - t, 3))));
            if (t >= 1)
            {
                _sidebarTimer!.Stop();
            }
        };
        _sidebarTimer.Start();
    }

    // The cards rise in one after another, left column first, top to bottom.
    private void PlayCardEntrance()
    {
        var cards = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(gridContent)
            .OfType<Border>()
            .Where(b => b.Classes.Contains("card") && b.IsEffectivelyVisible)
            .Select(b => (Card: b, Pos: b.TranslatePoint(default, gridContent) ?? default))
            .OrderBy(t => Math.Round(t.Pos.X / 40))
            .ThenBy(t => t.Pos.Y)
            .Select(t => t.Card)
            .ToList();
        for (var i = 0; i < cards.Count; i++)
        {
            AppMotion.PlayEntrance(cards[i], 18, 520, 40 + (i * 70));
        }
        gridContent.Classes.Remove("entering");
    }

    private bool? _isCompactLayout;

    // Extra top space for the cards when the caption buttons sit over the content.
    private double _titleBarInset;

    private void UpdateResponsiveLayout(double width)
    {
        // Narrow windows get an icon-only sidebar so the server list keeps its room.
        var compact = width < 1320;
        if (_isCompactLayout == compact)
        {
            return;
        }
        _isCompactLayout = compact;

        bdSidebar.Classes.Set("compact", compact);
        AnimateSidebarWidth(compact ? 76 : 240);
        gridContent.ColumnDefinitions[0].Width = new GridLength(compact ? 280 : 320);
        gridContent.ColumnDefinitions[1].Width = new GridLength(compact ? 20 : 24);
        var gap = compact ? 20 : 24;
        gridContent.Margin = new Thickness(gap, gap + _titleBarInset, gap, gap);

        foreach (var item in menuSidebar.Items.OfType<MenuItem>())
        {
            var label = (item.Header as Panel)?.Children.OfType<TextBlock>().FirstOrDefault()?.Text;
            ToolTip.SetTip(item, compact ? label : null);
        }
    }

    private void MenuSettingsSetUWP_Click(object? sender, RoutedEventArgs e)
    {
        ProcUtils.ProcessStart(Utils.GetBinPath("EnableLoopback.exe"));
    }

    public async Task AddServerViaClipboardAsync()
    {
        var clipboardData = await AvaUtils.GetClipboardData(this);
        if (clipboardData.IsNotEmpty() && ViewModel != null)
        {
            await ViewModel.AddServerViaClipboardAsync(clipboardData);
        }
    }

    public async Task ScanScreenTaskAsync()
    {
        ShowHideWindow(false);

        await Task.Delay(200);

        var bytes = QRCodeAvaloniaUtils.CaptureScreen();
        if (bytes != null && ViewModel != null)
        {
            await ViewModel.ScanScreenResult(bytes);
        }

        ShowHideWindow(true);
    }

    private void MenuCheckUpdate_Click(object? sender, RoutedEventArgs e)
    {
        _checkUpdateView ??= new CheckUpdateView();
        _checkUpdateView.ViewModel = ViewModel?.CheckUpdateViewModel;
        DialogHost.Show(_checkUpdateView);

        AppEvents.HasUpdateNotified.Publish(false);
    }

    private DateTime _usageWarnedDay;

    private void CheckUsageLimit()
    {
        try
        {
            DataUsageSheet.RecordToday();
            var limitGb = DataUsageSheet.LimitGb;
            if (limitGb <= 0 || !_config.GuiItem.EnableStatistics || _usageWarnedDay == DateTime.Today)
            {
                return;
            }
            var todayKb = DataUsageSheet.TodayKb;
            if (todayKb > limitGb * 1024 * 1024)
            {
                _usageWarnedDay = DateTime.Today;
                NoticeManager.Instance.Enqueue(string.Format(ResUI.TipUsageLimitReached, Utils.HumanFy(todayKb), limitGb));
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("CheckUsageLimit", ex);
        }
    }

    private async Task ShowDataUsageAsync()
    {
        if (DialogHost.IsDialogOpen(null))
        {
            return;
        }
        var sheet = new DataUsageSheet();
        await sheet.LoadAsync();
        DialogHost.Show(sheet);
    }

    private async Task ShowAppRoutingAsync()
    {
        if (ViewModel is not { } vm || DialogHost.IsDialogOpen(null))
        {
            return;
        }
        var sheet = new AppRoutingSheet(vm);
        await sheet.LoadAsync();
        DialogHost.Show(sheet);
    }

    private QuickSwitcherSheet? _quickSwitcher;
    private DiagnoseSheet? _diagnoseFromSwitcher;

    // Ctrl+K: servers and the everyday actions, by typing a few letters.
    private async Task ShowQuickSwitcherAsync()
    {
        if (ViewModel is not { } vm || DialogHost.IsDialogOpen(null))
        {
            return;
        }
        static Task Run(System.Windows.Input.ICommand command)
        {
            command.Execute(null);
            return Task.CompletedTask;
        }
        var action = ResUI.TbQuickAction;
        var actions = new List<QuickSwitcherSheet.Entry>
        {
            new(ResUI.TbConnect, action, () => Run(vm.StatusBarViewModel.SystemProxySetCmd)),
            new(ResUI.TbDisconnect, action, () => Run(vm.StatusBarViewModel.SystemProxyClearCmd)),
            new(ResUI.TbAddServer, action, () => { ShowAddServerSheet(); return Task.CompletedTask; }),
            new(ResUI.TbDiagnose, action, () => { DiagnoseSheet.Open(ref _diagnoseFromSwitcher); return Task.CompletedTask; }),
            new(ResUI.menuSetting, action, () => Run(vm.OptionSettingCmd)),
            new(ResUI.menuRoutingSetting, action, () => Run(vm.RoutingSettingCmd)),
            new(ResUI.menuSubSetting, action, () => Run(vm.SubSettingCmd)),
            new(ResUI.TbAppRouting, action, ShowAppRoutingAsync),
            new(ResUI.TbDataUsage, action, ShowDataUsageAsync),
            new(ResUI.menuCheckUpdate, action, () => { MenuCheckUpdate_Click(null, new RoutedEventArgs()); return Task.CompletedTask; }),
        };
        _quickSwitcher ??= new QuickSwitcherSheet();
        await _quickSwitcher.ShowAsync(vm, actions);
        DialogHost.Show(_quickSwitcher);
    }

    private void ShowAddServerSheet(string? text = null)
    {
        // Only one sheet at a time (Show throws when the host already has one).
        if (DialogHost.IsDialogOpen(null))
        {
            return;
        }
        _addServerSheet ??= new AddServerSheet();
        _addServerSheet.Main = ViewModel;
        _addServerSheet.Reset(text);
        DialogHost.Show(_addServerSheet);
    }

    // An icon at the start of each row in the sidebar's popups. Rows without one (the long list of
    // "Add [protocol]" items) keep the empty slot, so every label still lines up.
    private void SetSubmenuIcons()
    {
        var icons = new (MenuItem Item, string Key)[]
        {
            (menuAddServerSheet, "IconPlus"),
            (menuAddServerViaClipboard, "IconClipboard"),
            (menuAddServerViaScan, "IconScan"),
            (menuAddServerViaImage, "IconImage"),
            (menuAddCustomServer, "IconFileCode"),
            (menuAddPolicyGroupServer, "IconLayers"),
            (menuAddProxyChainServer, "IconChain"),
            (menuAddCustomOutboundServer, "IconOutbound"),
            (menuSubUpdate, "IconReload"),
            (menuSubGroupUpdate, "IconRss"),
            (menuSubSetting, "IconSliders"),
            (menuAppRouting, "IconApps"),
            (menuExportDiagnostics, "IconArchive"),
            (menuBackupAndRestore, "IconDatabase"),
            (menuImportFromOriginal, "IconImport"),
            (menuOpenTheFileLocation, "IconFolder"),
            (menuOptionSetting, "IconSliders"),
            (menuDNSSetting, "IconGlobe"),
            (menuFullConfigTemplate, "IconFileCode"),
            (menuGlobalHotkeySetting, "IconKeyboard"),
            (menuAppearance, "IconPalette"),
            (menuRebootAsAdmin, "IconShield"),
            (menuSettingsSetUWP, "IconReload"),
            (menuClearServerStatistics, "IconTrash"),
            (menuRegionalPresets, "IconGlobe"),
        };
        foreach (var (item, key) in icons)
        {
            if (this.TryFindResource(key, out var data) && data is Geometry geometry)
            {
                var icon = new PathIcon { Data = geometry };
                icon.Classes.Add("sub");
                item.Icon = icon;
            }
        }
    }

    private string? _clipboardOffered;

    // Present when "Update through the proxy" is ticked in the Subscriptions menu.
    private static string SubViaProxyFile => Utils.GetConfigPath("glide-sub-via-proxy");

    // A fingerprint of the last offered clipboard content, kept across restarts so the same copied
    // link isn't offered again every time Glide starts.
    private static string ClipboardOfferedFile => Utils.GetConfigPath("glide-clipboard-offered");

    /// <summary>
    /// Text that was just imported (pasted into the Add server sheet) counts as offered, so coming back
    /// to Glide, or restarting it, doesn't offer the same links again.
    /// </summary>
    public static void RememberClipboardOffered(string? text)
    {
        try
        {
            text = text?.Trim();
            if (text.IsNullOrEmpty())
            {
                return;
            }
            File.WriteAllText(ClipboardOfferedFile, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text))));
        }
        catch (Exception ex)
        {
            Logging.SaveLog("RememberClipboardOffered", ex);
        }
    }

    // Copied a server link or a subscription address? Coming back to Glide offers it in the Add
    // server sheet (with its preview; Cancel ignores it). Each clipboard content is offered once
    // (also across restarts), addresses already added are never offered, and the clipboard is
    // only read when this window is activated.
    private async Task OfferClipboardAsync()
    {
        try
        {
            if (DialogHost.IsDialogOpen(null) || !IsVisible)
            {
                return;
            }
            var text = (await AvaUtils.GetClipboardData(this))?.Trim();
            if (text.IsNullOrEmpty() || text == _clipboardOffered || text.Length > 100_000)
            {
                return;
            }
            _clipboardOffered = text;
            var fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
            if (File.Exists(ClipboardOfferedFile) && File.ReadAllText(ClipboardOfferedFile).Trim() == fingerprint)
            {
                return;
            }
            if (ImportPreview.Analyze(text).IsEmpty)
            {
                return;
            }
            File.WriteAllText(ClipboardOfferedFile, fingerprint);
            // Subscription addresses that are already added.
            var subs = await AppManager.Instance.SubItems() ?? [];
            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (lines.Length > 0 && lines.All(l => subs.Any(s => string.Equals(s.Url?.Trim(), l, StringComparison.OrdinalIgnoreCase))))
            {
                return;
            }
            ShowAddServerSheet(text);
        }
        catch (Exception ex)
        {
            Logging.SaveLog("OfferClipboard", ex);
        }
    }

    private void MenuBackupAndRestore_Click(object? sender, RoutedEventArgs e)
    {
        _backupAndRestoreView ??= new BackupAndRestoreView();
        _backupAndRestoreView.ViewModel = ViewModel?.BackupAndRestoreViewModel;
        DialogHost.Show(_backupAndRestoreView);
    }

    private async void MenuClose_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            // Only ask when quitting would cut a connection (Windows proxy or TUN going through Glide).
            var routing = _config.SystemProxyItem.SysProxyType is ESysProxyType.ForcedChange or ESysProxyType.Pac
                          || _config.TunModeItem.EnableTun;
            if (routing && await UI.ShowYesNo(ResUI.menuExitTips) != ButtonResult.Yes)
            {
                return;
            }

            _blCloseByUser = true;
            StorageUI();

            await AppManager.Instance.AppExitAsync(true);
        }
        catch
        {
            // Ignore
        }
    }

    private void Shutdown(bool obj)
    {
        if (obj is bool b && _blCloseByUser == false)
        {
            _blCloseByUser = b;
        }
        StorageUI();
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            HotkeyManager.Instance.Dispose();
            desktop.Shutdown();
        }
    }

    #endregion Event

    #region UI

    public void ShowHideWindow(bool? blShow)
    {
        var bl = blShow ??
                    (Utils.IsLinux() || Utils.IsMacOS()
                    ? (!AppManager.Instance.ShowInTaskbar ^ (WindowState == WindowState.Minimized))
                    : !AppManager.Instance.ShowInTaskbar);
        if (bl)
        {
            Show();
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }
            Activate();
            Focus();
        }
        else
        {
            if (Utils.IsLinux() && _config.UiItem.Hide2TrayWhenClose == false)
            {
                WindowState = WindowState.Minimized;
                return;
            }

            foreach (var ownedWindow in OwnedWindows)
            {
                ownedWindow.Close();
            }
            Hide();
        }

        AppManager.Instance.ShowInTaskbar = bl;
    }

    protected override void OnLoaded(object? sender, RoutedEventArgs e)
    {
        base.OnLoaded(sender, e);
        if (_config.UiItem.AutoHideStartup)
        {
            ShowHideWindow(false);
        }
        RestoreUI();
        _ = FirstRunOffersAsync();
    }

    private async Task FirstRunOffersAsync()
    {
        await OfferImportOnFirstRunAsync();
        await OfferIranPresetAsync();
    }

    // Persian users on the built-in (China-oriented) presets are asked once whether to switch to the
    // Iran preset: Iranian sites then go direct and DNS is set up for Iran. Asked once, whatever the answer.
    private async Task OfferIranPresetAsync()
    {
        try
        {
            var marker = Utils.GetConfigPath("glide-iran-offered");
            if (Design.IsDesignMode || !IsVisible || File.Exists(marker)
                || !(_config.UiItem.CurrentLanguage?.StartsWith("fa", StringComparison.OrdinalIgnoreCase) ?? false)
                || _config.ConstItem.RouteRulesTemplateSourceUrl.IsNotEmpty())
            {
                return;
            }
            await File.WriteAllTextAsync(marker, "1");
            if (await UI.ShowYesNo(ResUI.TipIranPresetOffer, ResUI.TbUseIranPreset, ResUI.TbNotNow) == ButtonResult.Yes
                && menuRegionalPresetsIran.Command is { } command && command.CanExecute(null))
            {
                command.Execute(null);
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("OfferIranPresetAsync", ex);
        }
    }

    private async Task OfferImportOnFirstRunAsync()
    {
        // A fresh install with no servers: offer to bring them over from the original v2rayN.
        try
        {
            if (Design.IsDesignMode || !IsVisible || ConfigImportHandler.IsOfferDismissed())
            {
                return;
            }
            var profiles = await AppManager.Instance.ProfileItems("");
            if (profiles is { Count: > 0 })
            {
                return;
            }
            await ImportFromOriginalAsync(false);
        }
        catch (Exception ex)
        {
            Logging.SaveLog("OfferImportOnFirstRunAsync", ex);
        }
    }

    private async Task ImportFromOriginalAsync(bool userInitiated)
    {
        try
        {
            var configDir = await Task.Run(ConfigImportHandler.FindOriginalConfigDir);
            if (configDir is null)
            {
                if (!userInitiated)
                {
                    return;
                }
                var folder = await UI.OpenFolderDialog(ResUI.TipImportNotFound);
                if (folder.IsNullOrEmpty())
                {
                    return;
                }
                configDir = ConfigImportHandler.GetConfigDirFromFolder(folder);
                if (configDir is null)
                {
                    NoticeManager.Instance.Enqueue(ResUI.TipImportInvalidFolder);
                    return;
                }
            }

            var installDir = Path.GetDirectoryName(configDir) ?? configDir;
            if (await UI.ShowYesNo(string.Format(ResUI.TipImportFound, installDir), ResUI.TbImport, ResUI.TbNotNow) != ButtonResult.Yes)
            {
                if (!userInitiated)
                {
                    ConfigImportHandler.DismissOffer();
                }
                return;
            }

            if (!await Task.Run(() => ConfigImportHandler.StageImport(configDir)))
            {
                NoticeManager.Instance.Enqueue(ResUI.OperationFailed);
                return;
            }

            // The staged settings are swapped in when the app starts again.
            _blCloseByUser = true;
            StorageUI();
            await AppManager.Instance.RestartApp();
        }
        catch (Exception ex)
        {
            Logging.SaveLog("ImportFromOriginalAsync", ex);
        }
    }

    private void RestoreUI()
    {
        if (_config.UiItem.MainGirdHeight1 > 0 && _config.UiItem.MainGirdHeight2 > 0)
        {
            if (_config.UiItem.MainGirdOrientation == EGirdOrientation.Horizontal)
            {
                gridMain.ColumnDefinitions[0].Width = new GridLength(_config.UiItem.MainGirdHeight1, GridUnitType.Star);
                gridMain.ColumnDefinitions[2].Width = new GridLength(_config.UiItem.MainGirdHeight2, GridUnitType.Star);
            }
            else if (_config.UiItem.MainGirdOrientation == EGirdOrientation.Vertical)
            {
                gridMain1.RowDefinitions[0].Height = new GridLength(_config.UiItem.MainGirdHeight1, GridUnitType.Star);
                gridMain1.RowDefinitions[2].Height = new GridLength(_config.UiItem.MainGirdHeight2, GridUnitType.Star);
            }
        }
        SetLogsHidden(File.Exists(LogsHiddenMarker));
    }

    // Glide-only preference, kept out of the shared v2rayN config: a marker file means "logs folded away".
    private static string LogsHiddenMarker => Utils.GetConfigPath("glide-logs-hidden");

    private bool _logsHidden;
    private GridLength _logsRowHeight = new(1, GridUnitType.Star);

    private void SetLogsHidden(bool hidden)
    {
        var row = gridMain1.RowDefinitions[2];
        if (hidden)
        {
            // Remember the split (saved as star weights like 474:238) so showing restores it exactly.
            if (row.Height.Value > 0)
            {
                _logsRowHeight = row.Height;
            }
            row.Height = new GridLength(0);
        }
        else if (_logsHidden)
        {
            row.Height = _logsRowHeight;
        }
        _logsHidden = hidden;
        gridMain1.RowDefinitions[1].Height = new GridLength(hidden ? 0 : 16);
        bdLogs.IsVisible = !hidden;
        splitLogs.IsVisible = !hidden;
        btnToggleLogs.Content = hidden ? ResUI.TbLogs : ResUI.TbHideLogs;
        try
        {
            if (hidden)
            {
                File.WriteAllText(LogsHiddenMarker, "1");
            }
            else if (File.Exists(LogsHiddenMarker))
            {
                File.Delete(LogsHiddenMarker);
            }
        }
        catch
        {
            // A read-only folder only means the choice isn't remembered.
        }
    }

    private void StorageUI()
    {
        ConfigHandler.SaveWindowSizeItem(_config, GetType().Name, Width, Height);

        if (_config.UiItem.MainGirdOrientation == EGirdOrientation.Horizontal)
        {
            ConfigHandler.SaveMainGirdHeight(_config, gridMain.ColumnDefinitions[0].ActualWidth, gridMain.ColumnDefinitions[2].ActualWidth);
        }
        else if (_config.UiItem.MainGirdOrientation == EGirdOrientation.Vertical && !_logsHidden)
        {
            // While the logs are folded away their row is 0 high; keep the split saved before that.
            ConfigHandler.SaveMainGirdHeight(_config, gridMain1.RowDefinitions[0].ActualHeight, gridMain1.RowDefinitions[2].ActualHeight);
        }
    }

    private void UpdateLayout(EGirdOrientation orientation)
    {
        var currentLayoutDisposables = new MultipleDisposable();
        _layoutBindingsDisposable.Create(currentLayoutDisposables);

        ClearLayoutContent();

        gridMain.IsVisible = orientation == EGirdOrientation.Horizontal;
        gridMain1.IsVisible = orientation == EGirdOrientation.Vertical;
        gridMain2.IsVisible = orientation == EGirdOrientation.Tab;

        switch (orientation)
        {
            case EGirdOrientation.Horizontal:
                this.OneWayBind(ViewModel, vm => vm.ProfilesViewModel, v => v.tabProfiles.Content).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.MsgViewModel, v => v.tabMsgView.Content).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ClashProxiesViewModel, v => v.tabClashProxies.Content).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ClashConnectionsViewModel, v => v.tabClashConnections.Content).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ShowClashUI, v => v.tabClashProxies.IsVisible).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ShowClashUI, v => v.tabClashConnections.IsVisible).DisposeWith(currentLayoutDisposables);
                this.Bind(ViewModel, vm => vm.TabMainSelectedIndex, v => v.tabMain.SelectedIndex).DisposeWith(currentLayoutDisposables);
                break;

            case EGirdOrientation.Vertical:
                this.OneWayBind(ViewModel, vm => vm.ProfilesViewModel, v => v.tabProfiles1.Content).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.MsgViewModel, v => v.tabMsgView1.Content).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ClashProxiesViewModel, v => v.tabClashProxies1.Content).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ClashConnectionsViewModel, v => v.tabClashConnections1.Content).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ShowClashUI, v => v.tabClashProxies1.IsVisible).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ShowClashUI, v => v.tabClashConnections1.IsVisible).DisposeWith(currentLayoutDisposables);
                this.Bind(ViewModel, vm => vm.TabMainSelectedIndex, v => v.tabMain1.SelectedIndex).DisposeWith(currentLayoutDisposables);
                break;

            case EGirdOrientation.Tab:
            default:
                this.OneWayBind(ViewModel, vm => vm.ProfilesViewModel, v => v.tabProfiles2.Content).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.MsgViewModel, v => v.tabMsgView2.Content).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ClashProxiesViewModel, v => v.tabClashProxies2.Content).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ClashConnectionsViewModel, v => v.tabClashConnections2.Content).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ShowClashUI, v => v.tabClashProxies2.IsVisible).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ShowClashUI, v => v.tabClashConnections2.IsVisible).DisposeWith(currentLayoutDisposables);
                this.Bind(ViewModel, vm => vm.TabMainSelectedIndex, v => v.tabMain2.SelectedIndex).DisposeWith(currentLayoutDisposables);
                break;
        }

        RestoreUI();
    }

    private void ClearLayoutContent()
    {
        tabProfiles.Content = null;
        tabMsgView.Content = null;
        tabClashProxies.Content = null;
        tabClashConnections.Content = null;

        tabProfiles1.Content = null;
        tabMsgView1.Content = null;
        tabClashProxies1.Content = null;
        tabClashConnections1.Content = null;

        tabProfiles2.Content = null;
        tabMsgView2.Content = null;
        tabClashProxies2.Content = null;
        tabClashConnections2.Content = null;
    }

    #endregion UI
}
