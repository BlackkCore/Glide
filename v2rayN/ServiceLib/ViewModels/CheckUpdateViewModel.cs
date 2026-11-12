namespace ServiceLib.ViewModels;

public partial class CheckUpdateViewModel : MyReactiveObject
{
    private const string _geo = "GeoFiles";
    private readonly ECoreType _v2rayN = ECoreType.v2rayN;
    private List<CheckUpdateModel> _lstUpdated = [];
    private static readonly string _tag = "CheckUpdateViewModel";

    public EventChannel<RxVoid> ReloadRequested { get; } = new();

    public BulkObservableCollection<CheckUpdateModel> CheckUpdateModels { get; } = [];
    public ReactiveCommand<RxVoid, RxVoid> CheckUpdateCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> CheckOnlyCmd { get; }

    [Reactive] public partial bool EnableUpdateViaProxy { get; set; }

    public CheckUpdateViewModel()
    {
        _config = AppManager.Instance.Config;

        CheckUpdateCmd = ReactiveCommand.CreateFromTask(CheckUpdate);
        CheckUpdateCmd.ThrownExceptions.Subscribe(ex =>
        {
            Logging.SaveLog(_tag, ex);
            _ = UpdateView(_v2rayN, ex.Message);
        });

        CheckOnlyCmd = ReactiveCommand.CreateFromTask(CheckOnly);
        CheckOnlyCmd.ThrownExceptions.Subscribe(ex =>
        {
            Logging.SaveLog(_tag, ex);
            _ = UpdateView(_v2rayN, ex.Message);
        });

        EnableUpdateViaProxy = _config.CheckUpdateItem.UpdateViaProxy;

        this.WhenAnyValue(x => x.EnableUpdateViaProxy)
            .Subscribe(c => _ = OnUpdateViaProxyChanged());

        AppEvents.HasUpdateNotified
         .AsObservable()
         .ObserveOn(RxSchedulers.MainThreadScheduler)
         .Subscribe(bl => RefreshCheckUpdateItems(bl));

        RefreshCheckUpdateItems(true);
    }

    private void RefreshCheckUpdateItems(bool hasUpdate)
    {
        if (!hasUpdate)
        {
            return;
        }

        var models = CoreInfoManager.Instance.GetCheckUpdateCoreTypes()
                        .Select(t => GetCheckUpdateModel(t))
                        .ToList();

        models.Add(GetGeoFileCheckUpdateModel());

        CheckUpdateModels.ReplaceRange(models);
        _ = ShowInstalledVersions();
    }

    private static bool IsEngine(ECoreType? type) => type is ECoreType.Xray or ECoreType.mihomo or ECoreType.sing_box;

    /// <summary>What is installed, or that the engine is missing and Update all will install it (verified).</summary>
    private async Task ShowInstalledVersions()
    {
        foreach (var type in CheckUpdateModels.Select(m => m.CoreType).Where(IsEngine).ToList())
        {
            if (AppManager.Instance.LastCheckUpdateResults.ContainsKey(type!.Value))
            {
                continue;
            }
            var installed = await UpdateService.InstalledCoreVersion(type.Value);
            await UpdateView(type, installed.Equals(new SemanticVersion(0, 0, 0))
                ? ResUI.TipCoreNotInstalled
                : string.Format(ResUI.TipInstalledVersion, installed.ToStandardVersionString("v")));
        }
    }

    private static async Task<string> AvailableMessage(ECoreType type, SemanticVersion available)
    {
        var name = Global.CoreDisplayName(type);
        if (!IsEngine(type))
        {
            return string.Format(ResUI.MsgCheckUpdateHasNewVersion, name, available);
        }
        var installed = await UpdateService.InstalledCoreVersion(type);
        return installed.Equals(new SemanticVersion(0, 0, 0))
            ? $"{ResUI.TipCoreNotInstalled} {string.Format(ResUI.MsgCheckUpdateHasNewVersion, name, available)}"
            : string.Format(ResUI.TipCoreUpdateAvailable, installed.ToStandardVersionString("v"), available.ToStandardVersionString("v"));
    }

