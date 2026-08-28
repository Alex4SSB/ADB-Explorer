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

    [ObservableProperty]
    public partial ICollectionView ExplorerItemsSource { get; set; }

    [ObservableProperty]
    public partial IEnumerable<IBrowserItem> ExplorerSource { get; set; }

    public NavigationTreeViewModel Tree { get; }

    partial void OnExplorerSourceChanged(IEnumerable<IBrowserItem> value) => UpdateExplorerView();

    private bool _uiListSubscribed;

    [ObservableProperty]
    public partial ICollectionView DriveItemsSource { get; set; }

    [ObservableProperty]
    public partial ListSortDirection? SortDirection { get; set; }

    [ObservableProperty]
    public partial SortingSelector.SortingProperty? SortedColumn { get; set; }

    private bool _suppressSortApply;

    private readonly DispatcherTimer _filterDebounceTimer;
    private readonly DispatcherTimer _packageSortCatchUpTimer;

    partial void OnSortDirectionChanged(ListSortDirection? value)
    {
        if (!_suppressSortApply)
            ApplySortToView();
    }

    partial void OnSortedColumnChanged(SortingSelector.SortingProperty? value)
    {
        if (!_suppressSortApply)
            ApplySortToView();
    }

    private void ApplySortToView()
    {
        if (SortDirection is not { } dir || SortedColumn is not { } col || ExplorerItemsSource is not { } view)
            return;

        // SortExplorer() runs synchronously in _navigateToPath right after FileActions.IsAppDrive
        // flips for the new location, but ExplorerItemsSource/ExplorerSource are only swapped once
        // the new location's items (packages or files) actually arrive. Applying Package-only or
        // File-only SortDescriptions to the stale, mismatched view here would sort the wrong
        // (soon-to-be-discarded) collection instead of the one about to be shown.
        var sourceIsPackages = ExplorerSource is IEnumerable<Package>;
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

        PackageTypeColumnSortDirection = col is SortingSelector.SortingProperty.Type ? dir : null;

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
            if (!Data.FileActions.IsAppDrive || ExplorerItemsSource is not { } view)
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
        if (ExplorerItemsSource is not ListCollectionView { IsLiveSorting: true })
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
        SortedColumn = column;
        SortDirection = direction;
        _suppressSortApply = false;
        ApplySortToView();
    }

    [ObservableProperty]
    public partial ListSortDirection? PackageTypeColumnSortDirection { get; set; }

    [ObservableProperty]
    public partial bool IsIconView { get; set; } = false;

    [ObservableProperty]
    public partial ThumbnailService.ThumbnailSize CurrentThumbsSize { get; set; }

    [ObservableProperty]
    public partial ObservableList<SavedLocation> SavedItems { get; set; }

    partial void OnCurrentThumbsSizeChanged(ThumbnailService.ThumbnailSize value)
    {
        IsIconView = ThumbnailService.IsIconLayout(value);

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

    public int FirstSelectedIndex { get; set; } = -1;

    public int CurrentSelectedIndex { get; set; } = -1;

    public int NextSelectedIndex { get; set; }

    public bool IsMenuOpen { get; set; }

    public bool SelectionInProgress { get; set; }

    /// <summary>
    /// Sets index to First, Current, and Next
    /// </summary>
    public void SetIndexSingle(int value)
    {
        FirstSelectedIndex = value;
        CurrentSelectedIndex = value;
        NextSelectedIndex = value;
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

    public LogicalDeviceViewModel? CurrentDevice => Data.DevicesObject?.Current;

    public Battery? CurrentDeviceBattery => Data.DevicesObject?.Current?.Battery;

    public Action RequestModeRefresh { get; set; }

    public bool IsBatteryVisible =>
        Data.Settings.PollBattery
        && CurrentDeviceBattery?.ChargeState is not Battery.ChargingState.Unknown
        && CurrentDeviceBattery?.Level is not null;

    public ExplorerViewModel()
    {
        Tree = new(() => ExplorerSource);

        IsIconView = ThumbnailService.IsIconLayout(Data.RuntimeSettings.ThumbsSize);

        _filterDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _filterDebounceTimer.Tick += (s, e) =>
        {
            _filterDebounceTimer.Stop();
            RefreshExplorerFilter();
        };

        // After icon/label queue idle (+ progress hide debounce), wait a bit longer so scroll
        // bursts do not thrash live-sort catch-up.
        _packageSortCatchUpTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
        _packageSortCatchUpTimer.Tick += PackageSortCatchUpTimer_Tick;

        Data.DevicesObjectCreated += (_, _) => App.SafeInvoke(EnsureDevicesSubscription);

        ApkIconService.IconLoadProgressChanged += OnApkIconLoadProgressChanged;
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

        Data.FileActions.PropertyChanged += FileActions_PropertyChanged;
        Data.RuntimeSettings.PropertyChanged += RuntimeSettings_PropertyChanged;
        Data.Settings.PropertyChanged += Settings_PropertyChanged;

        EnsureDevicesSubscription();

        Data.CurrentPathO.PropertyChanged += (s, e) =>
        {
            RequestModeRefresh?.Invoke();
            Tree.Sync();
        };

        Tree.Sync();

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

    private void SavedLocations_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        App.SafeBeginInvoke(() =>
        {
            SavedItems = [.. Data.Settings.SavedLocations.Select(p => new SavedLocation(p))];
        });
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
                IsIconView = ThumbnailService.IsIconLayout(Data.RuntimeSettings.ThumbsSize);
                break;

            default:
                break;
        }
    }

    private void DevicesObject_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Devices.Current))
        {
            OnPropertyChanged(nameof(CurrentDevice));
            OnPropertyChanged(nameof(CurrentDeviceBattery));
            OnPropertyChanged(nameof(IsBatteryVisible));
            SubscribeToBattery(CurrentDeviceBattery);
            UpdateDriveView();
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
            ExplorerItemsSource?.Refresh();
        });
    }

    private void UpdateExplorerView()
    {
        App.SafeInvoke(() =>
        {
            if (!Data.FileActions.IsExplorerVisible)
                return;

            var source = ExplorerSource;
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
                SortDirection ??= ListSortDirection.Ascending;
                SortedColumn ??= SortingSelector.SortingProperty.Name;

                // Bind first. DataGrid.OnItemsSourceChanged clears SortDescriptions that
                // don't match a column SortDirection, which used to wipe the sort applied
                // just before this assignment.
                ExplorerItemsSource = view;
                ApplyPackageSortToView(view, SortedColumn.Value, SortDirection.Value);
                return;
            }
            else
            {
                view.Filter = !Data.Settings.ShowHiddenItems
                    ? FileHelper.HideFiles()
                    : file => !FileHelper.IsHiddenRecycleItem((FileClass)file);

                SortDirection ??= ListSortDirection.Ascending;
                SortedColumn ??= SortingSelector.SortingProperty.Name;

                if (!view.SortDescriptions.Any(d => d.PropertyName
                        is nameof(FileClass.IsTemp)
                        or nameof(FileClass.IsDirectory)
                        or nameof(FileClass.SortName)))
                {
                    var dir = SortDirection.Value;
                    view.SortDescriptions.Add(new(nameof(FileClass.IsTemp), ListSortDirection.Descending));
                    view.SortDescriptions.Add(new(nameof(FileClass.IsDirectory), ListHelper.Invert(dir)));

                    var sortProp = SortedColumn.Value switch
                    {
                        SortingSelector.SortingProperty.Date => nameof(FileClass.ModifiedTime),
                        SortingSelector.SortingProperty.Size => nameof(FileClass.Size),
                        SortingSelector.SortingProperty.Type => $"{nameof(FolderViewModel)}.{nameof(FolderViewModel.TypeName)}",
                        _ => nameof(FileClass.SortName),
                    };

                    view.SortDescriptions.Add(new(sortProp, dir));
                }
            }

            ExplorerItemsSource = view;
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

    private void UpdateDriveView()
    {
        var source = Data.DevicesObject?.Current?.Drives;
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

        DriveItemsSource = view;
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
