using static ADB_Explorer.Models.AdbExplorerConst;
using static ADB_Explorer.Models.Data;

namespace ADB_Explorer.Views.Pages;

public partial class ExplorerPageContent
{
    /// <summary>
    /// Back / Forward Navigation
    /// </summary>
    internal bool BfNavigation { get; set; }

    internal void PathBoxFocus(bool isFocused)
    {
        if (isFocused)
            _focusPathBox();
        else
            _unfocusPathBox();

        void _focusPathBox()
        {
            NavigationBox.Mode = NavigationBox.ViewMode.Path;
        }

        void _unfocusPathBox()
        {
            if (NavigationBox.Mode is NavigationBox.ViewMode.None)
                return;

            NavigationBox.Mode = NavigationBox.ViewMode.Breadcrumbs;

            FocusHelper.ClearFocus(NavigationBox.PathBox);
        }
    }

    private void InitLister()
    {
        var device = Instance.EffectiveDevice;
        Instance.FileList.Device = device;
        Instance.FileList.DirList = new(App.AppDispatcher, device, FileHelper.ListerFileManipulator);
        Instance.FileList.DirList.PropertyChanged += DirectoryLister_PropertyChanged;
    }

    private bool TrySelectBackNavigationItem()
    {
        if (!BfNavigation)
            return false;

        var path = Instance.History.TakePendingSelectionPath();
        if (string.IsNullOrEmpty(path))
            return false;

        if (NavHistory.FindBackNavigationItem(path) is not { } prevItem)
            return false;

        ItemToSelect.Value = prevItem;
        return true;
    }

    private void DirectoryLister_PropertyChanged(object? sender, PropertyChangedEventArgs e) => App.SafeInvoke(() =>
    {
        switch (e.PropertyName)
        {
            case nameof(DirectoryLister.CurrentLocation):
                // Empty selection shows CurrentLocation in the details pane. Refresh on location
                // changes only (preliminary + final). InProgress no longer re-triggers the same load.
                if (DetailsPaneControl.IsOpen && ExplorerList.ActiveSelectedItems.Count == 0)
                    DetailsPaneControl.RefreshSelection();
                break;

            case nameof(DirectoryLister.IsProgressVisible):
                Instance.IsListingUnfinished = Instance.FileList.DirList.IsProgressVisible;
                NavigationBox.IsLoadingProgressVisible = Instance.FileList.DirList.IsProgressVisible;
                break;

            case nameof(DirectoryLister.InProgress):
                {
                    Task.Run(() =>
                    {
                        if (!Instance.FileList.DirList.InProgress)
                            Task.Delay(EMPTY_FOLDER_NOTICE_DELAY);

                        App.SafeInvoke(() =>
                        {
                            Instance.FileList.Actions.ListingInProgress = Instance.FileList.DirList.InProgress;
                            FileActionLogic.UpdateFileActions();
                        });
                    });

                    if (Instance.FileList.DirList.InProgress)
                        return;

                    if (Instance.FileList.Actions.IsRecycleBin)
                        TrashHelper.EnableRecycleButtons();

                    break;
                }
            case nameof(DirectoryLister.IsLinkListingFinished) when !Instance.FileList.DirList.IsLinkListingFinished:
                return;

            case nameof(DirectoryLister.IsLinkListingFinished):
                {
                    ViewModel.NotifyDirectoryLinksResolved();

                    if (Instance.FileList.DirList.FileList.Count > 0)
                    {
                        SortExplorer();

                        // ActiveView.Items can still be empty here - DirList.Stop() fires this
                        // event before ExplorerSource is reassigned to the new folder's list.
                        if (!TrySelectBackNavigationItem() && ExplorerList.ActiveView.Items.Count > 0)
                        {
                            ExplorerList.ActiveScrollIntoView(ExplorerList.ActiveView.Items[0]);

                            if (Settings.ThumbsMode is AppSettings.ThumbnailMode.OnPhotoDir
                                && !ThumbnailService.IsInitialized(Instance.EffectiveDevice.SerialNumber)
                                && FileHelper.IsPhotoDir())
                            {
                                Task.Run(() => ThumbnailService.ForceLoad(Instance.EffectiveDevice));
                            }
                        }
                    }

                    break;
                }
        }
    });

