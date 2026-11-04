using Avalonia.Controls.DataGridDragDrop;
using DialogHostAvalonia;
using v2rayN.Desktop.Common;

namespace v2rayN.Desktop.Views;

public partial class ProfilesView : ReactiveUserControl<ProfilesViewModel>
{
    private static Config _config;
    private static readonly string _tag = "ProfilesView";
    private bool _isDndRegistered = false;

    public ProfilesView()
    {
        InitializeComponent();

        _config = AppManager.Instance.Config;

        menuSelectAll.Click += menuSelectAll_Click;
        btnAutofitColumnWidth.Click += BtnAutofitColumnWidth_Click;
        txtServerFilter.KeyDown += TxtServerFilter_KeyDown;
        // Filter as you type (after a short pause), not only on Enter.
        _filterTimer.Tick += (_, _) =>
        {
            _filterTimer.Stop();
            ViewModel?.RefreshServers();
        };
        txtServerFilter.TextChanged += (_, _) =>
        {
            _filterTimer.Stop();
            _filterTimer.Start();
        };
        btnEmptyPaste.Click += (_, _) => AppEvents.AddServerSheetRequested.Publish();
        SetMenuIcons();
        SizeChanged += (_, e) => FitToolbar(e.NewSize.Width);
        btnAddServer.Click += (_, _) => AppEvents.AddServerSheetRequested.Publish();
        btnAutoFastest.Click += async (_, _) => await UseAutoFastestAsync();
        // Sort without the table's column headers (upstream's own sorting, by column tag).
        menuSortFastest.Click += async (_, _) => await Guarded(() => ViewModel?.SortServer("DelayVal") ?? Task.CompletedTask);
        menuSortName.Click += async (_, _) => await Guarded(() => ViewModel?.SortServer("Remarks") ?? Task.CompletedTask);
        // This subscription only, right away (the main menu updates all of them).
        menuSubUpdateNow.Click += async (_, _) => await Guarded(async () =>
        {
            if (ViewModel?.SelectedSub is { Id.Length: > 0 } sub && TopLevel.GetTopLevel(this) is MainWindow { ViewModel: { } main })
            {
                await main.UpdateSubscriptionProcess(sub.Id, false);
            }
        });
        // Upstream's per-subscription refresh interval, without opening the editor.
        foreach (var hours in new[] { 0, 6, 12, 24 })
        {
            var item = new MenuItem
            {
                Header = hours == 0 ? ResUI.TbOff : string.Format(ResUI.TbEveryHours, hours),
                ToggleType = MenuItemToggleType.Radio,
            };
            item.Click += async (_, _) => await Guarded(async () =>
            {
                if (ViewModel?.SelectedSub is { Id.Length: > 0 } sub)
                {
                    sub.AutoUpdateInterval = hours * 60;
                    await ConfigHandler.AddSubItem(_config, sub);
                    UpdateSubInfo();
                }
            });
            menuSubAutoUpdate.Items.Add(item);
        }
        menuSubAutoUpdate.SubmenuOpened += (_, _) =>
        {
            var current = ViewModel?.SelectedSub?.AutoUpdateInterval ?? 0;
            var index = current switch { 360 => 1, 720 => 2, 1440 => 3, _ => current > 0 ? -1 : 0 };
            for (var i = 0; i < menuSubAutoUpdate.Items.Count; i++)
            {
                ((MenuItem)menuSubAutoUpdate.Items[i]!).IsChecked = i == index;
            }
        };
        // The whole subscription as a QR code, for v2rayNG / Hiddify on the phone.
        menuSubSendToPhone.Click += async (_, _) =>
        {
            try
            {
                await ShareServer(ViewModel?.SelectedSub?.Url ?? string.Empty);
            }
            catch (Exception ex)
            {
                Logging.SaveLog(_tag, ex);
            }
        };
        menuFavorite.Click += (_, _) => ToggleFavorites(lstProfiles.SelectedItems?.OfType<ProfileItemModel>().ToList() ?? []);
        lstProfiles.KeyDown += LstProfiles_KeyDown;
        lstProfiles.SelectionChanged += lstProfiles_SelectionChanged;
        lstProfiles.DoubleTapped += LstProfiles_DoubleTapped;
        lstProfiles.Sorting += LstProfiles_Sorting;

        this.WhenActivated(disposables =>
        {
            this.OneWayBind(ViewModel, vm => vm.ProfileItems, v => v.lstProfiles.ItemsSource).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SelectedProfile, v => v.lstProfiles.SelectedItem).DisposeWith(disposables);

            // this.OneWayBind(ViewModel, vm => vm.SubItems, v => v.lstGroup.ItemsSource).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SelectedSub, v => v.lstGroup.SelectedItem).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.ServerFilter, v => v.txtServerFilter.Text).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddSubCmd, v => v.btnAddSub).DisposeWith(disposables);

            // Empty list with no filter typed: show the first-run card instead of a blank table.
            // This view and its view model live as long as the app, so the handler is added once.
            if (!_emptyStateHooked)
            {
                _emptyStateHooked = true;
                ViewModel.ProfileItems.CollectionChanged += (_, _) => Dispatcher.UIThread.Post(UpdateEmptyState);
                // A subscription update reloads the list, so the provider's new numbers show up then.
                ViewModel.ProfileItems.CollectionChanged += (_, _) => Dispatcher.UIThread.Post(() =>
                {
                    UpdateSubInfo();
                    WarnAboutSubscriptions();
                });
            }
            this.WhenAnyValue(v => v.ViewModel.SelectedSub)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(_ => UpdateSubInfo())
                .DisposeWith(disposables);
            this.WhenAnyValue(v => v.ViewModel.ServerFilter)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(_ => UpdateEmptyState())
                .DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.EditSubCmd, v => v.btnEditSub).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.EditSubCmd, v => v.menuSubEdit).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddSubCmd, v => v.menuSubAdd).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.DeleteSubCmd, v => v.menuSubDelete).DisposeWith(disposables);

            //servers delete
            this.BindCommand(ViewModel, vm => vm.EditServerCmd, v => v.menuEditServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RemoveServerCmd, v => v.menuRemoveServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RemoveDuplicateServerCmd, v => v.menuRemoveDuplicateServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.CopyServerCmd, v => v.menuCopyServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SetDefaultServerCmd, v => v.menuSetDefaultServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.ShareServerCmd, v => v.menuShareServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.GenGroupAllServerCmd, v => v.menuGenGroupAllServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.GenGroupRegionServerCmd, v => v.menuGenGroupRegionServer).DisposeWith(disposables);

            //servers move
            //this.OneWayBind(ViewModel, vm => vm.SubItems, v => v.cmbMoveToGroup.ItemsSource).DisposeWith(disposables);
            //this.Bind(ViewModel, vm => vm.SelectedMoveToGroup, v => v.cmbMoveToGroup.SelectedItem).DisposeWith(disposables);

            this.BindCommand(ViewModel, vm => vm.MoveTopCmd, v => v.menuMoveTop).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.MoveUpCmd, v => v.menuMoveUp).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.MoveDownCmd, v => v.menuMoveDown).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.MoveBottomCmd, v => v.menuMoveBottom).DisposeWith(disposables);

            //servers ping
            this.BindCommand(ViewModel, vm => vm.MixedTestServerCmd, v => v.menuMixedTestServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.TcpingServerCmd, v => v.menuTcpingServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RealPingServerCmd, v => v.menuRealPingServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.UdpTestServerCmd, v => v.menuUdpTestServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SpeedServerCmd, v => v.menuSpeedServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SortServerResultCmd, v => v.menuSortServerResult).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RemoveInvalidServerResultCmd, v => v.menuRemoveInvalidServerResult).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.FastRealPingCmd, v => v.btnFastRealPing).DisposeWith(disposables);

            //servers export
            this.BindCommand(ViewModel, vm => vm.Export2ClientConfigCmd, v => v.menuExport2ClientConfig).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.Export2ClientConfigClipboardCmd, v => v.menuExport2ClientConfigClipboard).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.Export2ShareUrlCmd, v => v.menuExport2ShareUrl).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.Export2ShareUrlBase64Cmd, v => v.menuExport2ShareUrlBase64).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.Export2InnerUriCmd, v => v.menuExport2InnerUri).DisposeWith(disposables);

            if (_config.UiItem.EnableDragDropSort && !_isDndRegistered)
            {
                lstProfiles.CanUserReorderRows = true;
                lstProfiles.RowDragHandle = DataGridRowDragHandle.RowHeaderAndRow;
                lstProfiles.RowDragHandleVisible = true;
                var rowDragDropOptions = new DataGridRowDragDropOptions()
                {
                    AllowedEffects = DragDropEffects.Move,
                    DragSelectedRows = true,
                    SuppressSelectionDragFromDragHandle = true,
                };
                lstProfiles.RowDragDropOptions = rowDragDropOptions;
                lstProfiles.RowDropHandler = new ProfileRowDropHandler(ViewModel);
                _isDndRegistered = true;
            }

            ViewModel.ShowYesNoInteraction.RegisterHandler(async interaction =>
            {
                var message = interaction.Input;
                var result = await UI.ShowYesNo(message);
                interaction.SetOutput(result == ButtonResult.Yes);
            }).DisposeWith(disposables);

            ViewModel.SaveFileDialogInteraction.RegisterHandler(async interaction =>
            {
                var viewModel = ViewModel;
                if (viewModel is null)
                {
                    interaction.SetOutput(false);
                    return;
                }
                var profileItem = interaction.Input;
                var fileName = await UI.SaveFileDialog("");
                if (fileName.IsNullOrEmpty())
                {
                    interaction.SetOutput(false);
                    return;
                }
                await viewModel.Export2ClientConfigResult(fileName, profileItem);
                interaction.SetOutput(true);
            }).DisposeWith(disposables);

            ViewModel.SetClipboardDataInteraction.RegisterHandler(async interaction =>
            {
                var strData = interaction.Input;
                await AvaUtils.SetClipboardData(this, strData);
                interaction.SetOutput(RxVoid.Default);
            }).DisposeWith(disposables);

            ViewModel.ProfilesFocusInteraction.RegisterHandler(interaction =>
            {
                lstProfiles.Focus();
                interaction.SetOutput(RxVoid.Default);
            }).DisposeWith(disposables);

            ViewModel.ShareServerInteraction.RegisterHandler(async interaction =>
            {
                var url = interaction.Input;
                if (url.IsNullOrEmpty())
                {
                    interaction.SetOutput(RxVoid.Default);
                    return;
                }
                await ShareServer(url);
                interaction.SetOutput(RxVoid.Default);
            }).DisposeWith(disposables);

            ViewModel.DispatcherRefreshServersBizInteraction.RegisterHandler(interaction =>
            {
                Dispatcher.UIThread.Post(RefreshServersBiz, DispatcherPriority.Default);
                interaction.SetOutput(RxVoid.Default);
            }).DisposeWith(disposables);

            ViewModel.AdjustMainLvColWidthInteraction.RegisterHandler(interaction =>
            {
                //AutofitColumnWidth();
                interaction.SetOutput(RxVoid.Default);
            }).DisposeWith(disposables);

            AppEvents.AppExitRequested
              .AsObservable()
              .ObserveOn(RxSchedulers.MainThreadScheduler)
              .Subscribe(_ => StorageUI())
              .DisposeWith(disposables);
        });

        RestoreUI();
        btnToggleView.Click += (_, _) =>
        {
            var list = !_listView;
            ApplyViewMode(list, saveTableLayout: true);
            try
            {
                if (list)
                {
                    File.Delete(TableViewMarker);
                }
                else
                {
                    File.WriteAllText(TableViewMarker, "");
                }
            }
            catch
            {
                // Only a preference: if it can't be saved the default view comes back next time.
            }
        };
        ApplyViewMode(!File.Exists(TableViewMarker), saveTableLayout: false);
    }

    private bool _emptyStateHooked;

    #region List view

    // The list view is the default; the old table stays one click away as the advanced view.
    private static string TableViewMarker => Utils.GetConfigPath("glide-table-view");

    private bool _listView;

    // The list view's column is the only one without a Tag (Tags name the table's saved columns).
    private DataGridColumn? ListColumn => lstProfiles.Columns.FirstOrDefault(c => c.Tag == null);

    private void ApplyViewMode(bool list, bool saveTableLayout)
    {
        // Keep the table's widths and hidden columns before its columns are hidden. Not at startup:
        // nothing is measured yet, so every width would be saved as 0.
        if (list && !_listView && saveTableLayout)
        {
            StorageUI();
        }
        _listView = list;

        var listColumn = ListColumn;
        foreach (var column in lstProfiles.Columns)
        {
            column.IsVisible = column == listColumn ? list : !list;
        }
        if (listColumn != null)
        {
            listColumn.Width = new DataGridLength(1, DataGridLengthUnitType.Star);
        }
        if (!list)
        {
            // Hides what the user had hidden (and the statistics columns when those are off).
            RestoreUI();
        }

        lstProfiles.FrozenColumnCount = list ? 0 : 2;
        lstProfiles.Classes.Set("list", list);
        btnAutofitColumnWidth.IsVisible = !list;
        colChooser.IsVisible = !list;
        if (Bounds.Width > 0)
        {
            FitToolbar(Bounds.Width);
        }
        if (this.TryFindResource(list ? "IconViewTable" : "IconViewList", out var icon) && icon is Geometry geometry)
        {
            icoToggleView.Data = geometry;
        }
        ToolTip.SetTip(btnToggleView, list ? ResUI.TbTableView : ResUI.TbListView);
    }

    private void BtnFavorite_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ProfileItemModel item })
        {
            ToggleFavorites([item]);
        }
    }

    // Stars them all, unless every one is starred already: then unstars them.
    private void ToggleFavorites(IReadOnlyList<ProfileItemModel> items)
    {
        try
        {
            if (items.Count == 0)
            {
                return;
            }
            var star = items.Any(i => !i.IsFavorite);
            foreach (var item in items.Where(i => i.IsFavorite != star))
            {
                item.IsFavorite = FavoriteStore.Toggle(item.IndexId);
            }
            // Re-sort so starred servers move to the top.
            ViewModel?.RefreshServers();
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
    }

    // The row's ⋯ button opens the same menu as a right-click, for that server.
    private void BtnRowMenu_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: ProfileItemModel item } button)
        {
            return;
        }
        try
        {
            if (lstProfiles.SelectedItems?.Contains(item) != true)
            {
                lstProfiles.SelectedItem = item;
            }
            // A ContextMenu only opens on the control it belongs to. Anchor it to this button and open
            // it to the left, so it stays inside the window (at the pointer it ran past the right edge),
            // and close the button's "More actions" tooltip, which otherwise covered the first item.
            if (lstProfiles.ContextMenu is { } menu)
            {
                // No tooltip while the menu is open; it comes back when the menu closes.
                _rowMenuButton = button;
                _rowMenuTip = ToolTip.GetTip(button);
                ToolTip.SetIsOpen(button, false);
                ToolTip.SetTip(button, null);
                menu.PlacementTarget = button;
                menu.Placement = PlacementMode.LeftEdgeAlignedTop;
                menu.Closed -= RowMenu_Closed;
                menu.Closed += RowMenu_Closed;
                menu.Open(lstProfiles);
            }
        }
        catch (Exception ex)
        {
            // An exception here would reach the dispatcher and close the app.
            Logging.SaveLog(_tag, ex);
        }
    }

    // Narrow windows: drop the least-needed toolbar buttons one by one instead of letting the row run
    // off the edge. Their actions stay reachable (speed test in the row menu, view switch when wider).
    private void FitToolbar(double width)
    {
        menuMixedTestServer.IsVisible = width >= 640;
        btnAutofitColumnWidth.IsVisible = width >= 600 && !lstProfiles.Classes.Contains("list");
        btnToggleView.IsVisible = width >= 560;
    }

    // An icon at the start of each row of the server menu and the list menu, like the sidebar popups.
    private void SetMenuIcons()
    {
        var icons = new (MenuItem Item, string Key)[]
        {
            (menuSetDefaultServer, "IconPower"),
            (menuEditServer, "SemiIconEdit"),
            (menuFavorite, "IconStar"),
            (menuCopyServer, "SemiIconCopy"),
            (menuTcpingServer, "IconChain"),
            (menuRealPingServer, "building_ping"),
            (menuSpeedServer, "IconArrowDown"),
            (menuUdpTestServer, "SemiIconBolt"),
            (menuMoveToGroup, "IconFolder"),
            (menuShareServer, "IconScan"),
            (menuRemoveServer, "IconTrash"),
            (menuSortServerResult, "IconArrowDown"),
            (menuSelectAll, "IconApps"),
            (menuRemoveDuplicateServer, "IconLayers"),
            (menuRemoveInvalidServerResult, "IconTrash"),
            (menuGenGroup, "IconLayers"),
        };
        foreach (var (item, key) in icons)
        {
            // From the app's resources: this runs before the view is attached to the window.
            if (Application.Current?.TryFindResource(key, out var data) == true && data is Geometry geometry)
            {
                var icon = new PathIcon { Data = geometry };
                icon.Classes.Add("sub");
                item.Icon = icon;
            }
        }
    }

    private Control? _rowMenuButton;
    private object? _rowMenuTip;

    // A right-click afterwards opens the menu at the pointer again, and the ⋯ button gets its tooltip back.
    private void RowMenu_Closed(object? sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu menu)
        {
            menu.Closed -= RowMenu_Closed;
            menu.PlacementTarget = null;
            menu.Placement = PlacementMode.Pointer;
        }
        if (_rowMenuButton != null)
        {
            ToolTip.SetTip(_rowMenuButton, _rowMenuTip);
            _rowMenuButton = null;
            _rowMenuTip = null;
        }
    }

    #endregion List view

    // Click handlers are async void underneath: an exception escaping one would close the app.
    private async Task Guarded(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            NoticeManager.Instance.Enqueue(ResUI.OperationFailed);
        }
    }

    private async Task UseAutoFastestAsync()
    {
        try
        {
            var viewModel = ViewModel;
            var id = viewModel == null ? null : await AutoServer.EnsureAsync(_config, viewModel.SelectedSub);
            if (viewModel == null || id == null)
            {
                NoticeManager.Instance.Enqueue(ResUI.OperationFailed);
                return;
            }
            // Starred, so it stays at the top of the list.
            if (!FavoriteStore.IsFavorite(id))
            {
                FavoriteStore.Toggle(id);
            }
            if (_config.IndexId == id)
            {
                await viewModel.RefreshServers();
            }
            else
            {
                await viewModel.SetDefaultServer(id);
            }
            NoticeManager.Instance.Enqueue(ResUI.TipAutoFastestOn);
        }
        catch (Exception ex)
        {
            // An exception escaping a click handler would close the app.
            Logging.SaveLog(_tag, ex);
            NoticeManager.Instance.Enqueue(ResUI.OperationFailed);
        }
    }

    private void UpdateSubInfo()
    {
        // Auto needs one subscription to choose from, so not on "All servers".
        var sub = ViewModel?.SelectedSub;
        btnAutoFastest.IsVisible = sub?.Id.IsNotEmpty() == true;
        // Same key the download used (SubscriptionHandler fetches the punycode form of the URL).
        var info = sub == null || sub.Url.IsNullOrEmpty() ? null : SubscriptionInfoStore.Get(Utils.GetPunycode(sub.Url.TrimEx()));
        // Upstream refreshes a subscription every AutoUpdateInterval minutes (TaskManager): say when.
        DateTimeOffset? nextUpdate = sub is { AutoUpdateInterval: > 0 }
            ? DateTimeOffset.FromUnixTimeSeconds(sub.UpdateTime + sub.AutoUpdateInterval * 60L)
            : null;
        pnlSubInfo.IsVisible = info != null || nextUpdate != null;

        var parts = new List<string>();
        var expired = false;
        if (info != null)
        {
            if (info.Total > 0)
            {
                parts.Add(string.Format(ResUI.TbSubUsed, FormatBytes(info.Used), FormatBytes(info.Total)));
                parts.Add(string.Format(ResUI.TbSubLeft, FormatBytes(info.Left)));
            }
            else if (info.Used > 0)
            {
                parts.Add(string.Format(ResUI.TbSubUsedNoLimit, FormatBytes(info.Used)));
            }
            if (info.Expires is { } expires)
            {
                var remaining = expires - DateTimeOffset.UtcNow;
                expired = remaining <= TimeSpan.Zero;
                parts.Add(expired ? ResUI.TbSubExpired
                    : remaining.TotalHours < 24 ? ResUI.TbSubExpiresToday
                    : string.Format(ResUI.TbSubDaysLeft, (int)remaining.TotalDays));
            }
            parts.Add(string.Format(ResUI.TbSubUpdated, Stamp(info.Updated)));
        }
        if (nextUpdate is { } next)
        {
            parts.Add(string.Format(ResUI.TbSubNextUpdate, Stamp(next < DateTimeOffset.UtcNow ? DateTimeOffset.UtcNow : next)));
        }

        txtSubInfo.Text = string.Join("  ·  ", parts);
        barSubUsage.IsVisible = info is { Total: > 0 };
        barSubUsage.Value = info is { Total: > 0 } ? Math.Min(1, (double)info.Used / info.Total) : 0;
        // Red when it's over or nearly used up, so it's noticed before the servers stop working.
        txtSubInfo.Classes.Set("bad", expired || (info is { Total: > 0 } && info.Left < info.Total / 10));
    }

    private static string Stamp(DateTimeOffset time)
    {
        return $"‎{time.ToLocalTime().ToString("MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture)}‎";
    }

    private readonly HashSet<string> _subWarned = [];

    // One toast per subscription and problem per run: before the plan or its data runs out.
    private void WarnAboutSubscriptions()
    {
        if (ViewModel == null)
        {
            return;
        }
        var now = DateTimeOffset.UtcNow;
        foreach (var sub in ViewModel.SubItems.Where(s => s.Url.IsNotEmpty()).ToList())
        {
            if (SubscriptionInfoStore.Get(Utils.GetPunycode(sub.Url.TrimEx())) is not { } info)
            {
                continue;
            }
            var kind = SubscriptionInfoStore.Warning(info, now);
            if (kind == SubscriptionInfoStore.WarningKind.None || !_subWarned.Add($"{sub.Id}:{kind}"))
            {
                continue;
            }
            NoticeManager.Instance.Enqueue(kind switch
            {
                SubscriptionInfoStore.WarningKind.ExpiresSoon => string.Format(ResUI.TipSubExpiresSoonWarn, sub.Remarks,
                    Math.Max(1, (int)Math.Ceiling((info.Expires!.Value - now).TotalDays))),
                SubscriptionInfoStore.WarningKind.Expired => string.Format(ResUI.TipSubExpiredWarn, sub.Remarks),
                SubscriptionInfoStore.WarningKind.LowData => string.Format(ResUI.TipSubLowDataWarn, sub.Remarks, FormatBytes(info.Left)),
                _ => string.Format(ResUI.TipSubNoDataWarn, sub.Remarks),
            });
        }
    }

    // Upstream's Utils.HumanFy starts at KB, so 500 bytes would read "500.0 KB"; and the LRM marks
    // keep "1.5 GB" in order inside Persian text.
    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return $"‎{size.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)} {units[unit]}‎";
    }

    private void UpdateEmptyState()
    {
        if (ViewModel is { } vm)
        {
            var show = vm.ProfileItems.Count == 0 && vm.ServerFilter.IsNullOrEmpty();
            if (show && !spEmptyServers.IsVisible)
            {
                AppMotion.PlayEntrance(spEmptyServers, 14, 380);
            }
            spEmptyServers.IsVisible = show;
            // A search with no results says so, instead of an empty list.
            var noMatch = vm.ProfileItems.Count == 0 && vm.ServerFilter.IsNotEmpty();
            txtNoServersMatch.Text = noMatch ? string.Format(ResUI.TipNoServersMatch, vm.ServerFilter) : string.Empty;
            txtNoServersMatch.IsVisible = noMatch;
            // An empty table has nothing to scroll sideways to; hide the bar under the first-run card.
            lstProfiles.HorizontalScrollBarVisibility = show
                ? Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden
                : Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        }
    }

    private async void LstProfiles_Sorting(object? sender, DataGridColumnEventArgs e)
    {
        e.Handled = true;

        if (ViewModel != null && e.Column?.Tag?.ToString() != null)
        {
            await ViewModel.SortServer(e.Column.Tag.ToString());
        }

        e.Handled = false;
    }

    #region Event

    public async Task ShareServer(string url)
    {
        if (url.IsNullOrEmpty())
        {
            return;
        }

        var dialog = new QrcodeView(url);
        await DialogHost.Show(dialog);
    }

    public void RefreshServersBiz()
    {
        if (lstProfiles.SelectedIndex >= 0)
        {
            lstProfiles.ScrollIntoView(lstProfiles.SelectedItem, null);
        }
    }

    private void lstProfiles_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ViewModel != null)
        {
            ViewModel.SelectedProfiles = lstProfiles.SelectedItems.Cast<ProfileItemModel>().ToList();
        }
    }

    private void LstProfiles_DoubleTapped(object? sender, TappedEventArgs e)
    {
        var source = e.Source as Border;
        if (source?.Name == "HeaderBackground")
        {
            return;
        }

        if (_config.UiItem.DoubleClick2Activate)
        {
            ViewModel?.SetDefaultServer();
        }
        else
        {
            ViewModel?.EditServerAsync();
        }
    }

    //private void LstProfiles_ColumnHeader_Click(object? sender, RoutedEventArgs e)
    //{
    //    var colHeader = sender as DataGridColumnHeader;
    //    if (colHeader == null || colHeader.TabIndex < 0 || colHeader.Column == null)
    //    {
    //        return;
    //    }

    //    var colName = ((MyDGTextColumn)colHeader.Column).ExName;
    //    ViewModel?.SortServer(colName);
    //}

    private void menuSelectAll_Click(object? sender, RoutedEventArgs e)
    {
        lstProfiles.SelectAll();
    }

    private void LstProfiles_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers is KeyModifiers.Control or KeyModifiers.Meta)
        {
            switch (e.Key)
            {
                case Key.A:
                    menuSelectAll_Click(null, null);
                    break;

                case Key.C:
                    ViewModel?.Export2ShareUrlAsync(false);
                    break;

                case Key.D:
                    ViewModel?.EditServerAsync();
                    break;

                case Key.F:
                    ViewModel?.ShareServerAsync();
                    break;

                case Key.O:
                    ViewModel?.ServerSpeedtest(ESpeedActionType.Tcping);
                    break;

                case Key.R:
                    ViewModel?.ServerSpeedtest(ESpeedActionType.Realping);
                    break;

                case Key.T:
                    ViewModel?.ServerSpeedtest(ESpeedActionType.Speedtest);
                    break;

                case Key.E:
                    ViewModel?.ServerSpeedtest(ESpeedActionType.Mixedtest);
                    break;
            }
        }
        else
        {
            switch (e.Key)
            {
                case Key.Enter:
                    //case Key.Return:
                    ViewModel?.SetDefaultServer();
                    break;

                case Key.Delete:
                case Key.Back:
                    ViewModel?.RemoveServerAsync();
                    break;

                case Key.T:
                    ViewModel?.MoveServer(EMove.Top);
                    break;

                case Key.U:
                    ViewModel?.MoveServer(EMove.Up);
                    break;

                case Key.D:
                    ViewModel?.MoveServer(EMove.Down);
                    break;

                case Key.B:
                    ViewModel?.MoveServer(EMove.Bottom);
                    break;

                case Key.Escape:
                    ViewModel?.ServerSpeedtestStop();
                    break;
            }
        }
    }

    private void BtnAutofitColumnWidth_Click(object? sender, RoutedEventArgs e)
    {
        AutofitColumnWidth();
    }

    private void AutofitColumnWidth()
    {
        try
        {
            //First scroll horizontally to the initial position to avoid the control crash bug
            if (lstProfiles.SelectedIndex >= 0)
            {
                lstProfiles.ScrollIntoView(lstProfiles.SelectedItem, lstProfiles.Columns[0]);
            }
            else
            {
                var model = lstProfiles.ItemsSource.Cast<ProfileItemModel>();
                if (model.Any())
                {
                    lstProfiles.ScrollIntoView(model.First(), lstProfiles.Columns[0]);
                }
                else
                {
                    return;
                }
            }

            foreach (var it in lstProfiles.Columns)
            {
                it.Width = new DataGridLength(1, DataGridLengthUnitType.Auto);
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
    }

    private readonly DispatcherTimer _filterTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };

    private void TxtServerFilter_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Return)
        {
            ViewModel?.RefreshServers();
        }
    }

    #endregion Event

    #region UI

    private void RestoreUI()
    {
        try
        {
            var lvColumnItem = _config.UiItem.MainColumnItem.OrderBy(t => t.Index).ToList();
            var displayIndex = 0;
            foreach (var item in lvColumnItem)
            {
                foreach (var item2 in lstProfiles.Columns)
                {
                    if (item2.Tag == null)
                    {
                        continue;
                    }
                    if (item2.Tag.Equals(item.Name))
                    {
                        if (item.Width < 0)
                        {
                            item2.IsVisible = false;
                        }
                        else
                        {
                            item2.Width = new DataGridLength(item.Width, DataGridLengthUnitType.Pixel);
                            item2.DisplayIndex = displayIndex++;
                        }
                        if (item.Name.StartsWith("to", StringComparison.CurrentCultureIgnoreCase))
                        {
                            item2.IsVisible = _config.GuiItem.EnableStatistics;
                        }
                        if (item.Name.Equals("IpInfo", StringComparison.CurrentCultureIgnoreCase))
                        {
                            item2.IsVisible = _config.SpeedTestItem.IPAPIUrl.IsNotEmpty() && !_config.UiItem.HideColumnIpInfo;
                        }
                    }
                }
            }
            EnsureHeaderWidths();
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
    }

    // Saved widths come from older layouts and can be narrower than the header text ("Delay (…"),
    // so no column may shrink below its own header. Measured, so it holds for every language.
    private void EnsureHeaderWidths()
    {
        var fontSize = this.TryFindResource("AppFontSmall", out var size) && size is double d ? d : 12;
        foreach (var column in lstProfiles.Columns)
        {
            if (column.Header is not string header || header.IsNullOrEmpty())
            {
                continue;
            }
            var probe = new TextBlock { Text = header, FontSize = fontSize, FontWeight = FontWeight.SemiBold };
            probe.Measure(Size.Infinity);
            // 16px header padding on each side plus room for the sort arrow.
            column.MinWidth = Math.Ceiling(probe.DesiredSize.Width) + 48;
        }
    }

    private async void CopyIpInfo_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is MenuItem { CommandParameter: string text } && !text.IsNullOrEmpty())
            {
                await AvaUtils.SetClipboardData(this, text);
            }
        }
        catch
        {
            // Ignore
        }
    }

    private void StorageUI()
    {
        // In the list view the table's columns are all hidden; keep the layout saved before that.
        if (_listView)
        {
            return;
        }
        try
        {
            List<ColumnItem> lvColumnItem = [];
            foreach (var item2 in lstProfiles.Columns)
            {
                if (item2.Tag == null)
                {
                    continue;
                }
                lvColumnItem.Add(new()
                {
                    Name = (string)item2.Tag,
                    Width = (int)(item2.IsVisible == true ? item2.ActualWidth : -1),
                    Index = item2.DisplayIndex
                });
            }
            _config.UiItem.MainColumnItem = lvColumnItem;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
    }

    #endregion UI

    private sealed class ProfileRowDropHandler(ProfilesViewModel viewModel) : IDataGridRowDropHandler
    {
        private readonly DataGridRowReorderHandler _reorderHandler = new();

        public bool Validate(DataGridRowDropEventArgs args)
        {
            ArgumentNullException.ThrowIfNull(args);
            return _reorderHandler.Validate(args);
        }

        public bool Execute(DataGridRowDropEventArgs args)
        {
            ArgumentNullException.ThrowIfNull(args);

            if (!_reorderHandler.Execute(args))
            {
                return false;
            }

            if (viewModel?.ProfileItems != null)
            {
                for (var index = 0; index < viewModel.ProfileItems.Count; index++)
                {
                    var item = viewModel.ProfileItems[index];
                    var sort = (index + 1) * 10;
                    item.Sort = sort;
                    ProfileExManager.Instance.SetSort(item.IndexId, sort);
                }
            }

            return true;
        }
    }
}
