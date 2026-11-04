using System.Text.RegularExpressions;
using v2rayN.Desktop.Common;
using v2rayN.Desktop.Views;

namespace v2rayN.Desktop;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        // Dropdowns hanging off a button (List actions, Sort, Appearance) open in their own popup that
        // doesn't inherit the window's right-to-left layout; follow the main window, as right-click menus do.
        Control.LoadedEvent.AddClassHandler<MenuFlyoutPresenter>((presenter, _) => FollowMainWindowDirection(presenter));
        Control.LoadedEvent.AddClassHandler<FlyoutPresenter>((presenter, _) => FollowMainWindowDirection(presenter));

        // Screen readers: an icon-only button (sort, star, ⋯, theme…) has no text of its own, so give it
        // its tooltip as its accessible name. Buttons with text keep reading their text.
        Control.LoadedEvent.AddClassHandler<Button>((button, _) =>
        {
            if (string.IsNullOrEmpty(Avalonia.Automation.AutomationProperties.GetName(button))
                && button.Content is not string
                && ToolTip.GetTip(button) is string tip
                && tip.Length > 0)
            {
                Avalonia.Automation.AutomationProperties.SetName(button, tip);
            }
        });

        // Transitions and animations only when Windows "Animation effects" is on. Added here, before any
        // window exists, so nothing has to be re-templated.
        if (AppMotion.Enabled)
        {
            Styles.Add(new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://Glide/")) { Source = new Uri("avares://Glide/Assets/AppMotion.axaml") });
        }

        // Screen readers don't read tooltips: an icon-only button with just a tooltip had no name.
        // Any control given a tooltip, and no accessible name of its own, takes the tooltip text, and
        // follows it when the tooltip changes later (e.g. the list / table view switch).
        ToolTip.TipProperty.Changed.AddClassHandler<Control>((control, e) =>
        {
            var name = Avalonia.Automation.AutomationProperties.GetName(control);
            if (e.NewValue is string tip && tip.IsNotEmpty()
                && (name.IsNullOrEmpty() || name == e.OldValue as string))
            {
                Avalonia.Automation.AutomationProperties.SetName(control, tip);
            }
        });

        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
    }

    private void FollowMainWindowDirection(Control presenter)
    {
        if ((ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow is { } window
            && presenter.FlowDirection != window.FlowDirection)
        {
            presenter.FlowDirection = window.FlowDirection;
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var viewLocator = SimpleViewLocator.Instance;
        DataTemplates.Add(viewLocator);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (!Design.IsDesignMode)
            {
                AppManager.Instance.InitComponents();
                DataContext = StatusBarViewModel.Instance;
                SetupTrayServers(StatusBarViewModel.Instance);
            }

            var mainWindowViewModel = new MainWindowViewModel();
            var mainWindow = (MainWindow)viewLocator.Build(mainWindowViewModel);
            mainWindow.ViewModel = mainWindowViewModel;
            desktop.MainWindow = mainWindow;

            if (OperatingSystem.IsMacOS())
            {
                Current?.TryGetFeature<IActivatableLifetime>()?.Activated += OnMacOSActivated;
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    #region Tray servers

    // "[VLESS] Frankfurt(fra***net:443)" or "[PolicyGroup] [Xray]⚡ Auto" -> the name only.
    private static readonly Regex TrayNameRegex = new(@"^\[[^\]]+\]\s*(?:\[[^\]]+\])?\s*(?<name>.*?)(\([^()]*:\d+\))?$", RegexOptions.Compiled);

    private NativeMenuItem? _trayServers;

    /// <summary>
    /// A "Servers" submenu under Connect / Disconnect: favourites first (also when the subscription is
    /// too long for upstream's tray list), then the rest. Picking one goes through upstream's
    /// SelectedServer, which switches the active server.
    /// </summary>
    private void SetupTrayServers(StatusBarViewModel viewModel)
    {
        try
        {
            if (TrayIcon.GetIcons(this)?.FirstOrDefault()?.Menu is not { } menu)
            {
                return;
            }
            // Top of the menu: where things stand (click opens Glide), then one Connect / Disconnect
            // item, then the servers. The proxy modes and the rest follow from App.axaml.
            _trayStatus = new NativeMenuItem(ResUI.TbStatusNotConnected);
            _trayStatus.Click += (_, _) => viewModel.ShowWindowCmd.Execute().Subscribe();
            _trayToggle = new NativeMenuItem(ResUI.TbConnect);
            _trayToggle.Click += (_, _) =>
            {
                var command = viewModel.BlSystemProxySet ? viewModel.SystemProxyClearCmd : viewModel.SystemProxySetCmd;
                command.Execute().Subscribe();
            };
            _trayServers = new NativeMenuItem(ResUI.menuServers) { Menu = new NativeMenu(), IsVisible = false };
            menu.Items.Insert(0, _trayStatus);
            menu.Items.Insert(1, new NativeMenuItemSeparator());
            menu.Items.Insert(2, _trayToggle);
            menu.Items.Insert(3, _trayServers);
            // Space before the proxy modes when there's nothing else between them.
            menu.Items.Insert(4, new NativeMenuItemSeparator());

            viewModel.WhenAnyValue(v => v.BlSystemProxySet)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(on =>
                {
                    _trayToggle.Header = on ? ResUI.TbDisconnect : ResUI.TbConnect;
                    _trayToggleOn = on;
                    RefreshTrayIcons();
                });
            StatusBarView.TrayStatusChanged += (state, label, server) => Dispatcher.UIThread.Post(() =>
            {
                _trayState = state;
                _trayStatus.Header = server.IsNotEmpty() ? $"{label}  ·  {Shorten(server, 34)}" : label;
                RefreshTrayIcons();
            });
            ActualThemeVariantChanged += (_, _) => RefreshTrayIcons();
            RefreshTrayIcons();

            viewModel.Servers.CollectionChanged += (_, _) => _ = RebuildTrayServersAsync(viewModel);
            // Upstream leaves Servers empty when the list is too long for the tray, so starring and
            // startup rebuild it too: favourites show either way.
            FavoriteStore.Changed += () => Dispatcher.UIThread.Post(async () => await RebuildTrayServersAsync(viewModel));
            RecentStore.Changed += () => Dispatcher.UIThread.Post(async () => await RebuildTrayServersAsync(viewModel));
            Dispatcher.UIThread.Post(async () => await RebuildTrayServersAsync(viewModel));
            viewModel.WhenAnyValue(v => v.SelectedServer)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(async _ => await RebuildTrayServersAsync(viewModel));
        }
        catch (Exception ex)
        {
            Logging.SaveLog("SetupTrayServers", ex);
        }
    }

    private NativeMenuItem? _trayStatus;
    private NativeMenuItem? _trayToggle;
    private bool _trayToggleOn;
    private ConnState _trayState = ConnState.NotConnected;

    private static string Shorten(string text, int max) => text.Length <= max ? text : text[..(max - 1)].TrimEnd() + "…";

    // Small icons for the tray menu rows, drawn from the app's own icon shapes in the theme's muted
    // colour; the status row gets a dot in the status colour. Redrawn when the theme changes.
    private void RefreshTrayIcons()
    {
        try
        {
            if (TrayIcon.GetIcons(this)?.FirstOrDefault()?.Menu is not { } menu)
            {
                return;
            }
            var dark = ActualThemeVariant == ThemeVariant.Dark;
            var muted = Color.Parse(dark ? "#A1A7B3" : "#4B5563");
            var accent = Color.Parse(dark ? "#818CF8" : "#4F46E5");

            if (_trayStatus != null)
            {
                var dot = _trayState switch
                {
                    ConnState.Connected => dark ? "#22D3EE" : "#06B6D4",
                    ConnState.Checking => "#E39A12",
                    ConnState.NoInternet or ConnState.PortConflict or ConnState.EngineStopped => "#E5484D",
                    _ => dark ? "#6B7280" : "#9CA3AF",
                };
                _trayStatus.Icon = RenderDot(Color.Parse(dot));
            }
            if (_trayToggle != null)
            {
                _trayToggle.Icon = RenderIcon("IconPower", _trayToggleOn ? muted : accent);
            }
            if (_trayServers != null)
            {
                _trayServers.Icon = RenderIcon("IconServers", muted);
            }

            var byHeader = new Dictionary<string, string>
            {
                [ResUI.menuSystemproxy] = "IconShield",
                [ResUI.menuAddServerViaClipboard] = "IconClipboard",
                [ResUI.TbNavSubscriptions] = "IconRss",
                [ResUI.TbCopyProxyCommand] = "IconTerminal",
                [ResUI.TbOpenGlide] = "IconGlider",
                [ResUI.menuExit] = "IconExit",
            };
            foreach (var item in menu.Items.OfType<NativeMenuItem>())
            {
                if (item.Header is { } header && byHeader.TryGetValue(header, out var key))
                {
                    item.Icon = RenderIcon(key, muted);
                }
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("RefreshTrayIcons", ex);
        }
    }

    private const int TrayIconPx = 32; // drawn at 2x for a 16 px icon

    private Bitmap? RenderIcon(string key, Color color)
    {
        if (!this.TryFindResource(key, ActualThemeVariant, out var resource) || resource is not Geometry geometry)
        {
            return null;
        }
        var bitmap = new RenderTargetBitmap(new PixelSize(TrayIconPx, TrayIconPx), new Vector(192, 192));
        using (var context = bitmap.CreateDrawingContext())
        using (context.PushTransform(Matrix.CreateScale(16 / 24.0, 16 / 24.0)))
        {
            context.DrawGeometry(new SolidColorBrush(color), null, geometry);
        }
        return bitmap;
    }

    private static Bitmap RenderDot(Color color)
    {
        var bitmap = new RenderTargetBitmap(new PixelSize(TrayIconPx, TrayIconPx), new Vector(192, 192));
        using (var context = bitmap.CreateDrawingContext())
        {
            context.DrawEllipse(new SolidColorBrush(color), null, new Point(8, 8), 4.5, 4.5);
        }
        return bitmap;
    }

    private async Task RebuildTrayServersAsync(StatusBarViewModel viewModel)
    {
        try
        {
            if (_trayServers?.Menu is not { } submenu)
            {
                return;
            }
            var activeId = AppManager.Instance.Config.IndexId;
            var listed = viewModel.Servers.Where(s => s.ID.IsNotEmpty()).ToList();
            async Task<List<ComboItem>> Resolve(IEnumerable<string> ids)
            {
                var items = new List<ComboItem>();
                foreach (var id in ids)
                {
                    var item = listed.FirstOrDefault(s => s.ID == id);
                    if (item == null && await AppManager.Instance.GetProfileItem(id) is { } profile)
                    {
                        item = new ComboItem { ID = profile.IndexId, Text = profile.GetSummary() };
                    }
                    if (item != null)
                    {
                        items.Add(item);
                    }
                }
                return items;
            }
            var favourites = await Resolve(FavoriteStore.All());
            var recent = await Resolve(RecentStore.All().Where(id => favourites.All(f => f.ID != id)));
            var others = listed.Where(s => favourites.All(f => f.ID != s.ID) && recent.All(r => r.ID != s.ID)).ToList();

            submenu.Items.Clear();
            foreach (var item in favourites)
            {
                submenu.Items.Add(TrayServerItem(viewModel, item, "★ ", activeId));
            }
            if (favourites.Count > 0 && recent.Count > 0)
            {
                submenu.Items.Add(new NativeMenuItemSeparator());
            }
            foreach (var item in recent)
            {
                submenu.Items.Add(TrayServerItem(viewModel, item, "↻ ", activeId));
            }
            if (favourites.Count + recent.Count > 0 && others.Count > 0)
            {
                submenu.Items.Add(new NativeMenuItemSeparator());
            }
            foreach (var item in others)
            {
                submenu.Items.Add(TrayServerItem(viewModel, item, string.Empty, activeId));
            }
            _trayServers.IsVisible = submenu.Items.Count > 0;
        }
        catch (Exception ex)
        {
            Logging.SaveLog("RebuildTrayServers", ex);
        }
    }

    private static NativeMenuItem TrayServerItem(StatusBarViewModel viewModel, ComboItem server, string prefix, string? activeId)
    {
        var match = TrayNameRegex.Match(server.Text ?? string.Empty);
        var name = match.Success ? match.Groups["name"].Value.Trim() : server.Text;
        var item = new NativeMenuItem($"{prefix}{name}")
        {
            ToggleType = MenuItemToggleType.Radio,
            IsChecked = server.ID == activeId,
        };
        item.Click += (_, _) => viewModel.SelectedServer = viewModel.Servers.FirstOrDefault(s => s.ID == server.ID) ?? server;
        return item;
    }

    #endregion Tray servers

    #region MacOS Activation

    private void OnMacOSActivated(object? sender, ActivatedEventArgs args)
    {
        if (args.Kind != ActivationKind.Reopen)
        {
            return;
        }

        if ((ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow is not MainWindow mainWindow)
        {
            return;
        }

        var isMiniaturized = MacAppUtils.IsWindowMiniaturized(mainWindow);

        Dispatcher.UIThread.Post(() =>
        {
            if (isMiniaturized)
            {
                RestoreMacOSAccessoryPolicyAfterMiniaturize(mainWindow);
                mainWindow.ShowHideWindow(true);
                return;
            }

            if (!AppManager.Instance.Config.UiItem.MacOSShowInDock)
            {
                MacAppUtils.SetActivationPolicyAccessory();
            }

            mainWindow.ShowHideWindow(true);
        });
    }

    private static void RestoreMacOSAccessoryPolicyAfterMiniaturize(MainWindow mainWindow)
    {
        if (AppManager.Instance.Config.UiItem.MacOSShowInDock)
        {
            return;
        }

        mainWindow
            .GetObservable(Window.WindowStateProperty)
            .Skip(1)
            .Where(state => state != WindowState.Minimized)
            .Take(1)
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(_ => QueueMacOSAccessoryPolicyRestore(mainWindow));
    }

    private static void QueueMacOSAccessoryPolicyRestore(MainWindow mainWindow)
    {
        // AppKit may keep isMiniaturized set until the Dock restore animation finishes.
        DispatcherTimer.RunOnce(() => RestoreMacOSAccessoryPolicy(mainWindow), TimeSpan.FromMilliseconds(300));
    }

    private static void RestoreMacOSAccessoryPolicy(MainWindow mainWindow)
    {
        if (AppManager.Instance.Config.UiItem.MacOSShowInDock || MacAppUtils.IsWindowMiniaturized(mainWindow))
        {
            return;
        }

        MacAppUtils.SetActivationPolicyAccessory();
        mainWindow.Activate();
        mainWindow.Focus();
    }

    #endregion MacOS Activation

    #region App Event

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject != null)
        {
            Logging.SaveLog("CurrentDomain_UnhandledException", (Exception)e.ExceptionObject);
        }
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Logging.SaveLog("TaskScheduler_UnobservedTaskException", e.Exception);
    }

    private async void MenuAddServerViaClipboardClick(object? sender, EventArgs e)
    {
        try
        {
            if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: not null })
            {
                AppEvents.AddServerViaClipboardRequested.Publish();
                await Task.Delay(1000);
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("MenuAddServerViaClipboardClick", ex);
        }
    }

    private async void MenuExit_Click(object? sender, EventArgs e)
    {
        await AppManager.Instance.AppExitAsync(false);
        AppManager.Instance.Shutdown(true);
    }

    #endregion App Event
}
