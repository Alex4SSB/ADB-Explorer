using ADB_Explorer.Controls;
using ADB_Explorer.Converters;
using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;
using Wpf.Ui.Abstractions.Controls;

namespace ADB_Explorer.ViewModels.Pages;

public partial class ExplorerViewModel : ObservableObject, INavigationAware
{
    private bool _isInitialized;
    private bool _devicesSubscribed;

    /// <summary>The active tab's full state; only one instance exists for now.</summary>
    public ExplorerInstance Instance => Data.ActiveExplorerInstance;

    public NavigationTreeViewModel Tree { get; }

    private bool _uiListSubscribed;

    private bool _suppressSortApply;

    private readonly DispatcherTimer _filterDebounceTimer;
    private readonly DispatcherTimer _packageSortCatchUpTimer;

    /// <summary>Reacts to Instance property changes that used to be ObservableProperty On*Changed hooks on this class.</summary>
    private void Instance_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ExplorerInstance.SortDirection):
            case nameof(ExplorerInstance.SortedColumn):
                if (!_suppressSortApply)
                    ApplySortToView();
                break;

            case nameof(ExplorerInstance.ExplorerSource):
                UpdateExplorerView();
                break;

            case nameof(ExplorerInstance.CurrentThumbsSize):
                OnCurrentThumbsSizeChanged(Instance.CurrentThumbsSize);
                break;

            case nameof(ExplorerInstance.EffectiveDevice):
                NotifyDeviceBindings();
                break;

            default:
                break;
        }
    }

    private void ApplySortToView()
    {
        if (Instance.SortDirection is not { } dir || Instance.SortedColumn is not { } col || Instance.ExplorerItemsSource is not { } view)
            return;

        // SortExplorer() runs synchronously in _navigateToPath right after FileActions.IsAppDrive
        // flips for the new location, but ExplorerItemsSource/ExplorerSource are only swapped once
        // the new location's items (packages or files) actually arrive. Applying Package-only or
        // File-only SortDescriptions to the stale, mismatched view here would sort the wrong
        // (soon-to-be-discarded) collection instead of the one about to be shown.
        var sourceIsPackages = Instance.ExplorerSource is IEnumerable<Package>;
        if (Data.FileActions.IsAppDrive != sourceIsPackages)
            return;

        if (Data.FileActions.IsAppDrive)
        {
            ApplyPackageSortToView(view, col, dir);
            return;
        }

        view.SortDescriptions.Clear();
        view.SortDescriptions.Add(new(nameof(FileClass.IsTemp), ListSortDirection.Descending));
        view.SortDescriptions.Add(new(nameof(FileClass.IsDirectory), ListHelper.Invert(dir)));

        var sortProp = col switch
        {
            SortingSelector.SortingProperty.Date => nameof(FileClass.ModifiedTime),
            SortingSelector.SortingProperty.Size => nameof(FileClass.Size),
            SortingSelector.SortingProperty.Type => $"{nameof(FolderViewModel)}.{nameof(FolderViewModel.TypeName)}",
            _ => nameof(FileClass.SortName),
        };

        view.SortDescriptions.Add(new(sortProp, dir));

        if (Data.Settings.SortingPerLocation)
        {
            if (Data.Settings.LocationSorting.ContainsKey(Data.CurrentPath))
            {
                Data.Settings.LocationSorting[Data.CurrentPath] = new(col, dir);
            }
            else
            {
                Data.Settings.LocationSorting.Add(Data.CurrentPath, new(col, dir));
            }
        }
    }

    private void ApplyPackageSortToView(ICollectionView view, SortingSelector.SortingProperty col, ListSortDirection dir)
    {
        view.SortDescriptions.Clear();

        var sortProp = col switch
        {
            SortingSelector.SortingProperty.Type => nameof(Package.Type),
            SortingSelector.SortingProperty.UserId => nameof(Package.Uid),
            SortingSelector.SortingProperty.Version => nameof(Package.Version),
            _ => nameof(Package.DisplayName),
        };

        view.SortDescriptions.Add(new(sortProp, dir));

        // Secondary name sort (same direction) when the primary column is not name.
        if (col is not SortingSelector.SortingProperty.Name)
            view.SortDescriptions.Add(new(nameof(Package.DisplayName), dir));

        // Live-sort on DisplayName so labels arriving later re-order tiles without
        // ICollectionView.Refresh() (which resets virtualization and blanks all icons).
        // Paused while APK icons/labels are streaming in — WPF resets ListView selection
        // whenever a live-sorted collection reorders, which breaks clicking to select.
        EnablePackageLiveSorting(view);

        Instance.PackageTypeColumnSortDirection = col is SortingSelector.SortingProperty.Type ? dir : null;

        if (Data.Settings.SortingPerLocation)
        {
            if (Data.Settings.LocationSorting.ContainsKey(Data.CurrentPath))
                Data.Settings.LocationSorting[Data.CurrentPath] = new(col, dir);
            else
                Data.Settings.LocationSorting.Add(Data.CurrentPath, new(col, dir));
        }
    }

    private static void EnablePackageLiveSorting(ICollectionView view)
    {
        if (view is not ListCollectionView listView || !listView.CanChangeLiveSorting)
            return;

        if (!listView.LiveSortingProperties.Contains(nameof(Package.DisplayName)))
            listView.LiveSortingProperties.Add(nameof(Package.DisplayName));

        listView.IsLiveSorting = !ApkIconService.IsLoadInProgress;
    }

    /// <summary>
    /// Disables live sorting while the icon/label queue is active (streaming labels would
    /// reorder mid-click and reset ListView selection). On idle, re-enable live sorting only —
    /// do not Clear/re-add <see cref="SortDescription"/>s: that refreshes the view, resets
    /// virtualization, and blanks all but a few tiles (especially when scroll keeps restarting
    /// the queue). Labels that arrived while paused are caught up via a debounced
    /// <see cref="DisplayName"/> nudge after the queue stays idle.
    /// </summary>
    private void OnApkIconLoadProgressChanged(bool active)
    {
        if (!Data.FileActions.IsAppDrive)
            return;

        App.SafeBeginInvoke(() =>
        {
            if (!Data.FileActions.IsAppDrive || Instance.ExplorerItemsSource is not { } view)
                return;

            if (active)
            {
                _packageSortCatchUpTimer.Stop();
                DisablePackageLiveSorting(view);
                return;
            }

            EnablePackageLiveSorting(view);
            _packageSortCatchUpTimer.Stop();
            _packageSortCatchUpTimer.Start();
        });
    }

    private void PackageSortCatchUpTimer_Tick(object? sender, EventArgs e)
    {
        _packageSortCatchUpTimer.Stop();

        if (!Data.FileActions.IsAppDrive || ApkIconService.IsLoadInProgress)
            return;
        if (Instance.ExplorerItemsSource is not ListCollectionView { IsLiveSorting: true })
            return;
        if (Data.Packages is not { Count: > 0 } packages)
            return;

        var selected = packages.Where(static p => p.IsSelected).ToList();
        var pending = packages.Where(static p => p.NeedsLiveSortCatchUp).ToList();
        if (pending.Count == 0)
            return;

        foreach (var pkg in pending)
            pkg.NotifyDisplayNameForSort();

        if (selected.Count == 0)
            return;

        foreach (var pkg in packages)
        {
            var shouldSelect = selected.Contains(pkg);
            if (pkg.IsSelected != shouldSelect)
                pkg.IsSelected = shouldSelect;
        }
    }

    private static void DisablePackageLiveSorting(ICollectionView view)
    {
        if (view is ListCollectionView { CanChangeLiveSorting: true } listView)
            listView.IsLiveSorting = false;
    }

    public void SetSort(SortingSelector.DirSortingOption sort) => SetSort(sort.Property, sort.Direction);

    public void SetSort(SortingSelector.SortingProperty column, ListSortDirection direction)
    {
        _suppressSortApply = true;
        Instance.SortedColumn = column;
        Instance.SortDirection = direction;
        _suppressSortApply = false;
        ApplySortToView();
    }

    private void OnCurrentThumbsSizeChanged(ThumbnailService.ThumbnailSize value)
    {
        Instance.IsIconView = ThumbnailService.IsIconLayout(value);
        Instance.IsContentView = value is ThumbnailService.ThumbnailSize.Content;

        // Device without unzip: force details view without clobbering saved sizes.
        // Tiles is drive-view only and must not overwrite the last explorer size.
        if (value is ThumbnailService.ThumbnailSize.Tiles
            || Data.FileActions.IsDriveViewVisible
            || Data.FileActions.IsAppDriveThumbsLocked)
            return;

        if (Data.Settings.ThumbSizePerLocation && Data.CurrentPath is not null)
        {
            if (Data.Settings.LocationThumbSize.ContainsKey(Data.CurrentPath))
                Data.Settings.LocationThumbSize[Data.CurrentPath] = value;
            else
                Data.Settings.LocationThumbSize.Add(Data.CurrentPath, value);
        }

        Data.RuntimeSettings.ThumbsSize = value;
    }

    public string SelectedFilesTotalSize => (Data.SelectedFiles is not null && FileHelper.TotalSize(Data.SelectedFiles) is long size and > 0) ? size.BytesToSize(true) : "";
    public string SelectedFilesCount => $"{(Data.FileActions.IsAppDrive ? Data.SelectedPackages.Count() : Data.SelectedFiles.Count())}";

    public Visibility SelectedItemsCountVisibility => SelectedFilesCount == "0" ? Visibility.Collapsed : Visibility.Visible;

    public Visibility SelectedFilesTotalSizeVisibility => string.IsNullOrEmpty(SelectedFilesTotalSize) ? Visibility.Collapsed : Visibility.Visible;

    public Visibility FolderColumnVisibility
        => Data.FileActions.IsAppDrive ? Visibility.Collapsed : Visibility.Visible;

    public Visibility RecycleBinColumnVisibility
        => Data.FileActions.IsRecycleBin ? Visibility.Visible : Visibility.Collapsed;

    public Visibility PackageColumnVisibility
        => Data.FileActions.IsAppDrive ? Visibility.Visible : Visibility.Collapsed;

    public void NotifySelectedFilesTotalSize()
    {
        OnPropertyChanged(nameof(SelectedFilesTotalSize));
        OnPropertyChanged(nameof(SelectedFilesTotalSizeVisibility));
    }

    public FileClass GalleryFile
    {
        get
        {
            field ??= new("Gallery", "/Gallery", AbstractFile.FileType.Gallery);

            return field;
        }
    } = null;

    public LogicalDeviceViewModel? EffectiveDevice => Instance.EffectiveDevice;

    public LogicalDeviceViewModel? CurrentDevice => EffectiveDevice;

    public Battery? CurrentDeviceBattery => EffectiveDevice?.Battery;

    public Action? RequestModeRefresh { get; set; }

    public bool IsBatteryVisible =>
        Data.Settings.PollBattery
        && CurrentDeviceBattery?.ChargeState is not Battery.ChargingState.Unknown
        && CurrentDeviceBattery?.Level is not null;

    private ExplorerInstance? _subscribedInstance;

    /// <summary>
    /// Re-wires the reactive handlers below (sort application, view refresh, drive view,
    /// column visibility, selection count) to whichever tab is now active, and immediately
    /// re-derives their outputs - a background tab's own navigation can finish loading while
    /// unsubscribed, so switching to it must catch up rather than wait for its next change.
    /// </summary>
    private void SubscribeActiveInstance()
    {
        if (ReferenceEquals(_subscribedInstance, Instance))
            return;

        var isTabSwitch = _subscribedInstance is not null;

        if (_subscribedInstance is not null)
        {
            _subscribedInstance.PropertyChanged -= Instance_PropertyChanged;
            _subscribedInstance.FileList.Actions.PropertyChanged -= FileActions_PropertyChanged;
        }

        _subscribedInstance = Instance;
        Instance.PropertyChanged += Instance_PropertyChanged;
        Instance.FileList.Actions.PropertyChanged += FileActions_PropertyChanged;

        Instance.IsIconView = ThumbnailService.IsIconLayout(Data.RuntimeSettings.ThumbsSize);
        Instance.IsContentView = Data.RuntimeSettings.ThumbsSize is ThumbnailService.ThumbnailSize.Content;

        OnPropertyChanged(nameof(SelectedFilesCount));
        OnPropertyChanged(nameof(SelectedItemsCountVisibility));
        OnPropertyChanged(nameof(FolderColumnVisibility));
        OnPropertyChanged(nameof(RecycleBinColumnVisibility));
        OnPropertyChanged(nameof(PackageColumnVisibility));

        UpdateExplorerView();
        UpdateDriveView();

        if (!isTabSwitch)
            return;

        // Device-derived bindings and the tree were last derived from the previous tab. The path
        // observer already syncs the tree and mode when the path differs, so don't repeat that.
        NotifyDeviceBindings(syncTree: false);

        var pathChanged = Data.CurrentPathO.Value != Data.CurrentPath;
        Data.CurrentPathO.Value = Data.CurrentPath;

        if (pathChanged)
            return;

        RequestModeRefresh?.Invoke();
        Tree.Sync();
    }

    /// <summary>Re-derives everything bound to the active tab's device.</summary>
    private void NotifyDeviceBindings(bool syncTree = true)
    {
        OnPropertyChanged(nameof(EffectiveDevice));
        OnPropertyChanged(nameof(CurrentDevice));
        OnPropertyChanged(nameof(CurrentDeviceBattery));
        OnPropertyChanged(nameof(IsBatteryVisible));
        SubscribeToBattery(CurrentDeviceBattery);
        UpdateDriveView();

        Tree.SubscribeDriveLists();

        if (syncTree)
            Tree.Sync();
    }

    public ExplorerViewModel()
    {
        Tree = new(() => Instance.ExplorerSource);

        App.Services.GetService<ExplorerTabsViewModel>().PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ExplorerTabsViewModel.ActiveTab))
                SubscribeActiveInstance();
        };

        SubscribeActiveInstance();

        _filterDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _filterDebounceTimer.Tick += (s, e) =>
        {
            _filterDebounceTimer.Stop();
            RefreshExplorerFilter();
            RefreshContentSearchMatches();
        };

        // After icon/label queue idle (+ progress hide debounce), wait a bit longer so scroll
        // bursts do not thrash live-sort catch-up.
        _packageSortCatchUpTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
        _packageSortCatchUpTimer.Tick += PackageSortCatchUpTimer_Tick;

        Data.DevicesObjectCreated += (_, _) => App.SafeInvoke(EnsureDevicesSubscription);

        ApkIconService.IconLoadProgressChanged += OnApkIconLoadProgressChanged;
    }

    /// <summary>Lets the always-visible navigation tree initialize this before the Explorer page is first shown.</summary>
    public void EnsureInitialized()
    {
        if (!_isInitialized)
            InitializeViewModel();
    }

    public Task OnNavigatedToAsync()
    {
        if (!_isInitialized)
            InitializeViewModel();
        else
            EnsureDevicesSubscription();

        Data.CurrentPage.Value = typeof(Views.Pages.ExplorerPage);

        return Task.CompletedTask;
    }

    public void NotifyDirectoryLinksResolved() => Tree.Sync();

    private void InitializeViewModel()
    {
        Data.Settings.SavedLocations ??= [];
        Data.Settings.SavedLocations.CollectionChanged += SavedLocations_CollectionChanged;
        SavedLocations_CollectionChanged(null, null);

        Data.RuntimeSettings.PropertyChanged += RuntimeSettings_PropertyChanged;
        Data.Settings.PropertyChanged += Settings_PropertyChanged;

        EnsureDevicesSubscription();

        Data.CurrentPathO.PropertyChanged += (s, e) =>
        {
            RequestModeRefresh?.Invoke();
            Tree.Sync();
        };

        Tree.Sync();

        // The tree shows no current location while a page covers the explorer.
        Data.CurrentPage.PropertyChanged += (_, _) => Tree.Sync();

        _isInitialized = true;
    }

    private void EnsureDevicesSubscription()
    {
        if (_devicesSubscribed || Data.DevicesObject is null)
            return;

        Data.DevicesObject.PropertyChanged += DevicesObject_PropertyChanged;
        _devicesSubscribed = true;
        SubscribeDeviceList();
        Tree.SubscribeDriveLists();
    }

    public Task OnNavigatedFromAsync() => Task.CompletedTask;

    private void SavedLocations_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs? e)
    {
        App.SafeBeginInvoke(() =>
        {
            foreach (var instance in AllInstances())
                instance.RefreshSavedItems();

            Tree.Sync();
        });
    }

    /// <summary>Every open tab, plus the active instance (which isn't in the tab list until
    /// Explorer is first navigated to).</summary>
    private static HashSet<ExplorerInstance> AllInstances()
    {
        var instances = new HashSet<ExplorerInstance> { Data.ActiveExplorerInstance };
        if (App.Services.GetService<ExplorerTabsViewModel>() is { } tabs)
            instances.UnionWith(tabs.Tabs);

        return instances;
    }

    private void NotifyBatteryVisibility()
    {
        OnPropertyChanged(nameof(IsBatteryVisible));
    }

    private Battery? _subscribedBattery;

    private void SubscribeToBattery(Battery? battery)
    {
        _subscribedBattery?.PropertyChanged -= Battery_PropertyChanged;

        _subscribedBattery = battery;

        _subscribedBattery?.PropertyChanged += Battery_PropertyChanged;
    }

    private void Battery_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Battery.ChargeState) or nameof(Battery.Level))
            NotifyBatteryVisibility();
    }

    private void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppSettings.EnableApk):
            case nameof(AppSettings.EnableRecycle):
                UpdateDriveView();
                Tree.Sync();
                break;

            case nameof(AppSettings.ShowHiddenItems):
                Tree.OnShowHiddenItemsChanged();
                break;

            case nameof(AppSettings.PollBattery):
                NotifyBatteryVisibility();
                break;

            case nameof(AppSettings.SidePane):
                RequestModeRefresh?.Invoke();
                break;

            case nameof(AppSettings.SearchBox):
                if (Data.Settings.SearchBox is SearchBox.SearchBoxMode.CurrentFolder && Data.FileActions.IsSearchMode)
                    Data.RaiseExitSearchMode();
                else if (Data.Settings.SearchBox is SearchBox.SearchBoxMode.AllSubfolders
                         && !string.IsNullOrEmpty(Data.FileActions.ExplorerFilter))
                    Data.RaiseRunExplorerSearch();

                RefreshContentSearchMatches();
                break;

            case nameof(AppSettings.SearchCaseSensitive):
                if (string.IsNullOrEmpty(Data.FileActions.ExplorerFilter))
                    break;

                if (Data.Settings.SearchBox is SearchBox.SearchBoxMode.AllSubfolders)
                    Data.RaiseRunExplorerSearch();
                else
                    RefreshExplorerFilter();

                RefreshContentSearchMatches();
                break;

            case nameof(AppSettings.SearchContents):
            case nameof(AppSettings.SearchArchives):
                if (Data.Settings.SearchBox is SearchBox.SearchBoxMode.AllSubfolders
                    && !string.IsNullOrEmpty(Data.FileActions.ExplorerFilter))
                    Data.RaiseRunExplorerSearch();
                else
                    RefreshContentSearchMatches();
                break;

            default:
                break;
        }
    }

    private void RuntimeSettings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppRuntimeSettings.FilterDrives):
                UpdateDriveView();
                break;

            case nameof(AppRuntimeSettings.ThumbsSize):
                Instance.IsIconView = ThumbnailService.IsIconLayout(Data.RuntimeSettings.ThumbsSize);
                Instance.IsContentView = Data.RuntimeSettings.ThumbsSize is ThumbnailService.ThumbnailSize.Content;
                break;

            default:
                break;
        }
    }

    private void DevicesObject_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Devices.Current))
        {
            // A tab pinned to its own device (not tracking the app-wide current one) doesn't
            // care that Devices.Current moved elsewhere - EffectiveDevice for it is unchanged.
            foreach (var instance in AllInstances().Where(i => i.TracksAppWideCurrentDevice))
                instance.NotifyEffectiveDeviceChanged();

            Tree.SubscribeDriveLists();
            Tree.Sync();
        }
        else if (e.PropertyName == nameof(Devices.Count))
        {
            Tree.SubscribeDriveLists();
            Tree.Sync();
        }
    }

    private void FileActions_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(FileActionsEnable.SelectedItemsCount):
                OnPropertyChanged(nameof(SelectedFilesCount));
                OnPropertyChanged(nameof(SelectedItemsCountVisibility));
                break;

            case nameof(FileActionsEnable.IsAppDrive):
                OnPropertyChanged(nameof(FolderColumnVisibility));
                OnPropertyChanged(nameof(RecycleBinColumnVisibility));
                OnPropertyChanged(nameof(PackageColumnVisibility));
                break;

            case nameof(FileActionsEnable.IsRecycleBin):
                OnPropertyChanged(nameof(FolderColumnVisibility));
                OnPropertyChanged(nameof(RecycleBinColumnVisibility));
                OnPropertyChanged(nameof(PackageColumnVisibility));
                break;

            case nameof(FileActionsEnable.IsDriveViewVisible):
                UpdateDriveView();
                break;

            case nameof(FileActionsEnable.ListingInProgress):
                if (!Data.FileActions.ListingInProgress)
                    Tree.Sync();
                break;

            case nameof(FileActionsEnable.ExplorerFilter):
                if (Data.Settings.SearchBox is SearchBox.SearchBoxMode.AllSubfolders)
                {
                    Data.RaiseRunExplorerSearch();
                    break;
                }

                _filterDebounceTimer.Stop();
                _filterDebounceTimer.Start();
                break;

            default:
                break;
        }
    }

    private void RefreshExplorerFilter()
    {
        App.SafeInvoke(() =>
        {
            Instance.ExplorerItemsSource?.Refresh();
        });
    }

    private CancellationTokenSource? _contentSearchCts;

    /// <summary>Re-runs the device "File contents" grep for Current Folder scope; matches land in
    /// <see cref="Data.ContentSearchMatches"/> and trigger a second filter pass (can't await inline).</summary>
    private void RefreshContentSearchMatches()
    {
        _contentSearchCts?.Cancel();
        _contentSearchCts?.Dispose();
        _contentSearchCts = null;

        var query = Data.FileActions.ExplorerFilter?.Trim();

        if (!Data.Settings.SearchContents
            || Data.Settings.SearchBox is not SearchBox.SearchBoxMode.CurrentFolder
            || string.IsNullOrEmpty(query)
            || EffectiveDevice is not { } device)
        {
            if (Data.ContentSearchMatches is not null)
            {
                Data.ContentSearchMatches = null;
                RefreshExplorerFilter();
            }

            return;
        }

        var cts = new CancellationTokenSource();
        _contentSearchCts = cts;
        var path = Data.CurrentPath;
        var caseSensitive = Data.Settings.SearchCaseSensitive;

        Task.Run(() =>
        {
            HashSet<string> matches;

            try
            {
                if (ArchivePath.IsArchivePath(path, device.ID))
                {
                    matches = ArchiveHelper.SearchArchiveContentsInFolder(device.ID, path, query, caseSensitive, cts.Token);
                }
                else
                {
                    matches = [];
                    foreach (var fileStat in ADBService.SearchContentsStreaming(device.ID, path, query, recursive: false, cts.Token, caseSensitive))
                        matches.Add(fileStat.FullPath);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (cts.IsCancellationRequested)
                return;

            App.SafeInvoke(() =>
            {
                if (cts.IsCancellationRequested)
                    return;

                Data.ContentSearchMatches = matches;
                RefreshExplorerFilter();
            });
        }, cts.Token);
    }

    private void UpdateExplorerView()
    {
        App.SafeInvoke(() =>
        {
            if (!Data.FileActions.IsExplorerVisible)
                return;

            var source = Instance.ExplorerSource;
            if (source is null)
                return;

            var view = CollectionViewSource.GetDefaultView(source);
            if (view is null)
                return;

            if (Data.FileActions.IsAppDrive)
            {
                view.Filter = Data.Settings.ShowSystemPackages
                    ? FileHelper.PkgFilter()
                    : pkg => ((Package)pkg).Type is Package.PackageType.User;

                // Default: Name, ascending.
                Instance.SortDirection ??= ListSortDirection.Ascending;
                Instance.SortedColumn ??= SortingSelector.SortingProperty.Name;

                // Bind first. DataGrid.OnItemsSourceChanged clears SortDescriptions that
                // don't match a column SortDirection, which used to wipe the sort applied
                // just before this assignment.
                Instance.ExplorerItemsSource = view;
                ApplyPackageSortToView(view, Instance.SortedColumn.Value, Instance.SortDirection.Value);
                return;
            }
            else
            {
                view.Filter = !Data.Settings.ShowHiddenItems
                    ? FileHelper.HideFiles()
                    : file => !FileHelper.IsHiddenRecycleItem((FileClass)file);

                Instance.SortDirection ??= ListSortDirection.Ascending;
                Instance.SortedColumn ??= SortingSelector.SortingProperty.Name;

                if (!view.SortDescriptions.Any(d => d.PropertyName
                        is nameof(FileClass.IsTemp)
                        or nameof(FileClass.IsDirectory)
                        or nameof(FileClass.SortName)))
                {
                    var dir = Instance.SortDirection.Value;
                    view.SortDescriptions.Add(new(nameof(FileClass.IsTemp), ListSortDirection.Descending));
                    view.SortDescriptions.Add(new(nameof(FileClass.IsDirectory), ListHelper.Invert(dir)));

                    var sortProp = Instance.SortedColumn.Value switch
                    {
                        SortingSelector.SortingProperty.Date => nameof(FileClass.ModifiedTime),
                        SortingSelector.SortingProperty.Size => nameof(FileClass.Size),
                        SortingSelector.SortingProperty.Type => $"{nameof(FolderViewModel)}.{nameof(FolderViewModel.TypeName)}",
                        _ => nameof(FileClass.SortName),
                    };

                    view.SortDescriptions.Add(new(sortProp, dir));
                }
            }

            Instance.ExplorerItemsSource = view;
        });
    }

    private void SubscribeDeviceList()
    {
        if (Data.DevicesObject?.UIList is null)
            return;

        if (!_uiListSubscribed)
        {
            Data.DevicesObject.UIList.CollectionChanged += UIList_CollectionChanged;
            _uiListSubscribed = true;
        }

        Tree.SubscribeDriveLists();
    }

    private void UIList_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Tree.SubscribeDriveLists();
        Tree.Sync();
    }

    private void UpdateDriveView() => UpdateDriveView(Instance);

    /// <summary>Populates the given tab's DriveItemsSource directly, instead of relying on it
    /// being the one this ViewModel happens to be subscribed to (the active tab) when
    /// IsDriveViewVisible changes - not guaranteed after an async continuation resumes.</summary>
    internal void UpdateDriveView(ExplorerInstance instance)
    {
        var source = instance.EffectiveDevice?.Drives;
        if (source is null)
            return;

        var view = CollectionViewSource.GetDefaultView(source);
        if (view is null)
            return;

        if (view.Filter is null)
        {
            Predicate<object> predicate = d =>
            {
                var drive = (DriveViewModel)d;

                return drive.Type switch
                {
                    AbstractDrive.DriveType.Trash => Data.Settings.EnableRecycle,
                    AbstractDrive.DriveType.Temp or AbstractDrive.DriveType.Package => Data.Settings.EnableApk,
                    _ => true,
                };
            };

            view.Filter = predicate;

            if (view.SortDescriptions.All(d => d.PropertyName != nameof(DriveViewModel.Type)))
                view.SortDescriptions.Add(new(nameof(DriveViewModel.Type), ListSortDirection.Ascending));
        }
        else
        {
            view.Refresh();
        }

        instance.DriveItemsSource = view;
    }

#if DEBUG
    [ObservableProperty]
    public partial string DebugApkLoadStatus { get; set; } = "";

    [RelayCommand]
    private void StopApkIconLoading()
    {
        ApkIconService.StopAllLoading();
        DebugApkLoadStatus = "Stopped — scroll will not load more icons";
    }

    [RelayCommand]
    private void ForceReloadSelectedApkIcon()
    {
        var selected = Data.SelectedPackages?.Where(static p => p is not null).ToList() ?? [];
        if (selected.Count == 0)
        {
            DebugApkLoadStatus = "No package selected";
            return;
        }

        if (selected.Count == 1)
        {
            var package = selected[0];
            DebugApkLoadStatus = $"Reloading {package.Name}…";
            ApkIconService.ForceReloadPackage(package, report =>
            {
                App.SafeBeginInvoke(() =>
                {
                    try { Clipboard.SetText(report); }
                    catch { /* clipboard busy */ }

                    var totalLine = report.Split('\n')
                        .LastOrDefault(static l => l.StartsWith("Total:", StringComparison.Ordinal))
                        ?.Trim();
                    DebugApkLoadStatus = string.IsNullOrEmpty(totalLine)
                        ? "Done — timing copied"
                        : $"{totalLine} — timing copied";
                });
            });
            return;
        }

        DebugApkLoadStatus = $"Reloading {selected.Count} packages…";
        var remaining = selected.Count;
        var reports = new System.Collections.Concurrent.ConcurrentBag<string>();
        foreach (var package in selected)
        {
            ApkIconService.ForceReloadPackage(package, report =>
            {
                reports.Add(report);
                App.SafeBeginInvoke(() =>
                {
                    remaining--;
                    if (remaining > 0)
                        return;

                    var combined = string.Join("\n---\n", reports);
                    try { Clipboard.SetText(combined); }
                    catch { /* clipboard busy */ }

                    DebugApkLoadStatus = $"{selected.Count} packages — timing copied";
                });
            });
        }
    }
#else
    // Bound from XAML (collapsed unless RuntimeSettings.IsDebug); keep members for release builds.
    public string DebugApkLoadStatus => "";
#endif

}