    private CheckUpdateModel GetCheckUpdateModel(ECoreType coreType)
    {
        if (coreType == _v2rayN && Utils.IsPackagedInstall())
        {
            return new()
            {
                IsSelected = false,
                IsCheckPreRelease = false,
                ShowCheckPreRelease = false,
                CoreType = coreType,
                IsGeoFile = false,
                Remarks = ResUI.menuCheckUpdate + $" ({ResUI.MsgNotSupport})",
            };
        }

        AppManager.Instance.LastCheckUpdateResults.TryGetValue(coreType, out var lastResult);
        return new()
        {
            IsSelected = _config.CheckUpdateItem.SelectedCoreTypes?.Contains(coreType.ToString()) ?? true,
            // Glide: engines updated through the signed index have no pre-release channel (nothing signed can exist for it).
            IsCheckPreRelease = !UpdateService.UsesCoreIndex(coreType) && (_config.CheckUpdateItem.CheckPreReleaseCoreTypes?.Contains(coreType.ToString()) ?? false),
            ShowCheckPreRelease = CoreInfoManager.Instance.GetCheckPreRelease(coreType, true) && !UpdateService.UsesCoreIndex(coreType),
            CoreType = coreType,
            IsGeoFile = false,
            // Glide: say what's there instead of repeating "Check update" on every row.
            Remarks = lastResult ?? (coreType == ECoreType.v2rayN
                ? string.Format(ResUI.TipInstalledVersion, Utils.GetVersionInfo())
                : ResUI.TipNotCheckedYet),
        };
    }

    private CheckUpdateModel GetGeoFileCheckUpdateModel()
    {
        return new()
        {
            IsSelected = _config.CheckUpdateItem.SelectedCoreTypes?.Contains(_geo) ?? true,
            IsCheckPreRelease = false,
            ShowCheckPreRelease = false,
            CoreType = null,
            IsGeoFile = true,
            Remarks = ResUI.TipNotCheckedYet,
        };
    }

    private async Task OnUpdateViaProxyChanged()
    {
        if (_config.CheckUpdateItem.UpdateViaProxy == EnableUpdateViaProxy)
        {
            return;
        }
        _config.CheckUpdateItem.UpdateViaProxy = EnableUpdateViaProxy;
        await SaveSelectedCoreTypes();
    }

    private async Task SaveSelectedCoreTypes()
    {
        _config.CheckUpdateItem.SelectedCoreTypes =
            CheckUpdateModels.Where(t => t.IsSelected == true)
                            .Select(t => t.CoreTypeForStorage)
                            .ToList();

        _config.CheckUpdateItem.CheckPreReleaseCoreTypes =
           CheckUpdateModels.Where(t => t.IsCheckPreRelease == true)
                           .Select(t => t.CoreTypeForStorage)
                           .ToList();

        await ConfigHandler.SaveConfig(_config);
    }

    private async Task CheckOnly()
    {
        await Task.Run(CheckOnlyTask);
    }

    private async Task CheckUpdate()
    {
        await Task.Run(CheckUpdateTask);
    }

    private async Task CheckOnlyTask()
    {
        await SaveSelectedCoreTypes();

        for (var k = CheckUpdateModels.Count - 1; k >= 0; k--)
        {
            var item = CheckUpdateModels[k];
            if (item.IsSelected != true)
            {
                continue;
            }

            await UpdateView(item.CoreType, "...");

            if (item.IsGeoFile || item.CoreType == null)
            {
                // Geo files carry no version number, so there is nothing to compare; "Check update" downloads them.
                await UpdateView(item.CoreType, ResUI.TipGeoCheckOnly);
                continue;
            }

            if (item.CoreType == null)
            {
                await UpdateView(item.CoreType, ResUI.MsgNotSupport);
                continue;
            }

            using var downloadService = await DownloadService.CreateDownloadServiceWithCheck(EnableUpdateViaProxy);
            var updateService = new UpdateService(downloadService, _config, async (success, msg) => await Task.CompletedTask);
            var result = await updateService.CheckHasUpdateOnly(item.CoreType.Value, item.IsCheckPreRelease);
            if (result.Success && result.Version != null)
            {
                await UpdateView(item.CoreType, await AvailableMessage(item.CoreType.Value, result.Version));
            }
            else
            {
                await UpdateView(item.CoreType, result.Msg);
            }
        }
    }