    private bool InitNavigation(string path = "")
    {
        if (path is null)
            return true;

        var realPath = FolderHelper.FolderExists(string.IsNullOrEmpty(path) ? DEFAULT_PATH : path, Instance.EffectiveDevice);
        if (realPath is null)
            return false;

        Instance.FileList.Actions.IsDriveViewVisible = false;
        Instance.FileList.Actions.IsExplorerVisible = true;
        Instance.FileList.Actions.HomeEnabled = true;
        RuntimeSettings.SelectedDrive = null;

        // Re-arm the stray-selection guard for this navigation. Without resetting it here,
        // IsExplorerLoaded stays true after the very first navigation and never protects
        // later ones (e.g. the drive tile's second click landing on the newly shown grid).
        RuntimeSettings.IsExplorerLoaded = false;
        _explorerLoadedTimer.Stop();
        _explorerLoadedTimer.Start();

        return NavigateToPathCore(realPath);
    }

    private bool NavigateToPathCore(string realPath, FileClass? locationSource = null)
    {
        Instance.FileList.Actions.IsSearchMode = false;
        Instance.FileList.SearchOriginPath = null;
        Instance.FileList.SearchOriginCanWrite = false;
        Instance.FileList.SearchTransferParent = null;

        DeviceCts.Cancel();
        DeviceCts.Dispose();
        DeviceCts = new();
        ApkIconService.CancelPending();

        Instance.FileList.DirList?.Stop();

        ArchivePath.InvalidateCache();

        var deviceId = Instance.EffectiveDevice?.ID;
        var isArchive = ArchivePath.IsArchivePath(realPath, deviceId);
        var devicePath = isArchive ? ArchivePath.GetArchivePath(realPath, deviceId) : realPath;

        Instance.FileList.Actions.ListingInProgress = true;

        Instance.FileList.Actions.WasInAppDrive = Instance.FileList.Actions.IsAppDrive;
        Instance.FileList.Actions.ExplorerFilter = "";
        Instance.History.Navigate(realPath);
        Instance.NotifyTabChanged();

        Instance.FirstSelectedIndex = -1;
        Instance.CurrentSelectedIndex = -1;
        ExplorerList.ActiveUnselectAll();

        if (DetailsPaneControl.IsOpen)
            DetailsPaneControl.SelectedFiles = [];

        ExplorerList.ActiveView.Focus();

        NavigationBox.Mode = NavigationBox.ViewMode.Breadcrumbs;
        SetExplorerBoxPath(realPath == RECYCLE_PATH ? AdbLocation.StringFromLocation(Navigation.SpecialLocation.RecycleBin) : realPath);
        Instance.FileList.CurrentDrive = DriveHelper.GetCurrentDrive(devicePath);
        Instance.FileList.Actions.IsRecycleBin = realPath == RECYCLE_PATH;
        Instance.FileList.Actions.IsAppDrive = realPath == AdbLocation.StringFromLocation(Navigation.SpecialLocation.PackageDrive);
        Instance.FileList.Actions.IsArchive = isArchive;
        Instance.FileList.Actions.IsTemp = realPath == TEMP_PATH;
        Instance.FileList.Actions.ParentEnabled = realPath != FileHelper.GetParentPath(realPath)
            && !Instance.FileList.Actions.IsRecycleBin && !Instance.FileList.Actions.IsAppDrive;

        if (Instance.FileList.DirList is null && Instance.EffectiveDevice is not null)
            InitLister();

        CurrentPath = realPath;

        FileActionLogic.IsPasteEnabled();

        Instance.FileList.Actions.PushPackageEnabled = Settings.EnableApk && Instance.EffectiveDevice is { Type: not DeviceType.Recovery };
        Instance.FileList.Actions.UninstallPackageEnabled = false;

        Instance.FileList.Actions.ContextPushPackagesEnabled =
        Instance.FileList.Actions.IsUninstallVisible.Value = Instance.FileList.Actions.IsAppDrive;
        Instance.FileList.Actions.IsCutPasteDeleteVisible.Value = !Instance.FileList.Actions.IsAppDrive;
        Instance.FileList.Actions.IsPullCopyVisible.Value = !Instance.FileList.Actions.IsRecycleBin;
        Instance.FileList.Actions.IsPasteVisible.Value = !Instance.FileList.Actions.IsAppDrive && !Instance.FileList.Actions.IsRecycleBin;

        Instance.FileList.Actions.CopyPathDescription.Value = Instance.FileList.Actions.IsAppDrive ? Strings.Resources.S_COPY_APK_NAME : Strings.Resources.S_COPY_PATH;

        ApplyLocationThumbSize();

        SortExplorer();

        if (Instance.FileList.Actions.IsRecycleBin)
        {
            TrashHelper.ParseIndexersAsync(DeviceCts.Token).ContinueWith(_ => Instance.FileList.DirList?.Navigate(realPath));

            Instance.FileList.Actions.DeleteDescription.Value = Strings.Resources.S_EMPTY_TRASH;
            Instance.FileList.Actions.RestoreDescription.Value = Strings.Resources.S_RESTORE_ALL;
        }
        else
        {
            if (Instance.FileList.Actions.IsAppDrive)
            {
                FileActionLogic.UpdatePackages(true, DeviceCts.Token, instance: Instance);
                FileActionLogic.UpdateFileActions();
                ExplorerList.ResetExplorerHorizontalScroll();
                return true;
            }

            if (Instance.FileList.DirList is null)
                return false;

            Instance.FileList.DirList.Navigate(realPath, locationSource);

            Instance.FileList.Actions.DeleteDescription.Value = Strings.Resources.S_DELETE_ACTION;
        }

        if (Instance.FileList.DirList is not null)
            Instance.ExplorerSource = Instance.FileList.DirList.FileList;

        FileActionLogic.UpdateFileActions();

        ExplorerList.ResetExplorerHorizontalScroll();

        return true;
    }

