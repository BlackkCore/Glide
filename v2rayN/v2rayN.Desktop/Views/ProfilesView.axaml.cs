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
        SetupSubPicker();
        // Sort without the table's column headers (upstream's own sorting, by column tag).
        menuSortFastest.Click += async (_, _) => await Guarded(() => ViewModel?.SortServer("DelayVal") ?? Task.CompletedTask, ResUI.TipSortFailed);
        menuSortName.Click += async (_, _) => await Guarded(() => ViewModel?.SortServer("Remarks") ?? Task.CompletedTask, ResUI.TipSortFailed);
        // This subscription only, right away (the main menu updates all of them).
        menuSubUpdateNow.Click += async (_, _) => await Guarded(async () =>
        {
            if (ViewModel?.SelectedSub is { Id.Length: > 0 } sub && TopLevel.GetTopLevel(this) is MainWindow { ViewModel: { } main })
            {
                await main.UpdateSubscriptionProcess(sub.Id, false);
            }
        }, ResUI.TipSubUpdateFailed);
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
            }, ResUI.TipSubIntervalFailed);
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
        lstProfiles.AddHandler(KeyDownEvent, LstProfiles_AltMove, RoutingStrategies.Tunnel);
        lstProfiles.SelectionChanged += lstProfiles_SelectionChanged;
        lstProfiles.DoubleTapped += LstProfiles_DoubleTapped;
        lstProfiles.Sorting += LstProfiles_Sorting;
        SizeChanged += (_, e) =>
        {
            // Crossing the 400px line shows or hides the fastest-server card at once.
            if ((e.PreviousSize.Height >= 400) != (e.NewSize.Height >= 400))
            {
                UpdateSuggest();
            }
        };

        _suggestTimer.Tick += (_, _) => UpdateSuggest();
        btnSuggestUse.Click += BtnSuggestUse_Click;

        void SetTab(int mode)
        {
            if (ViewModel == null)
            {
                return;
            }
            ViewModel.ListMode = mode;
            SyncTabButtons(mode);
            _ = ViewModel.RefreshServers();
        }
        btnTabAll.Click += (_, _) => SetTab(0);
        btnTabFav.Click += (_, _) => SetTab(1);
        btnTabFast.Click += (_, _) => SetTab(2);

        this.WhenActivated(disposables =>
        {
            this.OneWayBind(ViewModel, vm => vm.ProfileItems, v => v.lstProfiles.ItemsSource).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SelectedProfile, v => v.lstProfiles.SelectedItem).DisposeWith(disposables);

            // Set directly, not through a {Binding}: the list sits in the picker's popup, which drops its
            // DataContext when it closes. A bound ItemsSource then went empty and cleared the chosen
            // subscription.
            lstGroup.ItemsSource = ViewModel.SubItems;
            this.Bind(ViewModel, vm => vm.SelectedSub, v => v.lstGroup.SelectedItem).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.ServerFilter, v => v.txtServerFilter.Text).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddSubCmd, v => v.btnAddSub).DisposeWith(disposables);

            SyncTabButtons(ViewModel.ListMode);

            // The view model and the top bar live as long as the app, and in the tab layout this view leaves
            // the visual tree whenever another tab is chosen. These two hooks must keep working then (the
            // top bar's subscription pill, edit and add buttons, and its usage numbers), so they are added
            // once and never removed.
            if (!_appLifetimeHooked)
            {
                _appLifetimeHooked = true;
                // Empty list with no filter typed: show the first-run card instead of a blank table.
                // A subscription update reloads the list, so the provider's new numbers show up then.
                ViewModel.ProfileItems.CollectionChanged += (_, _) => Dispatcher.UIThread.Post(() =>
                {
                    UpdateEmptyState();
                    UpdateSubInfo();
                    WarnAboutSubscriptions();
                    UpdateSuggest();
                });
                // The top bar asks for the subscription list and the subscription actions. The view model's
                // commands are used directly: the button bindings are disposed while this view is hidden.
                SubActionRequested += (action, target) => Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        switch (action)
                        {
                            case "pick":
                                btnSubPicker.Flyout?.ShowAt(target ?? btnSubPicker);
                                break;
                            case "edit":
                                if (ViewModel?.SelectedSub is { Id.Length: > 0 })
                                {
                                    ViewModel.EditSubCmd.Execute().Subscribe();
                                }
                                break;
                            case "add":
                                ViewModel?.AddSubCmd.Execute().Subscribe();
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logging.SaveLog(_tag, ex);
                    }
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

            // Offer the fastest tested server; delays arrive one by one, so look again every couple of seconds.
            _suggestTimer.Start();
            UpdateSuggest();

            disposables.Add(new ActionDisposable(() =>
            {
                _suggestTimer.Stop();
                _filterTimer.Stop();
            }));
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
                // Every question this list asks is a deletion: red "Delete", and Enter cancels.
                var message = UiDigits.Localize(interaction.Input); // "۳ سرور" in Persian
                var result = await UI.ShowYesNo(message, ResUI.TbDeleteConfirm, ResUI.TbCancel, destructive: true);
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

    private bool _appLifetimeHooked;

    private void SyncTabButtons(int mode)
    {
        btnTabAll.Classes.Set("on", mode == 0);
        btnTabFav.Classes.Set("on", mode == 1);
        btnTabFast.Classes.Set("on", mode == 2);
    }

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
        lstProfiles.HorizontalScrollBarVisibility = list
            ? Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
            : Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
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
        // In a narrow panel "Test all" keeps its lightning icon and tooltip but drops the word, so the title row fits.
        // The sort and view buttons moved into one menu, so "Test all" keeps its word down to 300px.
        txtTestAll.IsVisible = width >= 300;
        // Narrow list: the "Active" word shrinks to a small dot, so the server's name keeps its room.
        lstProfiles.Classes.Set("narrowRows", width < 400);
        menuMixedTestServer.IsVisible = width >= 640;
        btnAutofitColumnWidth.IsVisible = width >= 600 && !lstProfiles.Classes.Contains("list");
        btnToggleView.IsVisible = width >= 560;
        // No empty bar when every button in it is hidden.
        bdTools.IsVisible = menuMixedTestServer.IsVisible || btnToggleView.IsVisible || btnAutofitColumnWidth.IsVisible || colChooser.IsVisible;
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
            (menuMoveSub, "IconArrowUp"),
            (menuShareServer, "IconScan"),
            (menuExportSub, "IconOutbound"),
            (menuRemoveServer, "IconTrash"),
            (menuSortServerResult, "IconArrowDown"),
            (menuSelectAll, "IconApps"),
            (menuRemoveDuplicateServer, "IconLayers"),
            (menuRemoveInvalidServerResult, "IconTrash"),
            (menuGenGroup, "IconLayers"),
            (menuSort, "IconArrowDown"),
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
    // failure: what to tell the user, naming what didn't work.
    private async Task Guarded(Func<Task> action, string? failure = null)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            NoticeManager.Instance.Enqueue(failure ?? ResUI.OperationFailed);
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
                NoticeManager.Instance.Enqueue(ResUI.TipAutoFastestFailed);
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
            NoticeManager.Instance.Enqueue(ResUI.TipAutoFastestFailed);
        }
    }

    // ---- Subscription picker: one button with the chosen subscription; the list (searchable when
    // long) opens below it, so many subscriptions never wrap into rows of chips.
    private const int SubSearchFrom = 7;

    private Flyout SubFlyout => (Flyout)btnSubPicker.Flyout!;

    private void SetupSubPicker()
    {
        SubFlyout.Opened += (_, _) =>
        {
            var many = (ViewModel?.SubItems.Count ?? 0) >= SubSearchFrom;
            txtSubSearch.IsVisible = many;
            txtSubSearch.Text = string.Empty;
            FilterSubs(string.Empty);
            Dispatcher.UIThread.Post(() =>
            {
                if (lstGroup.SelectedItem is { } selected)
                {
                    lstGroup.ScrollIntoView(selected);
                }
                if (many)
                {
                    txtSubSearch.Focus();
                }
                else
                {
                    (lstGroup.ContainerFromIndex(Math.Max(0, lstGroup.SelectedIndex)) as Control)?.Focus(NavigationMethod.Directional);
                }
            }, DispatcherPriority.Background);
        };
        txtSubSearch.TextChanged += (_, _) => FilterSubs(txtSubSearch.Text);
        // The list reuses row containers while scrolling: apply the search to each row as it is filled,
        // and clear it as it is given up, so a reused row never keeps another subscription's visibility.
        lstGroup.ContainerPrepared += (_, e) =>
        {
            if (e.Index >= 0 && e.Index < lstGroup.ItemCount && lstGroup.Items[e.Index] is SubItem sub)
            {
                e.Container.IsVisible = SubMatches(sub, txtSubSearch.Text?.Trim() ?? string.Empty);
            }
        };
        lstGroup.ContainerClearing += (_, e) => e.Container.IsVisible = true;
        // Enter in the search box picks the first match; Down moves into the list.
        txtSubSearch.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && FirstVisibleSub() is { } first)
            {
                lstGroup.SelectedItem = first;
                SubFlyout.Hide();
                e.Handled = true;
            }
            else if (e.Key == Key.Down && FirstVisibleSub() is { } next)
            {
                (lstGroup.ContainerFromItem(next) as Control)?.Focus(NavigationMethod.Directional);
                e.Handled = true;
            }
        };
        // A left click or Enter on a row picks it and closes the list; right-click keeps it open for the menu.
        lstGroup.AddHandler(PointerReleasedEvent, (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Left && e.Source is Visual v && Avalonia.VisualTree.VisualExtensions.FindAncestorOfType<ListBoxItem>(v, true) != null)
            {
                SubFlyout.Hide();
            }
        }, RoutingStrategies.Bubble, handledEventsToo: true);
        lstGroup.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                SubFlyout.Hide();
                e.Handled = true;
            }
        };
        if (lstGroup.ContextMenu is { } subMenu)
        {
            subMenu.Opening += (_, _) =>
            {
                var hasSub = ViewModel?.SelectedSub is { Id.Length: > 0 };
                var hasUrl = hasSub && ViewModel?.SelectedSub?.Url.IsNotEmpty() == true;
                menuSubEdit.IsEnabled = hasSub;
                menuSubDelete.IsEnabled = hasSub;
                menuSubSendToPhone.IsEnabled = hasUrl;
                menuSubUpdateNow.IsEnabled = hasUrl;
                menuSubAutoUpdate.IsEnabled = hasSub;
            };
        }
    }

    private object? FirstVisibleSub()
    {
        // From the data, not the rows: a row's container may not exist (or be reused) yet.
        var query = txtSubSearch.Text?.Trim() ?? string.Empty;
        return lstGroup.Items.OfType<SubItem>().FirstOrDefault(s => SubMatches(s, query));
    }

    private static bool SubMatches(SubItem sub, string query)
    {
        return query.Length == 0
            || (sub.Remarks?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
            || (Converters.SubHostConverter.Host.Convert(sub.Url, typeof(string), null, CultureInfo.CurrentCulture) as string)?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;
    }

    private void FilterSubs(string? query)
    {
        query = query?.Trim() ?? string.Empty;
        var any = false;
        foreach (var sub in lstGroup.Items.OfType<SubItem>())
        {
            var match = SubMatches(sub, query);
            if (lstGroup.ContainerFromItem(sub) is Control container)
            {
                container.IsVisible = match;
            }
            any |= match;
        }
        txtSubNoMatch.IsVisible = !any;
    }

    private void UpdateSubPicker()
    {
        var sub = ViewModel?.SelectedSub;
        txtSubPicked.Text = sub?.Remarks.IsNotEmpty() == true ? sub.Remarks : ResUI.AllGroupServers;
        ToolTip.SetTip(btnSubPicker, $"{ResUI.TipSubPicker}: {txtSubPicked.Text}");
    }

    private void UpdateSubInfo()
    {
        UpdateSubPicker();
        // Auto needs one subscription to choose from, so not on "All servers".
        var sub = ViewModel?.SelectedSub;
        btnAutoFastest.IsVisible = sub?.Id.IsNotEmpty() == true;
        // Auto lives in the suggestion card, which shows for it alone too.
        UpdateSuggest();
        // Same key the download used (SubscriptionHandler fetches the punycode form of the URL).
        var info = sub == null || sub.Url.IsNullOrEmpty() ? null : SubscriptionInfoStore.Get(Utils.GetPunycode(sub.Url.TrimEx()));
        // Upstream refreshes a subscription every AutoUpdateInterval minutes (TaskManager): say when.
        DateTimeOffset? nextUpdate = sub is { AutoUpdateInterval: > 0 }
            ? DateTimeOffset.FromUnixTimeSeconds(sub.UpdateTime + sub.AutoUpdateInterval * 60L)
            : null;
        // Split between two places:
        // - this panel, above the list: what's left, when it expires, when it was updated;
        // - the main window's top bar: the usage bar, how much was used, and the next automatic update.
        var panelParts = new List<string>();
        var topParts = new List<string>();
        var expired = false;
        if (info != null)
        {
            if (info.Total > 0)
            {
                topParts.Add(string.Format(ResUI.TbSubUsed, FormatBytes(info.Used), FormatBytes(info.Total)));
                panelParts.Add(string.Format(ResUI.TbSubLeft, FormatBytes(info.Left)));
            }
            else if (info.Used > 0)
            {
                topParts.Add(string.Format(ResUI.TbSubUsedNoLimit, FormatBytes(info.Used)));
            }
            if (info.Expires is { } expires)
            {
                var remaining = expires - DateTimeOffset.UtcNow;
                expired = remaining <= TimeSpan.Zero;
                panelParts.Add(expired ? ResUI.TbSubExpired
                    : remaining.TotalHours < 24 ? ResUI.TbSubExpiresToday
                    : string.Format(ResUI.TbSubDaysLeft, (int)remaining.TotalDays));
            }
            panelParts.Add(string.Format(ResUI.TbSubUpdated, Stamp(info.Updated)));
        }
        if (nextUpdate is { } next)
        {
            topParts.Add(string.Format(ResUI.TbSubNextUpdate, Stamp(next < DateTimeOffset.UtcNow ? DateTimeOffset.UtcNow : next)));
        }

        // Persian digits for the day count too (amounts and dates are already localized).
        txtSubInfo.Text = UiDigits.Localize(string.Join("  ·  ", panelParts));
        pnlSubInfo.IsVisible = panelParts.Count > 0;
        // The usage bar stays in the top bar only.
        barSubUsage.IsVisible = false;
        double? usage = info is { Total: > 0 } ? Math.Min(1, (double)info.Used / info.Total) : null;
        // Red when it's over or nearly used up, so it's noticed before the servers stop working.
        txtSubInfo.Classes.Set("bad", expired || (info is { Total: > 0 } && info.Left < info.Total / 10));
        var topText = topParts.Count > 0 ? UiDigits.Localize(string.Join("  ·  ", topParts)) : null;
        SubInfoChanged?.Invoke(txtSubPicked.Text, topText, usage, btnAutoFastest.IsVisible);
    }

    /// <summary>The chosen subscription's name, usage text, used fraction (null when unknown) and whether "Auto: fastest" applies; the top bar shows them.</summary>
    public static event Action<string?, string?, double?, bool>? SubInfoChanged;

    private static event Action<string, Control?>? SubActionRequested;

    /// <summary>Does a subscription action from the top bar: "pick" opens the list under the given control, "edit", "add".</summary>
    public static void RequestSubscriptionAction(string action, Control? target = null) => SubActionRequested?.Invoke(action, target);

    private static string Stamp(DateTimeOffset time)
    {
        return $"‎{UiDigits.Localize(time.ToLocalTime().ToString("MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture))}‎";
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
        return $"‎{UiDigits.Localize(size.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture))} {units[unit]}‎";
    }

    private readonly DispatcherTimer _suggestTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private string? _suggestId;

    private void UpdateSuggest()
    {
        // P-01: hidden to the tray the view stays "active", so the timer keeps ticking; skip the work.
        if (TopLevel.GetTopLevel(this) is Window { IsVisible: false })
        {
            return;
        }
        ProfileItemModel? best = null;
        var bestMs = int.MaxValue;
        var activeMs = 0;
        foreach (var item in ViewModel?.ProfileItems ?? [])
        {
            if (int.TryParse(item.DelayVal, out var ms) && ms > 0)
            {
                if (item.IsActive)
                {
                    activeMs = ms;
                }
                if (ms < bestMs || (ms == bestMs && item.IsActive))
                {
                    best = item;
                    bestMs = ms;
                }
            }
        }
        // The globe shows a dim point for each country the saved servers are in.
        var remarks = ViewModel?.UnfilteredRemarks is { Count: > 0 } unfiltered
            ? unfiltered
            : (ViewModel?.ProfileItems ?? []).Select(i => i.Remarks);
        GlobeView.SetServers(remarks.Select(CountryDetector.Detect));
        // On a short panel (a small window with the logs open) the card would leave room for one server row,
        // so it steps aside; the fastest server is still one click away on the Fastest tab.
        var roomy = Bounds.Height <= 0 || Bounds.Height >= 400;
        var items = ViewModel?.ProfileItems.ToList() ?? [];

        // First the server you usually use at this hour (when it isn't already the one in use).
        var usualId = UsageHabits.UsualNow(items.Select(i => i.IndexId));
        var usual = items.FirstOrDefault(i => i.IndexId == usualId && !i.IsActive);
        // Otherwise the fastest tested server, but only when it is clearly faster (50 ms or more) than
        // the one in use, or the one in use hasn't answered a test: a few ms isn't worth a switch.
        var fastest = best is { IsActive: false } && (activeMs <= 0 || bestMs + 50 <= activeMs) ? best : null;
        var pick = usual ?? fastest;
        // One card: the suggestion (with Connect) and/or Auto (when a subscription is chosen).
        var auto = btnAutoFastest.IsVisible;
        var show = roomy && (pick != null || auto);
        bdSuggest.IsVisible = show;
        btnSuggestUse.IsVisible = pick != null;
        if (!show)
        {
            _suggestId = null;
            return;
        }
        if (pick == null)
        {
            // Nothing to suggest right now: the card only offers Auto. One explaining sentence that wraps
            // (no caption: the button already says "Auto").
            _suggestId = null;
            txtSuggestCaption.IsVisible = false;
            iconSuggest.Data = this.FindResource("SemiIconBolt") as Geometry;
            SetSuggestNameStyle(sentence: true);
            txtSuggestName.Text = ResUI.TipAutoFastestCard;
            txtSuggestMs.Text = string.Empty;
            return;
        }
        txtSuggestCaption.IsVisible = true;
        SetSuggestNameStyle(sentence: false);
        _suggestId = pick.IndexId;
        txtSuggestCaption.Text = usual != null ? ResUI.TbUsualServer : ResUI.TbSortFastest;
        iconSuggest.Data = this.FindResource(usual != null ? "IconRoute" : "SemiIconBolt") as Geometry;
        txtSuggestName.Text = Converters.ServerNameDisplay.Clean(pick.Remarks);
        txtSuggestMs.Text = int.TryParse(pick.DelayVal, out var pickMs) && pickMs > 0
            ? $"{UiDigits.Localize(pickMs.ToString(CultureInfo.InvariantCulture))} ms"
            : string.Empty;
    }

    // A server name: one bold line, cut with "…" so the delay beside it stays visible.
    // The Auto explanation: a normal sentence that wraps onto a second line instead of being cut.
    private void SetSuggestNameStyle(bool sentence)
    {
        txtSuggestName.MaxWidth = sentence ? double.PositiveInfinity : 170;
        txtSuggestName.TextWrapping = sentence ? TextWrapping.Wrap : TextWrapping.NoWrap;
        txtSuggestName.TextTrimming = sentence ? TextTrimming.None : TextTrimming.CharacterEllipsis;
        txtSuggestName.FontWeight = sentence ? FontWeight.Normal : FontWeight.SemiBold;
        txtSuggestMs.IsVisible = !sentence;
    }

    private async void BtnSuggestUse_Click(object? sender, RoutedEventArgs e)
    {
        if (_suggestId != null && ViewModel != null)
        {
            await Guarded(() => ViewModel.SetDefaultServer(_suggestId), ResUI.TipSwitchServerFailed);
        }
    }

    private void UpdateEmptyState()
    {
        if (ViewModel is { } vm)
        {
            var show = vm.ProfileItems.Count == 0 && vm.ServerFilter.IsNullOrEmpty() && vm.ListMode == 0;
            if (show && !spEmptyServers.IsVisible)
            {
                AppMotion.PlayEntrance(spEmptyServers, 14, 380);
            }
            spEmptyServers.IsVisible = show;
            // A search or filtered tab with no results says why, instead of showing an empty list.
            var noMatch = vm.ProfileItems.Count == 0 && (vm.ServerFilter.IsNotEmpty() || vm.ListMode != 0);
            if (!noMatch)
            {
                txtNoServersMatch.Text = string.Empty;
            }
            else if (vm.ServerFilter.IsNotEmpty())
            {
                txtNoServersMatch.Text = string.Format(ResUI.TipNoServersMatch, vm.ServerFilter);
            }
            else
            {
                txtNoServersMatch.Text = vm.ListMode == 1 ? ResUI.TipNoFavorites : ResUI.TipNoTestedServers;
            }
            txtNoServersMatch.IsVisible = noMatch;
            // An empty table has nothing to scroll sideways to; hide the bar under the first-run card.
            // The list view never scrolls sideways (its rows fit the panel), so it keeps the bar off.
            lstProfiles.HorizontalScrollBarVisibility = lstProfiles.Classes.Contains("list")
                ? Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
                : (show || noMatch)
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

        // Double-click uses the server (connects through it when connected). Editing stays on the
        // right-click / ⋯ menu and Ctrl+D, so the old "double-click to activate" setting is gone.
        ViewModel?.SetDefaultServer();
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

                case Key.Escape:
                    ViewModel?.ServerSpeedtestStop();
                    break;
            }
        }
    }

    // Moving servers needs Alt + Home / Up / Down / End, so a stray letter typed into the list never
    // reorders it. Seen before the grid, which would otherwise use the arrows to move the selection.
    private void LstProfiles_AltMove(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers == KeyModifiers.Alt)
        {
            switch (e.Key)
            {
                case Key.Home:
                    ViewModel?.MoveServer(EMove.Top);
                    e.Handled = true;
                    break;

                case Key.Up:
                    ViewModel?.MoveServer(EMove.Up);
                    e.Handled = true;
                    break;

                case Key.Down:
                    ViewModel?.MoveServer(EMove.Down);
                    e.Handled = true;
                    break;

                case Key.End:
                    ViewModel?.MoveServer(EMove.Bottom);
                    e.Handled = true;
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
            var probe = new TextBlock { Text = header, FontSize = fontSize, FontWeight = FontWeight.Medium };
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