    private async Task CheckUpdateTask()
    {
        _lstUpdated.Clear();
        _lstUpdated = CheckUpdateModels
            .Where(x => x.IsSelected == true)
            .Select(x => new CheckUpdateModel()
            {
                CoreType = x.CoreType,
                IsGeoFile = x.IsGeoFile
            })
            .ToList();
        await SaveSelectedCoreTypes();

        for (var k = CheckUpdateModels.Count - 1; k >= 0; k--)
        {
            var item = CheckUpdateModels[k];
            if (item.IsSelected != true)
            {
                continue;
            }

            await UpdateView(item.CoreType, "...");

            if (item.IsGeoFile)
            {
                await CheckUpdateGeo();
            }
            else if (item.CoreType == _v2rayN)
            {
                if (Utils.IsPackagedInstall())
                {
                    await UpdateView(_v2rayN, ResUI.MsgNotSupport);
                    continue;
                }
                await CheckUpdateN(item.IsCheckPreRelease);
            }
            else if (item.CoreType.HasValue)
            {
                await CheckUpdateCore(item, item.IsCheckPreRelease);
            }
        }

        await UpdateFinished();
    }

    private void UpdatedPlusPlus(ECoreType? coreType, string fileName)
    {
        var item = _lstUpdated.FirstOrDefault(x => x.CoreType == coreType);
        if (item == null)
        {
            return;
        }
        item.IsFinished = true;
        if (!fileName.IsNullOrEmpty())
        {
            item.FileName = fileName;
        }
    }

    private async Task CheckUpdateGeo()
    {
        async Task _updateUI(bool success, string msg)
        {
            await UpdateView(null, msg);
            if (success)
            {
                UpdatedPlusPlus(null, "");
            }
        }
        using var downloadService = await DownloadService.CreateDownloadServiceWithCheck(EnableUpdateViaProxy);
        await new UpdateService(downloadService, _config, _updateUI).UpdateGeoFileAll()
            .ContinueWith(t => UpdatedPlusPlus(null, ""));
    }

    private async Task CheckUpdateN(bool preRelease)
    {
        async Task _updateUI(bool success, string msg)
        {
            await UpdateView(_v2rayN, msg);
            if (success)
            {
                await UpdateView(_v2rayN, ResUI.OperationSuccess);
                UpdatedPlusPlus(_v2rayN, msg);
            }
        }
        using var downloadService = await DownloadService.CreateDownloadServiceWithCheck(EnableUpdateViaProxy);
        await new UpdateService(downloadService, _config, _updateUI).CheckUpdateGuiN(preRelease)
            .ContinueWith(t => UpdatedPlusPlus(_v2rayN, ""));
    }

    private async Task CheckUpdateCore(CheckUpdateModel model, bool preRelease)
    {
        async Task _updateUI(bool success, string msg)
        {
            await UpdateView(model.CoreType, msg);
            if (success)
            {
                await UpdateView(model.CoreType, ResUI.MsgUpdateV2rayCoreSuccessfullyMore);
                UpdatedPlusPlus(model.CoreType, msg);
            }
        }

        if (model.CoreType.HasValue)
        {
            using var downloadService = await DownloadService.CreateDownloadServiceWithCheck(EnableUpdateViaProxy);
            await new UpdateService(downloadService, _config, _updateUI).CheckUpdateCore(model.CoreType.Value, preRelease)
                .ContinueWith(t => UpdatedPlusPlus(model.CoreType, ""));
        }
    }

    private async Task UpdateFinished()
    {
        if (_lstUpdated.Count > 0 && _lstUpdated.Count(x => x.IsFinished == true) == _lstUpdated.Count)
        {
            await UpdateFinishedSub(false);
            await Task.Delay(2000);
            try
            {
                await UpgradeCore();

                if (_lstUpdated.Any(x => x.CoreType == _v2rayN && x.IsFinished == true))
                {
                    await Task.Delay(1000);
                    await UpgradeN();
                }
            }
            finally
            {
                // Whatever happened, the engine that was stopped for the update starts again.
                await Task.Delay(1000);
                await UpdateFinishedSub(true);
            }
        }
    }