    private string? _explorerBoxPath;

    /// <summary>The box shows the explorer's location, unless a page is on top of it - that one is put back afterwards.</summary>
    private void SetExplorerBoxPath(string path)
    {
        _explorerBoxPath = path;
        NavigationBox.Path = path;
    }

    /// <summary>The box shows the current page's name, or the explorer's location when the tab isn't on a page.</summary>
    private void SyncNavigationBoxWithHistory()
    {
        var target = Instance.History.Current is { IsPage: true } page
            ? page.StringFromLocation()
            : _explorerBoxPath;

        if (target is null || NavigationBox.Path == target)
            return;

        NavigationBox.Mode = NavigationBox.ViewMode.Breadcrumbs;
        NavigationBox.Path = target;
    }

    private void StopListing()
    {
        Instance.FileList.DirList?.Stop();
        DisposeFileIcons();
    }

    /// <summary>True when the tab is showing a page and this is the location its explorer already sits at underneath.</summary>
    private bool IsRevealingHiddenExplorer(AdbLocation location)
        => Instance.IsShowingPage
        && !location.IsPage
        && Instance.History.Stamp(location).Equals(Instance.History.LastExplorerLocation);

    /// <summary>Back / forward landed on an entry - a page, a location on another device, or a plain folder.</summary>
    private void NavigateHistoryEntry(AdbLocation? entry)
    {
        if (entry is null)
            return;

        if (!entry.IsPage && !IsRevealingHiddenExplorer(entry))
            StopListing();

        NavigateToLocation(entry);
    }

    private void SwitchToEntryDevice(AdbLocation location)
    {
        var device = Data.DevicesObject?.LogicalDeviceViewModels?
            .FirstOrDefault(d => d.ID == location.DeviceId && d.Status is DeviceStatus.Ok);

        if (device is null)
            return;

        RuntimeSettings.PendingLocationAfterDeviceOpen = location;
        DeviceHelper.NavigateTabToDevice(Instance, device);
    }

