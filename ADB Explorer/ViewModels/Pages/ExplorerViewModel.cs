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

    partial void OnExplorerSourceChanged(IEnumerable<IBrowserItem> value) => UpdateExplorerView();

    [ObservableProperty]
    public partial ICollectionView DriveItemsSource { get; set; }

    [ObservableProperty]
    public partial ListSortDirection? SortDirection { get; set; }

    [ObservableProperty]
    public partial SortingSelector.SortingProperty? SortedColumn { get; set; }

    private bool _suppressSortApply;

    private readonly DispatcherTimer _filterDebounceTimer;

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

        if (Data.FileActions.IsAppDrive || Data.FileActions.WasInAppDrive)
            return;

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
        IsIconView = value is not ThumbnailService.ThumbnailSize.Disabled;

        if (!Data.FileActions.IsAppDrive)
        {
            if (Data.Settings.ThumbSizePerLocation)
            {
                if (Data.Settings.LocationThumbSize.ContainsKey(Data.CurrentPath))
                {
                    Data.Settings.LocationThumbSize[Data.CurrentPath] = value;
                }
                else
                {
                    Data.Settings.LocationThumbSize.Add(Data.CurrentPath, value);
                }
            }

            Data.RuntimeSettings.ThumbsSize = value;
        }
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
        IsIconView = Data.RuntimeSettings.ThumbsSize != ThumbnailService.ThumbnailSize.Disabled;

        _filterDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _filterDebounceTimer.Tick += (s, e) =>
        {
            _filterDebounceTimer.Stop();
            RefreshExplorerFilter();
        };
        Data.DevicesObjectCreated += (_, _) => App.SafeInvoke(EnsureDevicesSubscription);
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
        };

        _isInitialized = true;
    }

    private void EnsureDevicesSubscription()
    {
        if (_devicesSubscribed || Data.DevicesObject is null)
            return;

        Data.DevicesObject.PropertyChanged += DevicesObject_PropertyChanged;
        _devicesSubscribed = true;
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
                break;

            case nameof(AppSettings.PollBattery):
                NotifyBatteryVisibility();
                break;

            case nameof(AppSettings.SidePane):
                RequestModeRefresh?.Invoke();
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
                IsIconView = !Data.FileActions.IsAppDrive && Data.RuntimeSettings.ThumbsSize != ThumbnailService.ThumbnailSize.Disabled;
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

            case nameof(FileActionsEnable.ExplorerFilter):
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

                if (view.SortDescriptions.All(d => d.PropertyName != nameof(Package.Type)))
                {
                    view.SortDescriptions.Add(new(nameof(Package.Type), ListSortDirection.Descending));
                }

                PackageTypeColumnSortDirection ??= ListSortDirection.Descending;
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

}