    private async Task UpdateFinishedSub(bool blReload)
    {
        RxSchedulers.MainThreadScheduler.Schedule(() =>
        {
            _ = UpdateFinishedResult(blReload);
        });
        await Task.CompletedTask;
    }

    public async Task UpdateFinishedResult(bool blReload)
    {
        if (blReload)
        {
            ReloadRequested.Publish();
        }
        else
        {
            await CoreManager.Instance.CoreStop();
        }
    }

    private static int? StartUpdaterElevated(string upgradeFileName, string argument)
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = upgradeFileName,
                Arguments = argument.Contains(' ') ? argument.AppendQuotes() : argument,
                WorkingDirectory = Utils.StartupPath(),
                UseShellExecute = true,
                Verb = "runas",
            });
            return process?.Id;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(StartUpdaterElevated), ex);
            return null;
        }
    }

    private async Task UpgradeN()
    {
        try
        {
            var fileName = _lstUpdated.FirstOrDefault(x => x.CoreType == _v2rayN)?.FileName;
            if (fileName.IsNullOrEmpty())
            {
                return;
            }
            if (!Utils.UpgradeAppExists(out var upgradeFileName))
            {
                await UpdateView(_v2rayN, ResUI.UpgradeAppNotExistTip);
                NoticeManager.Instance.SendMessageAndEnqueue(ResUI.UpgradeAppNotExistTip);
                Logging.SaveLog("UpgradeApp does not exist");
                return;
            }

            // Glide (S-02): install only an update signed with Glide's release key; Glide stays open otherwise.
            var zipPath = Utils.UrlDecode(fileName);
            if (!UpdateSignature.VerifyDownloaded(zipPath))
            {
                Logging.SaveLog($"{nameof(UpgradeN)}: update refused, its signature did not verify");
                File.Delete(zipPath);
                File.Delete(zipPath + UpdateSignature.Extension);
                await UpdateView(_v2rayN, ResUI.OperationFailed);
                NoticeManager.Instance.SendMessageAndEnqueue(ResUI.OperationFailed);
                return;
            }

            // Glide (S-01): installed in Program Files, the updater needs administrator rights to write there.
            var id = GlidePaths.IsInstalled
                ? StartUpdaterElevated(upgradeFileName, fileName)
                : ProcUtils.ProcessStart(upgradeFileName, fileName, Utils.StartupPath());
            if (id > 0)
            {
                await AppManager.Instance.AppExitAsync(true);
            }
            else if (GlidePaths.IsInstalled)
            {
                // UAC declined: Glide stays as it is.
                await UpdateView(_v2rayN, ResUI.OperationFailed);
            }
        }
        catch (Exception ex)
        {
            await UpdateView(_v2rayN, ex.Message);
        }
    }

    private async Task UpgradeCore()
    {
        var elevated = new List<(ECoreType Type, string Zip)>();
        foreach (var item in _lstUpdated)
        {
            if (item.FileName.IsNullOrEmpty() || item.IsGeoFile)
            {
                continue;
            }

            var fileName = item.FileName;
            if (!File.Exists(fileName))
            {
                continue;
            }

            var coreTypeStr = item.CoreType?.ToString() ?? "";
            var toPath = Utils.GetBinPath("", coreTypeStr);

            // Glide: installed, engines live in the protected program folder; they are collected here and installed by one
            // short-lived administrator process (one permission prompt), which checks everything again itself.
            if (item.CoreType.HasValue && UpdateService.UsesCoreIndex(item.CoreType.Value) && GlidePaths.IsInstalled)
            {
                elevated.Add((item.CoreType.Value, fileName));
                continue;
            }

            // Glide: an engine from the signed index goes through the staged, verified installer; an engine zip that was
            // not checked against the index is never extracted (fail closed).
            if (item.CoreType.HasValue && UpdateService.UsesCoreIndex(item.CoreType.Value))
            {
                var coreName = Global.CoreDisplayName(item.CoreType.Value);
                var installed = VerifiedCoreInstall.Install(fileName, toPath);
                if (installed.Ok)
                {
                    await UpdateView(item.CoreType, ResUI.MsgUpdateV2rayCoreSuccessfully);
                }
                else
                {
                    Logging.SaveLog($"{_tag}: {coreName} install refused: {installed.Error}: {installed.Message}");
                    await UpdateView(item.CoreType, installed.Error == CoreInstallError.NotVerified
                        ? string.Format(ResUI.MsgCoreVerifyFailed, coreName)
                        : string.Format(ResUI.MsgCoreInstallFailed, coreName, installed.Message));
                }
                if (File.Exists(fileName))
                {
                    File.Delete(fileName);
                }
                continue;
            }

            if (fileName.Contains(".tar.gz"))
            {
                FileUtils.DecompressTarFile(fileName, toPath);
                var dir = new DirectoryInfo(toPath);
                if (dir.Exists)
                {
                    foreach (var subDir in dir.GetDirectories())
                    {
                        FileUtils.CopyDirectory(subDir.FullName, toPath, false, true);
                        subDir.Delete(true);
                    }
                }
            }
            else if (fileName.Contains(".gz"))
            {
                FileUtils.DecompressFile(fileName, toPath, coreTypeStr);
            }
            else
            {
                FileUtils.ZipExtractToFile(fileName, toPath, "geo");
            }

            if (Utils.IsNonWindows())
            {
                var filesList = new DirectoryInfo(toPath).GetFiles().Select(u => u.FullName).ToList();
                foreach (var file in filesList)
                {
                    await Utils.SetLinuxChmod(Path.Combine(toPath, coreTypeStr.ToLower()));
                }
            }

            await UpdateView(item.CoreType, ResUI.MsgUpdateV2rayCoreSuccessfully);

            if (File.Exists(fileName))
            {
                File.Delete(fileName);
            }
        }

        if (elevated.Count > 0)
        {
            await InstallElevated(elevated);
        }
    }

    private async Task InstallElevated(List<(ECoreType Type, string Zip)> jobs)
    {
        var index = VerifiedCoreDownloads.LatestIndex;
        var ready = jobs.Where(j => index != null && VerifiedCoreDownloads.TryGet(j.Zip, out _)).ToList();
        try
        {
            foreach (var j in jobs.Except(ready))
            {
                await UpdateView(j.Type, string.Format(ResUI.MsgCoreVerifyFailed, Global.CoreDisplayName(j.Type)));
            }
            if (ready.Count == 0)
            {
                return;
            }

            var outcomes = await ElevatedCoreInstallLauncher.RunAsync(ready, index!.Value.Json, index.Value.Signature, Utils.GetTempPath());
            foreach (var (type, outcome) in outcomes)
            {
                var name = Global.CoreDisplayName(type);
                await UpdateView(type, outcome switch
                {
                    ElevatedInstallOutcome.Installed => ResUI.MsgUpdateV2rayCoreSuccessfully,
                    ElevatedInstallOutcome.Declined => string.Format(ResUI.MsgCoreUacDeclined, name),
                    _ => string.Format(ResUI.MsgCoreInstallFailed, name, ""),
                });
            }
        }
        finally
        {
            foreach (var (_, zip) in jobs)
            {
                VerifiedCoreDownloads.Remove(zip);
                if (File.Exists(zip))
                {
                    File.Delete(zip);
                }
            }
        }
    }

    private async Task UpdateView(ECoreType? coreType, string msg)
    {
        var item = new CheckUpdateModel()
        {
            CoreType = coreType,
            IsGeoFile = coreType == null,
            Remarks = msg,
        };

        RxSchedulers.MainThreadScheduler.Schedule(() =>
        {
            _ = UpdateViewResult(item);
        });
        await Task.CompletedTask;
    }

    public async Task UpdateViewResult(CheckUpdateModel model)
    {
        var found = CheckUpdateModels.FirstOrDefault(t => t.CoreType == model.CoreType && t.IsGeoFile == model.IsGeoFile);
        if (found == null)
        {
            return;
        }
        found.Remarks = model.Remarks;
        await Task.CompletedTask;
    }
}
