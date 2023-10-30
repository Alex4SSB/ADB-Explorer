using ADB_Explorer.Controls;
using ADB_Explorer.Converters;
using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;
using ADB_Explorer.Services.AppInfra;
using ADB_Explorer.ViewModels;
using static ADB_Explorer.Helpers.VisibilityHelper;
using static ADB_Explorer.Models.AbstractFile;
using static ADB_Explorer.Models.AdbExplorerConst;
using static ADB_Explorer.Models.Data;
using static ADB_Explorer.Resources.Strings;

namespace ADB_Explorer;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly DispatcherTimer ServerWatchdogTimer = new();
    private readonly DispatcherTimer ConnectTimer = new();
    private readonly Mutex connectTimerMutex = new();
    private readonly ThemeService themeService = new();
    private int clickCount = 0;
    private int firstSelectedRow = -1;

    private double ColumnHeaderHeight => (double)FindResource("DataGridColumnHeaderHeight");
    private double ScrollContentPresenterMargin => ((Thickness)FindResource("DataGridScrollContentPresenterMargin")).Top;
    private double DataGridContentWidth
        => StyleHelper.GetChildItemsPresenter(ExplorerGrid) is ItemsPresenter presenter ? presenter.ActualWidth : 0;


    public event PropertyChangedEventHandler PropertyChanged;
    protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public string SelectedFilesTotalSize => (SelectedFiles is not null && FileClass.TotalSize(SelectedFiles) is ulong size and > 0) ? size.ToSize() : "";

    private string prevPath = "";

    private Point MouseDownPoint;

    private bool IsInEditMode
    {
        get
        {
            if (FileActions.IsAppDrive)
                return false;

            var cell = CellConverter.GetDataGridCell(ExplorerGrid.SelectedCells[1]);
            return cell switch
            {
                null => false,
                _ => cell.IsEditing
            };
        }
        set
        {

            var cell = CellConverter.GetDataGridCell(ExplorerGrid.SelectedCells[1]);
            if (cell is not null)
            {
                cell.IsEditing = value;
                FileActions.IsExplorerEditing = value;
            }
        }
    }

    public MainWindow()
    {
        InitializeComponent();

        KeyDown += new KeyEventHandler(OnButtonKeyDown);

        DevicesObject = new();
        DevicesList.ItemsSource = DevicesObject.UIList;

        FileOpQ = new();
        Task launchTask = Task.Run(LaunchSequence);

        ConnectTimer.Interval = CONNECT_TIMER_INIT;
        ConnectTimer.Tick += ConnectTimer_Tick;

        ServerWatchdogTimer.Interval = RESPONSE_TIMER_INTERVAL;
        ServerWatchdogTimer.Tick += ServerWatchdogTimer_Tick;

        Settings.PropertyChanged += Settings_PropertyChanged;
        RuntimeSettings.PropertyChanged += RuntimeSettings_PropertyChanged;
        themeService.PropertyChanged += ThemeService_PropertyChanged;
        CommandLog.CollectionChanged += CommandLog_CollectionChanged;
        FileOpQ.PropertyChanged += FileOperationQueue_PropertyChanged;
        FileActions.PropertyChanged += FileActions_PropertyChanged;
        DevicesObject.PropertyChanged += DevicesObject_PropertyChanged;
        DevicesObject.UIList.CollectionChanged += UIList_CollectionChanged;
        
        var versionTask = AdbHelper.CheckAdbVersion();
        versionTask.ContinueWith((t) =>
        {
            if (!t.IsCanceled && t.Result)
            {
                Dispatcher.Invoke(() =>
                {
                    ConnectTimer.Start();
                    RuntimeSettings.IsDevicesViewEnabled = true;
                    RuntimeSettings.IsDevicesPaneOpen = true;
                });
            }
        });

        AppActions.Bindings.ForEach(binding => InputBindings.Add(binding));

        UpperProgressBar.DataContext = FileOpQ;
        RuntimeSettings.IsPastViewVisible = false;
        UpdateFileOp();

#if DEBUG
        DeviceHelper.TestDevices();
#endif

        Task.Run(() =>
        {
            SettingsHelper.SplashScreenTask();
            Task.WaitAll(launchTask, versionTask);
            RuntimeSettings.IsWindowLoaded = true;
        });
    }

    private void UIList_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        FilterDevices();
    }

    private void DevicesObject_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DevicesObject.UIList))
            FilterDevices();
    }

    private void ServerWatchdogTimer_Tick(object sender, EventArgs e)
    {
        RuntimeSettings.LastServerResponse = RuntimeSettings.LastServerResponse;

        if (Settings.PollDevices
            && MdnsService?.State is MDNS.MdnsState.Running
            && DateTime.Now.Subtract(RuntimeSettings.LastServerResponse) > MDNS_FORCE_CONNECT_TIME)
        {
            MdnsService.State = MDNS.MdnsState.Disabled;
            UpdateMdns();
        }
    }

    private void RuntimeSettings_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            switch (e.PropertyName)
            {
                case nameof(AppRuntimeSettings.IsMdnsExpanderOpen):
                    if (MdnsService?.State is not MDNS.MdnsState.Disabled)
                    {
                        DeviceHelper.CollapseDevices();

                        if (RuntimeSettings.IsMdnsExpanderOpen)
                            UpdateQrClass();
                    }
                    break;

                case nameof(AppRuntimeSettings.SearchText):
                    FilterSettings();
                    break;

                case nameof(AppRuntimeSettings.BrowseDrive) when RuntimeSettings.BrowseDrive:
                    InitNavigation(RuntimeSettings.BrowseDrive.Path);
                    break;


                case nameof(AppRuntimeSettings.PathBoxNavigation):
                    if (RuntimeSettings.PathBoxNavigation == "-")
                        NavigateToLocation(NavHistory.GoBack(), true);
                    else
                    {
                        if (FileActions.IsExplorerVisible)
                            NavigateToLocation(RuntimeSettings.PathBoxNavigation);
                        else
                        {
                            if (!InitNavigation(RuntimeSettings.PathBoxNavigation))
                            {
                                DriveViewNav();
                                NavHistory.Navigate(NavHistory.SpecialLocation.DriveView);
                                return;
                            }
                        }
                    }
                    break;

                case nameof(AppRuntimeSettings.LocationToNavigate):
                    switch (RuntimeSettings.LocationToNavigate)
                    {
                        case NavHistory.SpecialLocation.Back:
                            NavigateToLocation(NavHistory.GoBack(), true);
                            break;
                        case NavHistory.SpecialLocation.Forward:
                            NavigateToLocation(NavHistory.GoForward(), true);
                            break;
                        case NavHistory.SpecialLocation.Up:
                            NavigateToPath(ParentPath);
                            break;
                        case not null:
                            if (FileActions.IsDriveViewVisible
                            && (RuntimeSettings.LocationToNavigate is NavHistory.SpecialLocation.DriveView
                                || (RuntimeSettings.LocationToNavigate is string location && location == NavHistory.StringFromLocation(NavHistory.SpecialLocation.DriveView))))
                                FileActionLogic.RefreshDrives(true);
                            else
                            {
                                NavHistory.Navigate(RuntimeSettings.LocationToNavigate);
                                NavigateToLocation(RuntimeSettings.LocationToNavigate);
                            }
                            break;
                        default:
                            break;
                    }
                    break;

                case nameof(AppRuntimeSettings.NewFolder):
                    NewItem(true);
                    break;

                case nameof(AppRuntimeSettings.NewFile):
                    NewItem(false);
                    break;

                case nameof(AppRuntimeSettings.Rename):
                    IsInEditMode ^= true;
                    break;

                case nameof(AppRuntimeSettings.SelectAll):
                    if (ExplorerGrid.Items.Count == ExplorerGrid.SelectedItems.Count)
                        ExplorerGrid.UnselectAll();
                    else
                        ExplorerGrid.SelectAll();
                    break;

                case nameof(AppRuntimeSettings.Refresh):
                    RefreshLocation();
                    break;

                case nameof(AppRuntimeSettings.IsPathBoxFocused):
                    IsPathBoxFocused(RuntimeSettings.IsPathBoxFocused is null
                        ? NavigationBox.Mode is NavigationBox.ViewMode.Breadcrumbs
                        : RuntimeSettings.IsPathBoxFocused.Value);

                    RuntimeSettings.AutoHideSearchBox = true;
                    break;

                case nameof(AppRuntimeSettings.IsSearchBoxFocused):
                    if (!RuntimeSettings.IsSearchBoxFocused)
                        FileOperationsSplitView.Focus();
                    break;

                case nameof(AppRuntimeSettings.AutoHideSearchBox):
                    if (Width < MAX_WINDOW_WIDTH_FOR_SEARCH_AUTO_COLLAPSE)
                        RuntimeSettings.IsSearchBoxFocused = false;

                    if (NavigationBox.Mode is not NavigationBox.ViewMode.Path)
                        FileOperationsSplitView.Focus();
                    break;

                case nameof(AppRuntimeSettings.ExplorerSource):
                    ExplorerGrid.ItemsSource = RuntimeSettings.ExplorerSource;
                    FilterExplorerItems();
                    break;

                case nameof(AppRuntimeSettings.FilterDrives):
                    FilterDrives();
                    break;

                case nameof(AppRuntimeSettings.FilterDevices):
                    FilterDevices();
                    break;

                case nameof(AppRuntimeSettings.FilterActions):
                    FilterFileActions();
                    FilterExplorerContextMenu();
                    break;

                case nameof(AppRuntimeSettings.ClearNavBox):
                    ClearNavBox();
                    break;

                case nameof(AppRuntimeSettings.InitLister):
                    InitLister();
                    break;

                case nameof(AppRuntimeSettings.DriveViewNav):
                    DriveViewNav();
                    break;

                case nameof(AppRuntimeSettings.GroupsExpanded):
                    SettingsAboutExpander.IsExpanded = RuntimeSettings.GroupsExpanded;
                    break;

                case nameof(AppRuntimeSettings.RefreshFileOpControls):
                    FileOpControlsMenu.Items.Refresh();
                    break;

                case nameof(AppRuntimeSettings.IsPastViewVisible):
                    CurrentOperationDataGrid.ItemsSource = RuntimeSettings.IsPastViewVisible is true
                        ? FileOpQ.PastOperations
                        : FileOpQ.Operations;

                    UpdateSelectedFileOp();
                    break;

                case nameof(AppRuntimeSettings.ClearLogs):
                    ClearLogs();
                    break;

                case nameof(AppRuntimeSettings.RefreshSettingsControls):
                    SettingsControlsMenu.Items.Refresh();
                    break;
            }
        });
    }

    private void ClearNavBox()
    {
        NavigationBox.Path = null;
        NavigationBox.Mode = NavigationBox.ViewMode.None;
    }

    private void SettingsSearchBox_FocusChanged(object sender, RoutedEventArgs e)
    {
        if (SettingsSearchBox.IsFocused)
            RuntimeSettings.IsOperationsViewOpen = false;

        if (!Settings.DisableAnimation)
            Settings.IsAnimated = !SettingsSearchBox.IsFocused;
    }

    private void FileActions_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FileActionsEnable.RefreshPackages) && FileActions.RefreshPackages)
        {
            if (FileActions.IsAppDrive)
            {
                Dispatcher.Invoke(() => _navigateToPath(CurrentPath));
                FileActions.RefreshPackages = false;
            }
        }
        else if (e.PropertyName == nameof(FileActionsEnable.ExplorerFilter))
        {
            FilterExplorerItems();
        }
        else if (e.PropertyName == nameof(FileActionsEnable.ItemToSelect) && FileActions.ItemToSelect is not null)
        {
            ExplorerGrid.ScrollIntoView(FileActions.ItemToSelect);
            ExplorerGrid.SelectedItem = FileActions.ItemToSelect;
        }
        else if (e.PropertyName == nameof(FileActionsEnable.PasteEnabled))
        {
            FilterFileActions();
            FilterExplorerContextMenu();
        }
    }

    private void FileOperationQueue_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (FileOperationQueue.NotifyProperties.Contains(e.PropertyName))
            UpdateFileOp();

        if (e.PropertyName is nameof(FileOperationQueue.CurrentOperation))
            RefreshFileOps();

        UpdateSelectedFileOp();
    }

    private void RefreshFileOps()
    {
        var collectionView = CollectionViewSource.GetDefaultView(CurrentOperationDetailedDataGrid.ItemsSource);
        if (collectionView is null)
            return;

        collectionView.SortDescriptions.Clear();
        collectionView.SortDescriptions.Add(new(nameof(FileOperation.Status), ListSortDirection.Ascending));
    }

    private void UpdateFileOp()
    {
        FileActionLogic.UpdateFileOpControls();
        RuntimeSettings.RefreshFileOpControls = true;

        if (FileOpQ.AnyFailedOperations)
            TaskbarItemInfo.ProgressState = TaskbarItemProgressState.Error;
        else if (FileOpQ.IsActive)
        {
            if (FileOpQ.Progress == 0)
                TaskbarItemInfo.ProgressState = TaskbarItemProgressState.Indeterminate;
            else
                TaskbarItemInfo.ProgressState = TaskbarItemProgressState.Normal;
        }
        else
            TaskbarItemInfo.ProgressState = TaskbarItemProgressState.None;
    }

    private void CommandLog_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is null || Dispatcher.HasShutdownStarted)
            return;
        
        if (e.OldItems is null || e.OldItems.Count < 1)
            Dispatcher.Invoke(LogControlsPanel.Items.Refresh);

        foreach (Log item in e.NewItems)
        {
            if (Dispatcher.HasShutdownStarted || RuntimeSettings.IsLogPaused)
                return;

            Dispatcher.Invoke(() =>
            {
                LogTextBox.Text += $"{item}\n";
                LogTextBox.CaretIndex = LogTextBox.Text.Length;
                LogTextBox.ScrollToEnd();
            });
        }
    }

    private void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppSettings.ForceFluentStyles):
                SettingsHelper.SetSymbolFont();
                NavigationBox.Refresh();
                break;
            case nameof(AppSettings.Theme):
                SetTheme(Settings.Theme);
                break;
            case nameof(AppSettings.EnableMdns):
                AdbHelper.EnableMdns();
                break;
            case nameof(AppSettings.ShowHiddenItems):
                FilterExplorerItems();
                break;
            case nameof(AppSettings.ShowSystemPackages):
                if (DevicesObject.Current is null)
                    return;

                if (FileActions.IsExplorerVisible)
                    FilterExplorerItems();
                else
                    FileActionLogic.UpdatePackages();
                break;
            case nameof(AppSettings.EnableLog):
                FileActions.IsLogToggleVisible.Value = Settings.EnableLog;
                
                if (!Settings.EnableLog)
                {
                    AppActions.ToggleActions.Find(a => a.FileAction.Name is FileAction.FileActionType.LogToggle).Toggle(false);
                    ClearLogs();
                }

                break;
            case nameof(AppSettings.SwRender):
                SetRenderMode();
                break;
            case nameof(AppSettings.EnableRecycle) or nameof(AppSettings.EnableApk):
                if (DevicesObject.Current is null)
                    return;

                FileActionLogic.UpdateFileActions();

                if (NavHistory.Current is NavHistory.SpecialLocation.DriveView)
                {
                    FileActionLogic.RefreshDrives(true);
                    FilterDrives();
                }

                break;
            case nameof(AppSettings.SaveDevices):
                if (Settings.SaveDevices && !DevicesObject.HistoryDeviceViewModels.Any())
                    DevicesObject.RetrieveHistoryDevices();

                FilterDevices();
                break;
            default:
                break;
        }
    }

    private void DirectoryLister_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DirectoryLister.IsProgressVisible))
        {
            UnfinishedBlock.Visible(DirList.IsProgressVisible);
            NavigationBox.IsLoadingProgressVisible = DirList.IsProgressVisible;
        }
        else if (e.PropertyName == nameof(DirectoryLister.InProgress))
        {
            Task.Run(() =>
            {
                Task.Delay(EMPTY_FOLDER_NOTICE_DELAY);
                Dispatcher.Invoke(() => FileActions.ListingInProgress = DirList.InProgress);
            });

            if (DirList.InProgress)
                return;

            if (FileActions.IsRecycleBin)
            {
                TrashHelper.EnableRecycleButtons();
            }

            if (string.IsNullOrEmpty(prevPath))
                return;

            var prevItem = DirList.FileList.Where(item => item.FullPath == prevPath);
            if (prevItem.Any())
            {
                ExplorerGrid.SelectedIndex = ExplorerGrid.Items.IndexOf(prevItem.First());
            }
        }
    }

    private void ThemeService_PropertyChanged(object sender, PropertyChangedEventArgs e) =>
        Dispatcher.Invoke(SetTheme);

    

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        DirList?.Stop();

        ConnectTimer.Stop();
        ServerWatchdogTimer.Stop();
        StoreClosingValues();
    }

    private void StoreClosingValues()
    {
        Storage.StoreValue(AppSettings.SystemVals.windowMaximized, WindowState == WindowState.Maximized);

        var detailedVisible = RuntimeSettings.IsOperationsViewOpen && Settings.ShowExtendedView;
        Storage.StoreValue(AppSettings.SystemVals.detailedVisible, detailedVisible);
        if (detailedVisible)
            Storage.StoreValue(AppSettings.SystemVals.detailedHeight, FileOpDetailedGrid.Height);
    }

    private void SetTheme()
    {
        SetTheme(Settings.Theme is AppSettings.AppTheme.windowsDefault
            ? themeService.WindowsTheme
            : ThemeManager.Current.ApplicationTheme.Value);
    }

    private void SetTheme(AppSettings.AppTheme theme) => SetTheme(AppThemeToActual(theme));

    private void SetTheme(ApplicationTheme theme) => Dispatcher.Invoke(() =>
    {
        ThemeManager.Current.ApplicationTheme = theme;

        Task.Run(() =>
        {
            var keys = ((ResourceDictionary)Application.Current.Resources["DynamicBrushes"]).Keys;
            string[] brushes = new string[keys.Count];
            keys.CopyTo(brushes, 0);

            Parallel.ForEach(brushes, (brush) =>
            {
                SetResourceColor(theme, brush);
            });
        });
    });

    private static void SetResourceColor(ApplicationTheme theme, string resource)
    {
        App.Current.Dispatcher.Invoke(() => Application.Current.Resources[resource] = new SolidColorBrush((Color)Application.Current.Resources[$"{theme}{resource}"]));
    }

    private void IsPathBoxFocused(bool isFocused)
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

            NavigationBox.UnfocusTarget = FileOperationsSplitView;
            NavigationBox.Mode = NavigationBox.ViewMode.Breadcrumbs;
        }
    }

    private void DataGridRow_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && !FileActions.IsAppDrive && SelectedFiles.Count() == 1 && !IsInEditMode)
            DoubleClick(ExplorerGrid.SelectedItem);
    }

    private void DoubleClick(object source)
    {
        if (source is FileClass file && !FileActions.IsRecycleBin)
        {
            switch (file.Type)
            {
                case FileType.File:
                    switch (Settings.DoubleClick)
                    {
                        case AppSettings.DoubleClickAction.pull when Settings.IsPullOnDoubleClickEnabled:
                            FileActionLogic.PullFiles(true);
                            break;
                        case AppSettings.DoubleClickAction.edit when FileActions.EditFileEnabled:
                            FileActionLogic.OpenEditor();
                            break;
                        default:
                            break;
                    }
                    break;
                case FileType.Folder:
                    NavigateToPath(file.FullPath);
                    break;
                default:
                    break;
            }
        }
    }

    private void ExplorerGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ExplorerGrid.SelectedItems.Count > 0 && !RuntimeSettings.IsExplorerLoaded)
        {
            ExplorerGrid.UnselectAll();
            return;
        }

        if (!SelectionHelper.GetSelectionInProgress(ExplorerGrid))
        {
            if (ExplorerGrid.SelectedItems.Count == 1)
            {
                SelectionHelper.SetFirstSelectedIndex(ExplorerGrid, ExplorerGrid.SelectedIndex);
                SelectionHelper.SetCurrentSelectedIndex(ExplorerGrid, ExplorerGrid.SelectedIndex);
            }
            else if (ExplorerGrid.SelectedItems.Count > 1 && e.AddedItems.Count == 1)
            {
                SelectionHelper.SetCurrentSelectedIndex(ExplorerGrid, ExplorerGrid.Items.IndexOf(e.AddedItems[0]));
            }
        }

        SelectedFiles = FileActions.IsAppDrive ? Enumerable.Empty<FileClass>() : ExplorerGrid.SelectedItems.OfType<FileClass>();
        SelectedPackages = FileActions.IsAppDrive ? ExplorerGrid.SelectedItems.OfType<Package>() : Enumerable.Empty<Package>();
        OnPropertyChanged(nameof(SelectedFilesTotalSize));

        if (SelectionHelper.GetIsMenuOpen(ExplorerGrid.ContextMenu))
        {
            Task.Run(() => Task.Delay(SELECTION_CHANGED_DELAY)).ContinueWith((t) => Dispatcher.Invoke(() => FileActionLogic.UpdateFileActions()));
        }
        else
            FileActionLogic.UpdateFileActions();
    }

    /// <summary>
    /// Refresh file actions menu to update their ToolTips
    /// </summary>
    private void FilterFileActions() => MainToolBar.Items?.Refresh();

    private void FilterExplorerContextMenu()
    {
        var collectionView = CollectionViewSource.GetDefaultView(ExplorerGrid.ContextMenu.ItemsSource);
        if (collectionView is null)
            return;

        Predicate<object> predicate = m =>
        {
            var menu = m as SubMenu;

            if (menu.Children is null)
                return menu.Action.Command.IsEnabled;
            else
                return menu.Action.Command.IsEnabled && menu.Children.Any(child => child.Action.Command.IsEnabled);
        };

        collectionView.Filter = new(predicate);
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        RuntimeSettings.IsPathBoxFocused = false;
    }

    private void LaunchSequence()
    {
        var height = SystemParameters.PrimaryScreenHeight;
        Dispatcher.BeginInvoke(() =>
        {
            Height = height * WINDOW_HEIGHT_RATIO;
            Width = Height / WINDOW_WIDTH_RATIO;
        });

        LoadSettings();
        InitFileOpColumns();

        DeviceHelper.UpdateWsaPkgStatus();

        SettingsHelper.CheckForUpdates();
    }

    private void FilterDevices() => DeviceHelper.FilterDevices(CollectionViewSource.GetDefaultView(DevicesList.ItemsSource));

    private void InitLister()
    {
        DirList = new(Dispatcher, CurrentADBDevice, FileHelper.ListerFileManipulator);
        DirList.PropertyChanged += DirectoryLister_PropertyChanged;
    }

    private void LoadSettings()
    {
        Dispatcher.Invoke(SettingsHelper.SetSymbolFont);

        SetTheme(Settings.Theme);
        SetRenderMode();

        AdbHelper.EnableMdns();

        UISettings.Init();

        Dispatcher.Invoke(() =>
        {
            SettingsList.ItemsSource = UISettings.GroupedSettings;
            SortedSettings.ItemsSource = UISettings.SortSettings;

            NavigationToolBar.ItemsSource = Services.NavigationToolBar.List;
            MainToolBar.ItemsSource = Services.MainToolBar.List;
            FileActionLogic.UpdateFileActions();

            FilterDevices();

            FileActions.IsLogToggleVisible.Value = Settings.EnableLog;
        });

        Settings.RootArgs ??= new[] { "root" };
        Settings.UnrootArgs ??= new[] { "unroot" };
        Settings.UnrootOnDisconnect ??= false;
    }

    private void SetRenderMode() => Dispatcher.Invoke(() =>
    {
        if (Settings.SwRender)
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        else if (RenderOptions.ProcessRenderMode == RenderMode.SoftwareOnly)
            RenderOptions.ProcessRenderMode = RenderMode.Default;
    });

    private ApplicationTheme AppThemeToActual(AppSettings.AppTheme appTheme) => appTheme switch
    {
        AppSettings.AppTheme.light => ApplicationTheme.Light,
        AppSettings.AppTheme.dark => ApplicationTheme.Dark,
        AppSettings.AppTheme.windowsDefault => themeService.WindowsTheme,
        _ => throw new NotSupportedException(),
    };

    private void InitFileOpColumns() => Dispatcher.Invoke(() =>
    {
        FileOpColumns.Init();

        FileOpColumns.List.ForEach(col => col.Column = GetColumnFromType(col.Type));
        foreach (var item in FileOpColumns.List)
        {
            var column = GetColumnFromType(item.Type);
            item.Column = column;

            if (item.Icon is null)
                column.Header = item.Name;
            else
            {
                column.Header = new FontIcon()
                {
                    Glyph = item.Icon,
                    ToolTip = item.Name,
                };
            }
        }

        DataGridColumn GetColumnFromType(FileOpColumnConfig.ColumnType columnType) => columnType switch
        {
            FileOpColumnConfig.ColumnType.OpType => OpTypeColumn,
            FileOpColumnConfig.ColumnType.FileName => FileNameColumn,
            FileOpColumnConfig.ColumnType.Progress => ProgressColumn,
            FileOpColumnConfig.ColumnType.Source => SourceColumn,
            FileOpColumnConfig.ColumnType.Dest => DestColumn,
            FileOpColumnConfig.ColumnType.TimeStamp => TimeColumn,
            _ => throw new NotSupportedException(),
        };
    });

    private FileOpColumnConfig.ColumnType GetColumnType(DataGridColumn column)
    {
        if (column == OpTypeColumn) return FileOpColumnConfig.ColumnType.OpType;
        if (column == FileNameColumn) return FileOpColumnConfig.ColumnType.FileName;
        if (column == ProgressColumn) return FileOpColumnConfig.ColumnType.Progress;
        if (column == SourceColumn) return FileOpColumnConfig.ColumnType.Source;
        if (column == DestColumn) return FileOpColumnConfig.ColumnType.Dest;
        if (column == TimeColumn) return FileOpColumnConfig.ColumnType.TimeStamp;

        throw new NotSupportedException();
    }

    private void RefreshLocation()
    {
        if (FileActions.IsDriveViewVisible)
            FileActionLogic.RefreshDrives(true);
        else
            _navigateToPath(CurrentPath);
    }

    private void DriveViewNav()
    {
        FileActionLogic.ClearExplorer(false);
        FileActions.IsDriveViewVisible = true;
        UpdateFileOp();

        NavigationBox.Mode = NavigationBox.ViewMode.Breadcrumbs;
        NavigationBox.Path = NavHistory.StringFromLocation(NavHistory.SpecialLocation.DriveView);
        
        DriveList.ItemsSource = DevicesObject.Current.Drives;

        if (DriveList.SelectedIndex > -1)
            SelectionHelper.GetListViewItemContainer(DriveList).Focus();
    }

    private bool InitNavigation(string path = "", bool bfNavigated = false)
    {
        if (path is null)
            return true;

        var realPath = FolderHelper.FolderExists(string.IsNullOrEmpty(path) ? DEFAULT_PATH : path);
        if (realPath is null)
            return false;

        FileActions.IsDriveViewVisible = false;
        FileActions.IsExplorerVisible = true;
        FileActions.HomeEnabled = true;
        RuntimeSettings.BrowseDrive = null;

        UpdateFileOp();

        Task.Delay(EXPLORER_NAV_DELAY).ContinueWith((t) => Dispatcher.Invoke(() => RuntimeSettings.IsExplorerLoaded = true));

        if (Width > MAX_WINDOW_WIDTH_FOR_SEARCH_AUTO_COLLAPSE)
            RuntimeSettings.IsSearchBoxFocused = true;

        return _navigateToPath(realPath, bfNavigated);
    }

    private void ListDevices(IEnumerable<LogicalDevice> devices)
    {
        RuntimeSettings.LastServerResponse = DateTime.Now;

        if (devices is null)
            return;

        var deviceVMs = devices.Select(d => new LogicalDeviceViewModel(d));

        if (!DevicesObject.DevicesChanged(deviceVMs))
            return;

        DeviceHelper.DeviceListSetup(deviceVMs);

        if (!Settings.AutoRoot)
            return;

        foreach (var item in DevicesObject.LogicalDeviceViewModels.Where(device => device.Root is AbstractDevice.RootStatus.Unchecked))
        {
            Task.Run(() => item.EnableRoot(true));
        }
    }

    private void ConnectTimer_Tick(object sender, EventArgs e)
    {
        if (!ServerWatchdogTimer.IsEnabled)
            ServerWatchdogTimer.Start();

        if (ConnectTimer.Interval == CONNECT_TIMER_INIT)
            ConnectTimer.Interval = CONNECT_TIMER_INTERVAL;

        Task.Run(() =>
        {
            if (!connectTimerMutex.WaitOne(0))
            {
                return;
            }

            if (Settings.PollDevices)
            {
                RefreshDevices();
            }

            if (Settings.PollBattery)
            {
                DeviceHelper.UpdateDevicesBatInfo();
            }

            if (FileActions.IsDriveViewVisible && Settings.PollDrives)
            {
                try
                {
                    Dispatcher.Invoke(() => FileActionLogic.RefreshDrives(true));
                }
                catch (Exception)
                { }
            }

            if (RuntimeSettings.IsDevicesPaneOpen)
            {
                DeviceHelper.UpdateDevicesRootAccess();

                DeviceHelper.UpdateWsaPkgStatus();
            }

            connectTimerMutex.ReleaseMutex();
        });
    }

    private void RefreshDevices()
    {
        Dispatcher.BeginInvoke(new Action<IEnumerable<LogicalDevice>>(ListDevices), ADBService.GetDevices()).Wait();

        Task.Run(DeviceHelper.ConnectWsaDevice);

        if (!RuntimeSettings.IsDevicesPaneOpen)
            return;

        Dispatcher.Invoke(DevicesObject.UpdateLogicalIp);

        if (MdnsService.State is MDNS.MdnsState.Running)
            Dispatcher.BeginInvoke(new Action<IEnumerable<ServiceDevice>>(DeviceHelper.ListServices), WiFiPairingService.GetServices()).Wait();
    }

    public bool NavigateToPath(string path, bool bfNavigated = false)
    {
        if (path is null)
            return false;

        var realPath = FolderHelper.FolderExists(path);
        FileActions.ExplorerFilter = "";

        return realPath is not null && _navigateToPath(realPath, bfNavigated);
    }

    private bool _navigateToPath(string realPath, bool bfNavigated = false)
    {
        if (bfNavigated)
            prevPath = CurrentPath;
        else
        {
            NavHistory.Navigate(realPath);

            SelectionHelper.SetFirstSelectedIndex(ExplorerGrid, -1);
            SelectionHelper.SetCurrentSelectedIndex(ExplorerGrid, -1);

            prevPath = "";
        }

        ExplorerGrid.Focus();
        CurrentPath = realPath;

        NavigationBox.Path = realPath == RECYCLE_PATH ? NavHistory.StringFromLocation(NavHistory.SpecialLocation.RecycleBin) : realPath;
        ParentPath = CurrentPath[..FilePath.LastSeparatorIndex(CurrentPath)];

        FileActions.IsRecycleBin = CurrentPath == RECYCLE_PATH;
        FileActions.IsAppDrive = CurrentPath == NavHistory.StringFromLocation(NavHistory.SpecialLocation.PackageDrive);
        FileActions.IsTemp = CurrentPath == TEMP_PATH;
        FileActions.ParentEnabled = CurrentPath != ParentPath && !FileActions.IsRecycleBin && !FileActions.IsAppDrive;
        FileActions.PasteEnabled = FileActionLogic.IsPasteEnabled();
        FileActions.PushPackageEnabled = Settings.EnableApk && DevicesObject?.Current?.Type is not AbstractDevice.DeviceType.Recovery;
        FileActions.InstallPackageEnabled = FileActions.IsTemp && DevicesObject?.Current?.Type is not AbstractDevice.DeviceType.Recovery;
        FileActions.UninstallPackageEnabled = false;
        FileActions.ContextPushPackagesEnabled =
        FileActions.IsUninstallVisible.Value = FileActions.IsAppDrive;

        FileActions.PushFilesFoldersEnabled =
        FileActions.ContextNewEnabled =
        FileActions.ContextPushEnabled =
        FileActions.NewEnabled = FileActions.PushPullEnabled && !FileActions.IsRecycleBin && !FileActions.IsAppDrive;

        OriginalPath.Visibility =
        OriginalDate.Visibility = Visible(FileActions.IsRecycleBin);

        PackageName.Visibility =
        PackageType.Visibility =
        PackageUid.Visibility =
        PackageVersion.Visibility = Visible(FileActions.IsAppDrive);

        IconColumn.Visibility =
        NameColumn.Visibility =
        DateColumn.Visibility =
        TypeColumn.Visibility =
        SizeColumn.Visibility = Visible(!FileActions.IsAppDrive);

        FileActions.CopyPathAction.Value = FileActions.IsAppDrive ? S_COPY_APK_NAME : S_COPY_PATH;

        if (FileActions.IsRecycleBin)
        {
            FileActions.ListingInProgress = true;
            TrashHelper.ParseIndexers().ContinueWith((t) => DirList.Navigate(realPath));

            DateColumn.Header = S_DATE_DEL_COL;
            FileActions.DeleteAction.Value = S_EMPTY_TRASH;
            FileActions.RestoreAction.Value = S_RESTORE_ALL;
        }
        else
        {
            if (FileActions.IsAppDrive)
            {
                FileActionLogic.UpdatePackages(true);
                FileActionLogic.UpdateFileActions();
                return true;
            }

            DirList.Navigate(realPath);

            DateColumn.Header = S_DATE_MOD_COL;
            FileActions.DeleteAction.Value = S_DELETE_ACTION;
        }

        RuntimeSettings.ExplorerSource = DirList.FileList;
        FileActionLogic.UpdateFileActions();
        return true;
    }

    private void NavigateToLocation(object location, bool bfNavigated = false)
    {
        SelectionHelper.SetIsMenuOpen(ExplorerGrid.ContextMenu, false);

        if (location is string path)
        {
            if (!FileActions.IsExplorerVisible)
                InitNavigation(path, bfNavigated);
            else
                NavigateToPath(path, bfNavigated);
        }
        else if (location is NavHistory.SpecialLocation.DriveView)
        {
            FileActions.IsRecycleBin = false;
            RuntimeSettings.IsPathBoxFocused = false;
            FileActionLogic.RefreshDrives();
            DriveViewNav();

            FileActionLogic.UpdateFileActions();
        }
    }

    private void Window_MouseUp(object sender, MouseButtonEventArgs e) => e.Handled = e.ChangedButton switch
    {
        MouseButton.XButton1 => NavHistory.NavigateBF(NavHistory.SpecialLocation.Back),
        MouseButton.XButton2 => NavHistory.NavigateBF(NavHistory.SpecialLocation.Forward),
        _ => false,
    };

    private void DataGridRow_KeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key;
        if (key is Key.Enter)
        {
            if (IsInEditMode)
                return;

            if (ExplorerGrid.SelectedItems.Count == 1 && ExplorerGrid.SelectedItem is FilePath file && file.IsDirectory)
                DoubleClick(ExplorerGrid.SelectedItem);
        }
        else if (key == Key.Back)
        {
            NavHistory.NavigateBF(NavHistory.SpecialLocation.Back);
        }
        else if (key == Key.Delete && FileActions.DeleteEnabled)
        {
            FileActionLogic.DeleteFiles();
        }
        else if (key is Key.Up or Key.Down)
        {
            if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
                ExplorerGrid.MultiSelect(key);
            else
                ExplorerGrid.SingleSelect(key);
        }
        else if (key is Key.F2)
        {
            AppActions.List.First(action => action.Name is FileAction.FileActionType.Rename).Command.Execute();
        }
        else
            return;

        e.Handled = true;
    }

    private void OnButtonKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Up or Key.Down or Key.Left or Key.Right or Key.Escape or Key.Home or Key.End)
        {
            bool handle = false;

            if (FileActions.IsExplorerVisible)
            {
                handle |= ExplorerGridKeyNavigation(e.Key);
            }
            else if (FileActions.IsDriveViewVisible)
            {
                handle |= DriveViewKeyNavigation(e.Key);
            }

            e.Handled = handle;
        }
    }

    private bool DriveViewKeyNavigation(Key key)
    {
        if (DriveList.Items.Count == 0)
            return false;

        if (DriveList.SelectedItems.Count == 0)
        {
            if (key is Key.Left or Key.Up)
                DriveList.SelectedIndex = DriveList.Items.Count - 1;
            else if (key is Key.Right or Key.Down)
                DriveList.SelectedIndex = 0;
            else
                return false;

            SelectionHelper.GetListViewItemContainer(DriveList).Focus();
            return true;
        }

        if (key is Key.Enter)
        {
            ((DriveViewModel)DriveList.SelectedItem).BrowseCommand.Execute();
            return true;
        }

        if (key is Key.Escape)
        {
            // Should've been clear selected drives, but causes inconsistent behavior
            return true;
        }

        return false;
    }

    private bool ExplorerGridKeyNavigation(Key key)
    {
        if (ExplorerGrid.Items.Count < 1)
            return false;

        switch (key)
        {
            case Key.Down or Key.Up or Key.Home or Key.End:
                if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
                    ExplorerGrid.MultiSelect(key);
                else
                    ExplorerGrid.SingleSelect(key);
                break;
            case Key.Enter:
                if (ExplorerGrid.SelectedCells.Count < 1 || IsInEditMode)
                    return false;

                if (ExplorerGrid.SelectedItems.Count == 1 && ((FilePath)ExplorerGrid.SelectedItem).IsDirectory)
                    DoubleClick(ExplorerGrid.SelectedItem);
                break;
            default:
                return false;
        }

        return true;
    }

    private void DataGridRow_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is MouseButton.XButton1 or MouseButton.XButton2)
            return;

        var row = sender as DataGridRow;

        if (!row.IsSelected)
        {
            ExplorerGrid.UnselectAll();
        }

        if (e.OriginalSource is not Border)
        {
            row.IsSelected = true;

            SelectionHelper.SetCurrentSelectedIndex(ExplorerGrid, row.GetIndex());
            SelectionHelper.SetFirstSelectedIndex(ExplorerGrid, row.GetIndex());
        }
    }

    private void ExplorerGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var point = Mouse.GetPosition(ExplorerGrid);
        if (point.Y < ColumnHeaderHeight)
            e.Handled = true;

        SelectionHelper.SetIsMenuOpen(ExplorerGrid.ContextMenu, true);
        FileActionLogic.UpdateFileActions();
    }

    private void ExplorerGrid_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var point = e.GetPosition(ExplorerGrid);
        MouseDownPoint = point;
        var actualRowWidth = 0.0;
        int selectionIndex;

        foreach (var item in ExplorerGrid.Columns.Where(col => col.Visibility == Visibility.Visible))
        {
            actualRowWidth += item.ActualWidth;
        }

        if (point.Y > (ExplorerGrid.Items.Count * ExplorerGrid.MinRowHeight + ColumnHeaderHeight)
            || point.Y > (ExplorerGrid.ActualHeight - StyleHelper.GetChildItemsPresenter(ExplorerGrid).ActualHeight % ExplorerGrid.MinRowHeight)
            || point.Y < ColumnHeaderHeight + ScrollContentPresenterMargin
            || point.X > actualRowWidth
            || point.X > DataGridContentWidth)
        {
            if (ExplorerGrid.SelectedItems.Count > 0 && IsInEditMode)
                IsInEditMode = false;

            ExplorerGrid.UnselectAll();

            selectionIndex = -1;
        }
        else
            selectionIndex = ExplorerGrid.SelectedIndex;

        SelectionHelper.SetCurrentSelectedIndex(ExplorerGrid, selectionIndex);
        SelectionHelper.SetFirstSelectedIndex(ExplorerGrid, selectionIndex);
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        NavigationBox.Refresh();
        ResizeDetailedView();
        EnableSplitViewAnimation();
        SearchBoxMaxWidth();

        Size maximizedSize = new(SystemParameters.MaximizedPrimaryScreenWidth, SystemParameters.MaximizedPrimaryScreenHeight);
        if (e.NewSize != maximizedSize && e.PreviousSize != maximizedSize)
            FileActions.IsEditorOpen = false;
    }

    private void SearchBoxMaxWidth()
    {
        RuntimeSettings.MaxSearchBoxWidth = WindowState switch
        {
            WindowState.Maximized => SystemParameters.PrimaryScreenWidth * MAX_SEARCH_WIDTH_RATIO,
            _ => Width * MAX_SEARCH_WIDTH_RATIO,
        };

        RuntimeSettings.AutoHideSearchBox = true;
        SearchBox.Refresh();
    }

    private void EnableSplitViewAnimation()
    {
        // Read value to force IsAnimated to update
        _ = Settings.DisableAnimation;

        bool enableAnimation = Settings.IsAnimated && (MonitorInfo.IsPrimaryMonitor(this) is bool and true || WindowState is not WindowState.Maximized);
        StyleHelper.SetActivateAnimation(SettingsSplitView, enableAnimation);
        StyleHelper.SetActivateAnimation(DevicesSplitView, enableAnimation);
    }

    private void ResizeDetailedView()
    {
        double windowHeight = WindowState == WindowState.Maximized ? ActualHeight : Height;

        if (DetailedViewSize() is sbyte val && val == -1)
        {
            FileOpDetailedGrid.Height = windowHeight * MIN_PANE_HEIGHT_RATIO;
        }
        else if (val == 1)
        {
            FileOpDetailedGrid.Height = windowHeight * MAX_PANE_HEIGHT_RATIO;
        }
    }

    private void FilterDrives()
    {
        var collectionView = CollectionViewSource.GetDefaultView(DriveList.ItemsSource);
        if (collectionView is null)
            return;

        if (collectionView.Filter is not null)
        {
            collectionView.Refresh();
            return;
        }

        Predicate<object> predicate = d =>
        {
            var drive = (DriveViewModel)d;

            return drive.Type switch
            {
                AbstractDrive.DriveType.Trash => Settings.EnableRecycle,
                AbstractDrive.DriveType.Temp or AbstractDrive.DriveType.Package => Settings.EnableApk,
                _ => true,
            };
        };

        collectionView.Filter = new(predicate);
        collectionView.SortDescriptions.Clear();
        collectionView.SortDescriptions.Add(new SortDescription(nameof(DriveViewModel.Type), ListSortDirection.Ascending));
    }

    private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        ScrollViewer scroller = sender switch
        {
            ScrollViewer sv => sv,
            DataGrid dg => StyleHelper.GetChildScrollViewer(dg),
            _ => throw new NotSupportedException(),
        };

        scroller.ScrollToVerticalOffset(scroller.VerticalOffset - e.Delta);
        e.Handled = true;
    }

    private void DataGridCell_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        if (e.OriginalSource is DataGridCell && e.TargetRect == Rect.Empty)
        {
            e.Handled = true;
        }
    }

    private void PaneCollapse_Click(object sender, RoutedEventArgs e)
    {
        RuntimeSettings.IsSettingsPaneOpen = false;
    }

    private void GridBackgroundBlock_MouseDown(object sender, MouseButtonEventArgs e)
    {
        RuntimeSettings.IsPathBoxFocused = false;
        DriveHelper.ClearSelectedDrives();
    }

    private void FilterExplorerItems()
    {
        //https://docs.microsoft.com/en-us/dotnet/desktop/wpf/controls/how-to-group-sort-and-filter-data-in-the-datagrid-control?view=netframeworkdesktop-4.8

        if (!FileActions.IsExplorerVisible)
            return;

        var collectionView = CollectionViewSource.GetDefaultView(ExplorerGrid.ItemsSource);
        if (collectionView is null)
            return;

        if (FileActions.IsAppDrive)
        {
            collectionView.Filter = Settings.ShowSystemPackages
                ? new(FileHelper.PkgFilter())
                : new(pkg => ((Package)pkg).Type is Package.PackageType.User);

            ExplorerGrid.Columns[8].SortDirection = ListSortDirection.Descending;
            collectionView.SortDescriptions.Clear();
            collectionView.SortDescriptions.Add(new(nameof(Package.Type), ListSortDirection.Descending));
        }
        else
        {
            collectionView.Filter = !Settings.ShowHiddenItems
            ? (new(FileHelper.HideFiles()))
            : (new(file => !FileHelper.IsHiddenRecycleItem((FileClass)file)));

            ExplorerGrid.Columns[1].SortDirection = ListSortDirection.Ascending;
            collectionView.SortDescriptions.Clear();
            collectionView.SortDescriptions.Add(new(nameof(FileClass.IsTemp), ListSortDirection.Descending));
            collectionView.SortDescriptions.Add(new(nameof(FileClass.IsDirectory), ListSortDirection.Descending));
            collectionView.SortDescriptions.Add(new(nameof(FileClass.SortName), ListSortDirection.Ascending));
        }
    }

    private void GridSplitter_DragDelta(object sender, DragDeltaEventArgs e)
    {
        // -1 + 1 or 1 + -1 (0 + 0 shouldn't happen)
        if (SimplifyNumber(e.VerticalChange) + DetailedViewSize() == 0)
            return;

        if (FileOpDetailedGrid.Height is double.NaN)
            FileOpDetailedGrid.Height = FileOpDetailedGrid.ActualHeight;

        FileOpDetailedGrid.Height -= e.VerticalChange;

        sbyte SimplifyNumber(double num) => num switch
        {
            < 0 => -1,
            > 0 => 1,
            _ => 0
        };
    }

    /// <summary>
    /// Compares the size of the detailed file op view to its limits
    /// </summary>
    /// <returns>0 if within limits, 1 if exceeds upper limits, -1 if exceeds lower limits</returns>
    private sbyte DetailedViewSize()
    {
        double height = FileOpDetailedGrid.ActualHeight;
        if (height == 0 && FileOpDetailedGrid.Height > 0)
            height = FileOpDetailedGrid.Height;

        if (height > ActualHeight * MAX_PANE_HEIGHT_RATIO)
            return 1;

        if (ActualHeight == 0 || height < ActualHeight * MIN_PANE_HEIGHT_RATIO && height < MIN_PANE_HEIGHT)
            return -1;

        return 0;
    }

    private void FileOpDetailedGrid_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        ResizeDetailedView();
    }

    private void UpdateMdns()
    {
        if (MdnsService.State == MDNS.MdnsState.Disabled)
        {
            MdnsService.State = MDNS.MdnsState.InProgress;
            AdbHelper.MdnsCheck();
        }
        else
        {
            MdnsService.State = MDNS.MdnsState.Disabled;
        }

        if (RuntimeSettings.IsMdnsExpanderOpen)
        {
            UpdateQrClass();
        }
    }

    private void UpdateQrClass() => PairingQrImage.Source = QrClass.Image;

    private void Window_SourceInitialized(object sender, EventArgs e)
    {
        if (Storage.RetrieveBool(AppSettings.SystemVals.windowMaximized) == true)
            WindowState = WindowState.Maximized;

        if (Storage.RetrieveBool(AppSettings.SystemVals.detailedVisible) is bool and true)
        {
            RuntimeSettings.IsOperationsViewOpen = true;
        }

        if (double.TryParse(Storage.RetrieveValue(AppSettings.SystemVals.detailedHeight)?.ToString(), out double detailedHeight))
        {
            FileOpDetailedGrid.Height = detailedHeight;
            ResizeDetailedView();
        }
    }

    private void RestartAdbButton_Click(object sender, RoutedEventArgs e)
    {
        ADBService.KillAdbServer();
        MdnsService.State = MDNS.MdnsState.Disabled;
        UpdateMdns();
    }

    private void Border_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (((MenuItem)(FindResource("DeviceActionsMenu") as Menu).Items[0]).IsSubmenuOpen)
            return;

        DeviceHelper.CollapseDevices();
    }

    private void CurrentOperationDetailedDataGrid_ColumnDisplayIndexChanged(object sender, DataGridColumnEventArgs e)
    {
        if (e.Column is not DataGridColumn column)
            return;

        var col = FileOpColumns.List.Find(col => col.Type == GetColumnType(column));
        if (col?.Column is null)
            return;

        col.Index = column.DisplayIndex;
    }

    private void DataGridColumnHeader_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (((DataGridColumnHeader)sender).Column is not DataGridColumn column)
            return;

        var col = FileOpColumns.List.Find(col => col.Type == GetColumnType(column));
        if (col?.Column is null)
            return;

        col.Width = column.ActualWidth;
    }

    private void NameColumnEdit_Loaded(object sender, RoutedEventArgs e)
    {
        var textBox = sender as TextBox;
        TextHelper.SetAltObject(textBox, FileHelper.GetFromCell(ExplorerGrid.SelectedCells[1]));
        textBox.Focus();
    }

    private void NameColumnEdit_LostFocus(object sender, RoutedEventArgs e)
    {
        FileActionLogic.Rename(sender as TextBox);
    }

    private void NameColumnEdit_KeyDown(object sender, KeyEventArgs e)
    {
        var cell = CellConverter.GetDataGridCell(ExplorerGrid.SelectedCells[1]);
        var textBox = sender as TextBox;

        if (e.Key is Key.Escape or Key.F2)
        {
            var name = FileHelper.DisplayName(textBox);
            if (string.IsNullOrEmpty(name))
            {
                DirList.FileList.Remove(ExplorerGrid.SelectedItem as FileClass);
            }
            else
            {
                textBox.Text = FileHelper.DisplayName(sender as TextBox);
            }

            AppActions.List.First(action => action.Name is FileAction.FileActionType.Rename).Command.Execute();
        }
        else if (e.Key is not Key.Enter)
            return;

        e.Handled = true;

        if (ExplorerGrid.SelectedCells.Count > 0)
        {
            cell.IsEditing = false;
            FileActions.IsExplorerEditing = false;
        }
    }

    private void NameColumnCell_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;

        var cell = sender as DataGridCell;
        if (cell.IsEditing)
            return;

        MouseDownPoint = e.GetPosition(ExplorerGrid);
        e.Handled = true;
        clickCount = e.ClickCount;

        if (clickCount > 1)
        {
            DoubleClick(cell.DataContext);
        }
        else
        {
            RuntimeSettings.IsPathBoxFocused = false;   
            var row = DataGridRow.GetRowContainingElement(cell);
            var current = row.GetIndex();

            if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
            {
                ExplorerGrid.SelectedItems.Clear();

                int firstUnselected = firstSelectedRow, lastUnselected = current + 1;
                if (current < firstSelectedRow)
                {
                    firstUnselected = current;
                    lastUnselected = firstSelectedRow + 1;
                }

                for (int i = firstUnselected; i < lastUnselected; i++)
                {
                    ExplorerGrid.SelectedItems.Add(ExplorerGrid.Items[i]);
                }

                return;
            }

            if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
            {
                row.IsSelected = !row.IsSelected;
                return;
            }

            firstSelectedRow = row.GetIndex();

            if (!row.IsSelected)
            {
                ExplorerGrid.SelectedItems.Clear();
                row.IsSelected = true;
            }
            else
            {
                if (SelectedFiles is null || SelectedFiles.Count() > 1)
                {
                    ExplorerGrid.SelectedItems.Clear();
                    row.IsSelected = true;

                    return;
                }

                if (cell.IsReadOnly || (DevicesObject.Current.Root is not AbstractDevice.RootStatus.Enabled
                    && ((FileClass)cell.DataContext).Type is not (FileType.File or FileType.Folder)))
                    return;

                Task.Delay(DOUBLE_CLICK_TIMEOUT).ContinueWith(t =>
                {
                    if (clickCount != 1)
                        return;

                    Dispatcher.Invoke(() =>
                    {
                        if (e.LeftButton == MouseButtonState.Released && SelectedFiles.Count() == 1)
                        {
                            cell.IsEditing = true;
                            FileActions.IsExplorerEditing = true;
                        }
                    });
                });
            }
        }
    }

    private void NameColumnCell_MouseEnter(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DataGridRow.GetRowContainingElement(sender as DataGridCell).IsSelected = true;
    }

    private void NameColumnCell_MouseLeave(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            var cell = sender as DataGridCell;
            var row = DataGridRow.GetRowContainingElement(cell);
            var index = row.GetIndex();
            var pos = e.GetPosition(cell).Y;

            if (index == firstSelectedRow)
                return;

            if ((index > firstSelectedRow && pos < 0) || (index < firstSelectedRow && pos > 0))
                row.IsSelected = false;
        }
    }

    private void NameColumnEdit_TextChanged(object sender, TextChangedEventArgs e)
    {
        (sender as TextBox).FilterString(INVALID_ANDROID_CHARS);
    }

    private void NewItem(bool isFolder)
    {
        var namePrefix = S_NEW_ITEM(isFolder);
        var index = FileClass.ExistingIndexes(DirList.FileList, namePrefix);

        var fileName = $"{namePrefix}{index}";
        FileClass newItem = new(fileName, FileHelper.ConcatPaths(CurrentPath, fileName), isFolder ? FileType.Folder : FileType.File, isTemp: true);
        DirList.FileList.Insert(0, newItem);

        ExplorerGrid.ScrollIntoView(newItem);
        ExplorerGrid.SelectedItem = newItem;
        
        IsInEditMode = true;
        if (!IsInEditMode) // in case cell was not acquired
            FileActionLogic.CreateNewItem(newItem);
    }

    private void ExplorerGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Delete)
        {
            e.Handled = true;
            if (FileActions.DeleteEnabled)
                FileActionLogic.DeleteFiles();
        }
    }

    private void ClearLogs()
    {
        CommandLog.Clear();
        LogTextBox.Clear();
    }

    private void RefreshDevicesButton_Click(object sender, RoutedEventArgs e)
        => Task.Run(RefreshDevices);

    private void AndroidRobotLicense_Click(object sender, RoutedEventArgs e)
        => SettingsHelper.ShowAndroidRobotLicense();

    private void ExplorerGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        if (FileActions.IsAppDrive)
            return;

        var collectionView = CollectionViewSource.GetDefaultView(ExplorerGrid.ItemsSource);
        var sortDirection = ListHelper.Invert(e.Column.SortDirection);
        e.Column.SortDirection = sortDirection;

        collectionView.SortDescriptions.Clear();
        collectionView.SortDescriptions.Add(new(nameof(FileClass.IsTemp), ListSortDirection.Descending));
        collectionView.SortDescriptions.Add(new(nameof(FileClass.IsDirectory), ListHelper.Invert(sortDirection)));

        if (e.Column.SortMemberPath != nameof(FileClass.FullName))
            collectionView.SortDescriptions.Add(new(e.Column.SortMemberPath, sortDirection));

        collectionView.SortDescriptions.Add(new(nameof(FileClass.SortName), sortDirection));

        e.Handled = true;
    }

    private void ExplorerGrid_ContextMenuClosing(object sender, ContextMenuEventArgs e)
    {
        SelectionHelper.SetIsMenuOpen(ExplorerGrid.ContextMenu, false);
    }

    private void FilterSettings()
    {
        var collectionView = CollectionViewSource.GetDefaultView(SortedSettings.ItemsSource);
        if (collectionView is null)
            return;

        if (string.IsNullOrEmpty(RuntimeSettings.SearchText))
            collectionView.Filter = null;
        else
        {
            collectionView.Filter = new(sett => ((AbstractSetting)sett).Description.Contains(RuntimeSettings.SearchText, StringComparison.OrdinalIgnoreCase)
            || (sett is EnumSetting enumSett && enumSett.Buttons.Any(button => button.Name.Contains(RuntimeSettings.SearchText, StringComparison.OrdinalIgnoreCase))));
        }
    }

    private void MainWin_StateChanged(object sender, EventArgs e)
    {
        SearchBoxMaxWidth();
    }

    private void SortedSettings_MouseMove(object sender, MouseEventArgs e)
    {
        SortedSettings.Focus();
    }

    private void MdnsCheckBox_Click(object sender, RoutedEventArgs e)
    {
        UpdateMdns();
    }

    private void NavigationBox_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        NavigationBox.Refresh();
    }

    private void ExplorerGrid_MouseMove(object sender, MouseEventArgs e)
    {
        var point = e.GetPosition(ExplorerCanvas);
        if (e.LeftButton == MouseButtonState.Released || !RuntimeSettings.IsExplorerLoaded)
        {
            SelectionRect.Visibility = Visibility.Collapsed;
            return;
        }

        SelectionRect.Visibility = Visibility.Visible;
        if (point.Y > MouseDownPoint.Y)
        {
            Canvas.SetTop(SelectionRect, MouseDownPoint.Y);
        }
        else
        {
            Canvas.SetTop(SelectionRect, point.Y);
        }
        if (point.X > MouseDownPoint.X)
        {
            Canvas.SetLeft(SelectionRect, MouseDownPoint.X);
        }
        else
        {
            Canvas.SetLeft(SelectionRect, point.X);
        }

        SelectionRect.Height = Math.Abs(MouseDownPoint.Y - point.Y);
        SelectionRect.Width = Math.Abs(MouseDownPoint.X - point.X);

        SelectRows();
    }

    private void SelectRows()
    {
        Rect selection = new(Canvas.GetLeft(SelectionRect),
                             Canvas.GetTop(SelectionRect),
                             SelectionRect.Width,
                             SelectionRect.Height);

        for (int i = 0; i < ExplorerGrid.ItemContainerGenerator.Items.Count; i++)
        {
            if (ExplorerGrid.ItemContainerGenerator.ContainerFromIndex(i) is not DataGridRow row)
                continue;

            Rect rowRect = new(row.TranslatePoint(new(), ExplorerGrid), row.DesiredSize);
            row.IsSelected = rowRect.IntersectsWith(selection);
        }
    }

    private void DataGridCell_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        MouseDownPoint = e.GetPosition(ExplorerGrid);
    }

    private void ExplorerCanvas_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        SelectionRect.Visibility = Visibility.Collapsed;
    }

    private void CurrentOperationDetailedDataGrid_KeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Escape)
        {
            ((DataGrid)sender).UnselectAll();
        }
    }

    private void CurrentOperationDetailedDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSelectedFileOp();
    }

    private void UpdateSelectedFileOp()
    {
        var selectedOps = CurrentOperationDetailedDataGrid.SelectedItems.OfType<FileOperation>();
        if (!selectedOps.Any()
            && !CurrentOperationDetailedDataGrid.Items.IsEmpty
            && CurrentOperationDetailedDataGrid.Items[0] is FileOperation op)
        {
            selectedOps = new[] { op };
        }

        FileActions.SelectedFileOps.Value = selectedOps;
    }
}
