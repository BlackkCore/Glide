using System.Text.RegularExpressions;
using v2rayN.Desktop.Common;
using v2rayN.Desktop.Views;

namespace v2rayN.Desktop;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

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
            _trayServers = new NativeMenuItem(ResUI.menuServers) { Menu = new NativeMenu(), IsVisible = false };
            var disconnect = menu.Items.OfType<NativeMenuItem>().FirstOrDefault(i => i.Header == ResUI.TbDisconnect);
            menu.Items.Insert(disconnect == null ? 0 : menu.Items.IndexOf(disconnect) + 1, _trayServers);

            viewModel.Servers.CollectionChanged += (_, _) => _ = RebuildTrayServersAsync(viewModel);
            // Upstream leaves Servers empty when the list is too long for the tray, so starring and
            // startup rebuild it too: favourites show either way.
            FavoriteStore.Changed += () => Dispatcher.UIThread.Post(async () => await RebuildTrayServersAsync(viewModel));
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
            var favourites = new List<ComboItem>();
            foreach (var id in FavoriteStore.All())
            {
                var item = listed.FirstOrDefault(s => s.ID == id);
                if (item == null && await AppManager.Instance.GetProfileItem(id) is { } profile)
                {
                    item = new ComboItem { ID = profile.IndexId, Text = profile.GetSummary() };
                }
                if (item != null)
                {
                    favourites.Add(item);
                }
            }
            var others = listed.Where(s => favourites.All(f => f.ID != s.ID)).ToList();

            submenu.Items.Clear();
            foreach (var item in favourites)
            {
                submenu.Items.Add(TrayServerItem(viewModel, item, "★ ", activeId));
            }
            if (favourites.Count > 0 && others.Count > 0)
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