    internal void NavigateToLocation(AdbLocation? location)
    {
        if (location is null)
            return;

        Instance.IsMenuOpen = false;

        if (location.IsPage)
        {
            PathBoxFocus(false);

            Instance.History.Navigate(location);
            Instance.NotifyTabChanged();
            return;
        }

        if (location.DeviceId is { } deviceId && deviceId != Instance.EffectiveDevice?.ID)
        {
            SwitchToEntryDevice(location);
            return;
        }

        if (IsRevealingHiddenExplorer(location))
        {
            Instance.History.Navigate(location);
            Instance.NotifyTabChanged();
            return;
        }

        if (location.Location is Navigation.SpecialLocation.DriveView)
        {
            if (DiskUsagePollingService.ServerUnresponsive || Instance.EffectiveDevice is null)
                return;

            Instance.FileList.Actions.IsRecycleBin = false;
            PathBoxFocus(false);
            RaiseUnfocusSearchBox();
            FileActionLogic.RefreshDrives(true, DeviceCts.Token, Instance.EffectiveDevice);
            DriveViewNav();

            FileActionLogic.UpdateFileActions();
        }
        else
        {
            if (location.Location is Navigation.SpecialLocation.SearchMode)
            {
                RestoreSearch(location);
                return;
            }

            var path = string.IsNullOrEmpty(location.Path)
                ? location.StringFromLocation()
                : location.Path;

            if (!Instance.FileList.Actions.IsExplorerVisible)
            {
                if (!InitNavigation(path))
                    DriveViewNav();
            }
            else
                NavigateToPath(path);
        }
    }

    public bool NavigateToPath(FileClass file)
    {
        if (file is null)
            return false;

        if (!Instance.FileList.Actions.IsAppDrive
            && Instance.EffectiveDevice is { } device
            && ArchiveHelper.CanNavigateIntoArchive(file.FullPath, file.FullName, device.ID, Instance.FileList.Actions.IsArchive))
        {
            return NavigateToPathCore(ArchivePath.Join(file.FullPath, ""), file);
        }

        string realPath = !string.IsNullOrEmpty(file.LinkTarget)
            ? file.LinkTarget
            : file.FullPath;

        return realPath is not null && NavigateToPathCore(realPath, file);
    }

    public bool NavigateToPath(string path)
    {
        if (path is null)
            return false;

        var realPath = FolderHelper.FolderExists(path, Instance.EffectiveDevice);
        if (realPath is null)
            return false;

        var locationSource = Instance.FileList.DirList?.FileList.FirstOrDefault(f => f.IsDirectory && f.FullPath == realPath);
        return NavigateToPathCore(realPath, locationSource);
    }

    private void DriveViewNav()
    {
        if (DiskUsagePollingService.ServerUnresponsive || Instance.EffectiveDevice is null)
            return;

        DeviceCts.Cancel();
        DeviceCts.Dispose();
        DeviceCts = new();
        ApkIconService.CancelPending();

        // Clears this pane's listing, not the focused one's, and leaves the focused pane's stray-selection guard alone.
        var wasLoaded = RuntimeSettings.IsExplorerLoaded;
        using (Data.UseInstance(Instance))
            FileActionLogic.ClearExplorer(false);

        if (!ReferenceEquals(Instance, Data.ActiveExplorerInstance))
            RuntimeSettings.IsExplorerLoaded = wasLoaded;

        Instance.FileList.Actions.IsDriveViewVisible = true;

        NavigationBox.Mode = NavigationBox.ViewMode.Breadcrumbs;
        CurrentPath = AdbLocation.StringFromLocation(Navigation.SpecialLocation.DriveView);
        SetExplorerBoxPath(CurrentPath);
        Instance.History.Navigate(Navigation.SpecialLocation.DriveView);
        Instance.NotifyTabChanged();

        // Direct call, not just relying on the IsDriveViewVisible change above to reach
        // ExplorerViewModel reactively - see UpdateDriveView(ExplorerInstance)'s own comment.
        ViewModel.UpdateDriveView(Instance);

        Instance.FileList.CurrentDrive = null;

        if (!BfNavigation)
        {
            ExplorerList.DriveListView.SelectedIndex = -1;
            RuntimeSettings.SelectedDrive = null;
        }

        if (ExplorerList.DriveListView.SelectedIndex > -1)
        {
            SelectionHelper.GetListViewItemContainer(ExplorerList.DriveListView).Focus();

            if (DetailsPaneControl.IsOpen)
                DetailsPaneControl.SelectedFiles = ExplorerList.DriveListView.SelectedItem is DriveViewModel selectedDrive ? [selectedDrive] : [];
        }
        else if (DetailsPaneControl.IsOpen)
        {
            DetailsPaneControl.SelectedFiles = [];
            DetailsPaneControl.RefreshSelection();
        }

        RuntimeSettings.SelectedDrive = ExplorerList.DriveListView.SelectedItem as DriveViewModel;
        FileActionLogic.UpdateFileActions();

        Instance.CurrentThumbsSize = ThumbnailService.ThumbnailSize.Tiles;
    }
}
