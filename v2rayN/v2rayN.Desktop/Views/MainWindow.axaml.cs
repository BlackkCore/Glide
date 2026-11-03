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
    private bool _blCloseByUser = false;

    public MainWindow()
    {
        InitializeComponent();

        _config = AppManager.Instance.Config;
        // Toasts start below the 40px title bar so they never cover the window buttons.
        _manager = new WindowNotificationManager(TopLevel.GetTopLevel(this))
        {
            MaxItems = 3,
            Position = NotificationPosition.TopRight,
            Margin = new Thickness(16, 40, 16, 0),
        };

        KeyDown += MainWindow_KeyDown;
        menuSettingsSetUWP.Click += MenuSettingsSetUWP_Click;
        menuPromotion.Click += MenuPromotion_Click;
        menuCheckUpdate.Click += MenuCheckUpdate_Click;
        btnNewUpdate.Click += MenuCheckUpdate_Click;
        menuBackupAndRestore.Click += MenuBackupAndRestore_Click;
        menuImportFromOriginal.Click += async (_, _) => await ImportFromOriginalAsync(true);
        menuClose.Click += MenuClose_Click;

        conTheme.Content ??= new ThemeSettingView();
        if (conTheme.Content is ThemeSettingView appearance)
        {
            appearance.ThemeRequested += (theme, origin) => PlayThemeReveal(() => SetTheme(theme), origin);
        }
        btnThemeLight.Click += (_, _) => PlayThemeReveal(() => SetTheme(ETheme.Light), btnThemeLight);
        btnThemeDark.Click += (_, _) => PlayThemeReveal(() => SetTheme(ETheme.Dark), btnThemeDark);
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
            this.BindCommand(ViewModel, vm => vm.SubUpdateCmd, v => v.menuSubUpdate).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SubUpdateViaProxyCmd, v => v.menuSubUpdateViaProxy).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SubGroupUpdateCmd, v => v.menuSubGroupUpdate).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SubGroupUpdateViaProxyCmd, v => v.menuSubGroupUpdateViaProxy).DisposeWith(disposables);

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
            this.OneWayBind(ViewModel, vm => vm.BlNewUpdate, v => v.btnNewUpdate.IsVisible).DisposeWith(disposables);

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
        });

        if (Utils.IsWindows())
        {
            Title = $"{Utils.GetVersion()} - {(Utils.IsAdministrator() ? ResUI.RunAsAdmin : ResUI.NotRunAsAdmin)}";

            if (!Design.IsDesignMode)
            {
                ThreadPool.RegisterWaitForSingleObject(Program.ProgramStarted, OnProgramStarted, null, -1, false);
                HotkeyManager.Instance.Init(_config, OnHotkeyHandler);
            }
        }
        else
        {
            Title = $"{Utils.GetVersion()}";
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
        _manager?.Show(new Avalonia.Controls.Notifications.Notification(null, content, NotificationType.Information));
    }

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
        btnThemeLight.Classes.Set("active", !isDark);
        btnThemeDark.Classes.Set("active", isDark);
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

    // The new theme spreads out in a circle from the button that was clicked: a picture of the old
    // theme covers the window, the theme changes underneath, and a growing hole is cut into the picture.
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

        var center = origin.TranslatePoint(new Point(origin.Bounds.Width / 2, origin.Bounds.Height / 2), gridRoot)
                     ?? new Point(0, bounds.Height);
        var maxRadius = new[]
        {
            new Point(0, 0), new Point(bounds.Width, 0), new Point(0, bounds.Height), new Point(bounds.Width, bounds.Height),
        }.Max(p => Math.Sqrt(Math.Pow(p.X - center.X, 2) + Math.Pow(p.Y - center.Y, 2)));
        var area = new RectangleGeometry(new Rect(bounds.Size));
        var start = DateTime.Now;

        _revealTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _revealTimer.Tick += (_, _) =>
        {
            var t = Math.Clamp((DateTime.Now - start).TotalMilliseconds / 620, 0, 1);
            var eased = t < 0.5 ? 4 * t * t * t : 1 - (Math.Pow((-2 * t) + 2, 3) / 2);
            var r = maxRadius * eased;
            imgThemeSnapshot.Clip = new CombinedGeometry(GeometryCombineMode.Exclude, area,
                new EllipseGeometry(new Rect(center.X - r, center.Y - r, r * 2, r * 2)));
            if (t >= 1)
            {
                EndThemeReveal();
            }
        };
        _revealTimer.Start();
    }

    private void EndThemeReveal()
    {
        _revealTimer?.Stop();
        Classes.Remove("themeSwap");
        imgThemeSnapshot.IsVisible = false;
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
        DockPanel.SetDock(conTheme, compact ? Dock.Bottom : Dock.Right);

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
            if (await UI.ShowYesNo(ResUI.menuExitTips) != ButtonResult.Yes)
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
        _ = OfferImportOnFirstRunAsync();
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
    }

    private void StorageUI()
    {
        ConfigHandler.SaveWindowSizeItem(_config, GetType().Name, Width, Height);

        if (_config.UiItem.MainGirdOrientation == EGirdOrientation.Horizontal)
        {
            ConfigHandler.SaveMainGirdHeight(_config, gridMain.ColumnDefinitions[0].ActualWidth, gridMain.ColumnDefinitions[2].ActualWidth);
        }
        else if (_config.UiItem.MainGirdOrientation == EGirdOrientation.Vertical)
        {
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
