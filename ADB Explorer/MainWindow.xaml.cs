using ADB_Explorer.Controls;
using ADB_Explorer.Converters;
using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Resources;
using ADB_Explorer.Services;
using ADB_Explorer.Services.AppInfra;
using ADB_Explorer.ViewModels;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Xml;
using static ADB_Explorer.Helpers.VisibilityHelper;
using static ADB_Explorer.Models.AbstractFile;
using static ADB_Explorer.Models.AdbExplorerConst;
using static ADB_Explorer.Models.Data;
using static ADB_Explorer.Services.FileAction;

namespace ADB_Explorer;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly DispatcherTimer ServerWatchdogTimer = new() { Interval = RESPONSE_TIMER_INTERVAL };
    private readonly DispatcherTimer ConnectTimer = new() { Interval = CONNECT_TIMER_INIT };
    private readonly DispatcherTimer SelectionTimer = new() { Interval = SELECTION_CHANGED_DELAY };
    private readonly DispatcherTimer DiskUsageTimer = new() { Interval = DISK_USAGE_INTERVAL_ACTIVE };

    private readonly Mutex DiskUsageMutex = new();
    private readonly Mutex ConnectTimerMutex = new();
    private readonly ThemeService ThemeService = new();

    private static Point NullPoint => new(-1, -1);

    private double? RowHeight { get; set; }
    private double ColumnHeaderHeight => (double)FindResource("DataGridColumnHeaderHeight") + ScrollContentPresenterMargin;
    private double ScrollContentPresenterMargin => RuntimeSettings.UseFluentStyles ? ((Thickness)FindResource("DataGridScrollContentPresenterMargin")).Top : 0;
    private double DataGridContentWidth
        => StyleHelper.FindDescendant<ItemsPresenter>(ExplorerGrid) is ItemsPresenter presenter ? presenter.ActualWidth : 0;

    public string SelectedFilesTotalSize => (SelectedFiles is not null && FileHelper.TotalSize(SelectedFiles) is ulong size and > 0) ? size.ToSize() : "";
    public string SelectedFilesCount => $"{ExplorerGrid.SelectedItems.Count}";

    private string prevPath = "";

    /// <summary>
    /// Back / Forward Navigation
    /// </summary>
    private bool bfNavigation;

    private int ClickCount = 0;
    private bool WasSelected;
    private bool WasEditing;
    private bool WasDragging;
    private Point MouseDownPoint;
    private FileToIconConverter FileToIcon;
    private DateTime appDataClick;
    private readonly DragWindow dw = new();

    private bool IsInEditMode
    {
        get
        {
            if (FileActions.IsAppDrive || !ExplorerGrid.SelectedCells.Any())
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

    public event PropertyChangedEventHandler PropertyChanged;
    protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public MainWindow()
    {
        InitializeComponent();

        KeyDown += new KeyEventHandler(OnButtonKeyDown);
        PreviewTextInput += new TextCompositionEventHandler(MainWindow_PreviewTextInput);

        DevicesObject = new();
        DevicesList.ItemsSource = DevicesObject.UIList;

        FileOpQ = new();
        Task launchTask = Task.Run(LaunchSequence);

        ConnectTimer.Tick += ConnectTimer_Tick;
        ServerWatchdogTimer.Tick += ServerWatchdogTimer_Tick;
        DiskUsageTimer.Tick += DiskUsageTimer_Tick;
        SelectionTimer.Tick += SelectionTimer_Tick;

        Settings.PropertyChanged += Settings_PropertyChanged;
        RuntimeSettings.PropertyChanged += RuntimeSettings_PropertyChanged;
        ThemeService.PropertyChanged += ThemeService_PropertyChanged;
        CommandLog.CollectionChanged += CommandLog_CollectionChanged;
        FileOpQ.PropertyChanged += FileOperationQueue_PropertyChanged;
        FileActions.PropertyChanged += FileActions_PropertyChanged;
        DevicesObject.PropertyChanged += DevicesObject_PropertyChanged;
        DevicesObject.UIList.CollectionChanged += UIList_CollectionChanged;
        FileOpFilters.CheckedFilterCount.PropertyChanged += CheckedFilterCount_PropertyChanged;

        AppActions.Bindings.ForEach(binding =>
        {
            InputBindings.Add(binding);
            ExplorerGrid.InputBindings.Add(binding);
        });

        UpperProgressBar.DataContext = FileOpQ;
        CurrentOperationDetailedDataGrid.ItemsSource = FileOpQ.Operations;
        ((DataGrid)FindResource("CurrentOperationDataGrid")).ItemsSource = FileOpQ.Operations;
        UpdateFileOp();

        NativeMethods.InterceptClipboard.Init(this, CopyPaste.GetClipboardPasteItems, IpcService.AcceptIpcMessage);

#if DEBUG
        DeviceHelper.TestDevices();
#endif

        Task.Run(() =>
        {
            SetTheme(AppSettings.AppTheme.light);
            SettingsHelper.SplashScreenTask();

            launchTask.Wait();
            RuntimeSettings.IsWindowLoaded = true;

            Dispatcher.Invoke(() =>
            {
                dw.Show();

                App.Current.MainWindow = this;
            });
        });
    }

    private void FinalizeSplash()
    {
        SetTheme();

        RuntimeSettings.IsSplashScreenVisible = false;
        RuntimeSettings.IsDevicesPaneOpen = true;

        AdbHelper.VerifyProgressRedirection();

        ConnectTimer.Start();
        ServerWatchdogTimer.Start();
        DiskUsageTimer.Start();

        SettingsHelper.CheckAppVersions();
    }

    private void DiskUsageTimer_Tick(object sender, EventArgs e)
    {
        DiskUsageMutex.WaitOne(0);
        Task.Run(DiskUsageHelper.GetAdbDiskUsage).ContinueWith(
            (t) => Dispatcher.Invoke(DiskUsageMutex.ReleaseMutex));

        DiskUsageTimer.Interval = FileOpQ.IsActive
            ? DISK_USAGE_INTERVAL_ACTIVE
            : DISK_USAGE_INTERVAL_IDLE;
    }

    private void MainWindow_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (RuntimeSettings.IsDevicesPaneOpen
            || RuntimeSettings.IsSettingsPaneOpen
            || SearchBox.IsFocused
            || NavigationBox.Mode is NavigationBox.ViewMode.Path
            || FileActions.IsExplorerEditing)
            return;

        var selected = ExplorerGrid.SelectedItems.Count;
        var selectedIndex = ExplorerGrid.SelectedIndex;
        object altItem = null;

        for (int i = 0; i < ExplorerGrid.Items.Count; i++)
        {
            var item = ExplorerGrid.Items[i];
            var name = item.ToString();

            if (name.StartsWith(e.Text, StringComparison.OrdinalIgnoreCase))
            {
                if (selected != 1 || selectedIndex < i)
                {
                    FileActions.ItemToSelect = item;
                    break;
                }
                else if (altItem is null)
                    altItem = item;
            }
        }

        if (selectedIndex == ExplorerGrid.SelectedIndex && altItem is not null)
            FileActions.ItemToSelect = altItem;
    }

    private void CheckedFilterCount_PropertyChanged(object sender, PropertyChangedEventArgs<int> e)
    {
        UpdateFileOpFilterCheck();

        SortFileOps();
    }

    private static void UpdateFileOpFilterCheck()
    {
        AppActions.ToggleActions.Find(a => a.FileAction.Name is FileActionType.FileOpFilter).Button.IsChecked
            = FileOpFilters.CheckedFilterCount + 1 < FileOpFilters.List.Count;
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
                    {
                        bfNavigation = true;
                        NavigateToLocation(NavHistory.GoBack());
                    }
                    else
                    {
                        if (FileActions.IsExplorerVisible)
                            NavigateToLocation(RuntimeSettings.PathBoxNavigation);
                        else
                        {
                            if (!InitNavigation(RuntimeSettings.PathBoxNavigation))
                            {
                                DriveViewNav();
                            }
                        }
                    }
                    break;

                case nameof(AppRuntimeSettings.LocationToNavigate):
                    switch (RuntimeSettings.LocationToNavigate)
                    {
                        case NavHistory.SpecialLocation.Back:
                            bfNavigation = true;
                            NavigateToLocation(NavHistory.GoBack());
                            break;
                        case NavHistory.SpecialLocation.Forward:
                            bfNavigation = true;
                            NavigateToLocation(NavHistory.GoForward());
                            break;
                        case NavHistory.SpecialLocation.Up:
                            bfNavigation = false;
                            NavigateToPath(ParentPath);
                            break;
                        case not null:
                            bfNavigation = false;
                            if (FileActions.IsDriveViewVisible && NavHistory.LocationFromString(RuntimeSettings.LocationToNavigate) is NavHistory.SpecialLocation.DriveView)
                                FileActionLogic.RefreshDrives(true);
                            else
                                NavigateToLocation(RuntimeSettings.LocationToNavigate);
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
                    IsPathBoxFocused(RuntimeSettings.IsPathBoxFocused
                                     ?? NavigationBox.Mode
                                     is NavigationBox.ViewMode.Breadcrumbs);

                    RuntimeSettings.AutoHideSearchBox = true;
                    break;

                case nameof(AppRuntimeSettings.IsSearchBoxFocused):
                    if (!RuntimeSettings.IsSearchBoxFocused)
                        SettingsSplitView.Focus();
                    break;

                case nameof(AppRuntimeSettings.AutoHideSearchBox):
                    if (Width < MAX_WINDOW_WIDTH_FOR_SEARCH_AUTO_COLLAPSE)
                        RuntimeSettings.IsSearchBoxFocused = false;

                    if (NavigationBox.Mode is not NavigationBox.ViewMode.Path)
                        SettingsSplitView.Focus();
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
                    Task.Run(() =>
                    {
                        if (FileActions.IsAppDrive || FileActions.IsRecycleBin || DevicesObject.Current is null)
                            FilterFileActions();
                    });
                    Task.Run(() =>
                    {
                        FilterExplorerContextMenu();
                        ExplorerContextMenu.UpdateSeparators();
                    });
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

                case nameof(AppRuntimeSettings.ClearLogs):
                    ClearLogs();
                    break;

                case nameof(AppRuntimeSettings.RefreshSettingsControls):
                    SettingsControlsMenu.Items.Refresh();
                    break;

                case nameof(AppRuntimeSettings.SortFileOps):
                    SortFileOps();
                    break;

                case nameof(AppRuntimeSettings.RefreshExplorerSorting):
                    FilterExplorerItems(true);
                    break;

                case nameof(AppRuntimeSettings.FinalizeSplash):
                    FinalizeSplash();
                    break;

                case nameof(AppRuntimeSettings.MainCursor):
                    Cursor = RuntimeSettings.MainCursor;
                    break;

                case nameof(AppRuntimeSettings.RefreshBreadcrumbs):
                    NavigationBox.Refresh();
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
        switch (e.PropertyName)
        {
            case nameof(FileActionsEnable.RefreshPackages) when FileActions.RefreshPackages:
                if (FileActions.IsAppDrive)
                {
                    Dispatcher.Invoke(() => _navigateToPath(CurrentPath));
                    FileActions.RefreshPackages = false;
                }
                break;

            case nameof(FileActionsEnable.ExplorerFilter):
                FilterExplorerItems();
                break;

            case nameof(FileActionsEnable.ItemToSelect):
                if (FileActions.ItemToSelect is null)
                    ExplorerGrid.UnselectAll();
                else
                {
                    ExplorerGrid.ScrollIntoView(FileActions.ItemToSelect);
                    ExplorerGrid.SelectedItem = FileActions.ItemToSelect;
                }
                break;

            case nameof(FileActionsEnable.PasteEnabled):
                FilterFileActions();
                FilterExplorerContextMenu();
                break;
        }
    }

    private void FileOperationQueue_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (FileOperationQueue.NotifyProperties.Contains(e.PropertyName))
        {
            UpdateFileOp();

            if (e.PropertyName == nameof(FileOperationQueue.IsActive))
            {
                CurrentOperationDetailedDataGrid.UnselectAll();

                RuntimeSettings.RefreshFileOpControls = true;
            }
        }

        if (e.PropertyName is nameof(FileOperationQueue.CurrentChanged))
        {
            SortFileOps();
            Task.Run(FileActionLogic.UpdateFileOpControls);
        }

        if (!FileOpQ.IsActive)
            UpdateSelectedFileOp();
    }

    private void SortFileOps()
    {
        var collectionView = CollectionViewSource.GetDefaultView(CurrentOperationDetailedDataGrid.ItemsSource);
        if (collectionView is null)
            return;

        if (collectionView.Filter is not null)
        {
            collectionView.Refresh();
            return;
        }

        Predicate<object> predicate = op =>
        {
            var fileOp = (FileOperation)op;

            if (FileOpQ.IsActive)
                return fileOp.Filter is FileOpFilter.FilterType.Running;

            return FileOpFilters.List.Find(f => f.Type == fileOp.Filter).IsChecked is true;
        };

        collectionView.Filter = predicate;

        if (collectionView.SortDescriptions.All(d => d.PropertyName != nameof(FileOperation.Filter)))
            collectionView.SortDescriptions.Add(new(nameof(FileOperation.Filter), ListSortDirection.Ascending));
    }

    private void UpdateFileOp(bool onlyProgress = true)
    {
        if (!onlyProgress)
            Task.Run(FileActionLogic.UpdateFileOpControls);

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
                RowHeight = null;
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
                    FileActionLogic.RefreshDrives(true);

                FilterDrives();

                break;
            case nameof(AppSettings.SaveDevices):
                if (Settings.SaveDevices && !DevicesObject.HistoryDeviceViewModels.Any())
                    DevicesObject.RetrieveHistoryDevices();

                FilterDevices();
                break;

            case nameof(AppSettings.UseProgressRedirection):
                AdbHelper.VerifyProgressRedirection();
                break;
        }
    }

    private void DirectoryLister_PropertyChanged(object sender, PropertyChangedEventArgs e) => Dispatcher.Invoke(() =>
    {
        switch (e.PropertyName)
        {
            case nameof(DirectoryLister.IsProgressVisible):
                UnfinishedBlock.Visible(DirList.IsProgressVisible);
                NavigationBox.IsLoadingProgressVisible = DirList.IsProgressVisible;
                break;

            case nameof(DirectoryLister.InProgress):
            {
                Task.Run(() =>
                {
                    if (!DirList.InProgress)
                        Task.Delay(EMPTY_FOLDER_NOTICE_DELAY);

                    Dispatcher.Invoke(() => FileActions.ListingInProgress = DirList.InProgress);
                });

                if (DirList.InProgress)
                    return;

                if (FileActions.IsRecycleBin)
                {
                    TrashHelper.EnableRecycleButtons();
                }

                break;
            }
            case nameof(DirectoryLister.IsLinkListingFinished) when ExplorerGrid.Items.Count < 1 || !DirList.IsLinkListingFinished:
                return;

            case nameof(DirectoryLister.IsLinkListingFinished) when bfNavigation
                && !string.IsNullOrEmpty(prevPath) && DirList.FileList.FirstOrDefault(item => item.FullPath == prevPath) is var prevItem and not null:
                FileActions.ItemToSelect = prevItem;
                break;

            case nameof(DirectoryLister.IsLinkListingFinished):
            {
                if (ExplorerGrid.Items.Count > 0)
                    ExplorerGrid.ScrollIntoView(ExplorerGrid.Items[0]);
                break;
            }
        }
    });

    private void ThemeService_PropertyChanged(object sender, PropertyChangedEventArgs e) =>
        Dispatcher.Invoke(SetTheme);

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        DirList?.Stop();

        dw.Close();
        NativeMethods.InterceptClipboard.Close();

        ConnectTimer.Stop();
        ServerWatchdogTimer.Stop();
        DiskUsageTimer.Stop();
        StoreClosingValues();
    }

    private void StoreClosingValues()
    {
        Storage.StoreValue(AppSettings.SystemVals.windowMaximized, WindowState == WindowState.Maximized);

        var detailedVisible = RuntimeSettings.IsOperationsViewOpen && Settings.EnableCompactView;
        Storage.StoreValue(AppSettings.SystemVals.detailedVisible, detailedVisible);
        if (detailedVisible)
            Storage.StoreValue(AppSettings.SystemVals.detailedHeight, FileOpDetailedGrid.Height);
    }

    private void SetTheme() => SetTheme(Settings.Theme);

    private void SetTheme(AppSettings.AppTheme theme) => ThemeService.SetTheme(theme);

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

            NavigationBox.UnfocusTarget = SettingsSplitView;
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
        if (source is not FileClass file || FileActions.IsRecycleBin)
            return;

        if (file.Type is FileType.Folder)
        {
            bfNavigation = false;
            NavigateToPath(file);

            return;
        }
        else if (file.Type is not FileType.File)
            return;

        if (Settings.DoubleClick is AppSettings.DoubleClickAction.pull
            && Settings.IsPullOnDoubleClickEnabled
            && FileActions.PullEnabled)
        {
            FileActionLogic.PullFiles(Settings.DefaultFolder);
        }
        else if (Settings.DoubleClick is AppSettings.DoubleClickAction.edit)
            FileActionLogic.OpenEditor();
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
                SelectionHelper.SetCurrentSelectedIndex(ExplorerGrid, ExplorerGrid.SelectedIndex);
                if (SelectionHelper.GetFirstSelectedIndex(ExplorerGrid) < 0
                    || Keyboard.Modifiers is not ModifierKeys.Control and not ModifierKeys.Shift)
                {
                    SelectionHelper.SetFirstSelectedIndex(ExplorerGrid, ExplorerGrid.SelectedIndex);
                }
            }
            else if (ExplorerGrid.SelectedItems.Count > 1 && e.AddedItems.Count == 1)
            {
                SelectionHelper.SetCurrentSelectedIndex(ExplorerGrid, ExplorerGrid.Items.IndexOf(e.AddedItems[0]));
            }
        }

        SelectionTimer.Stop();
        SelectionTimer.Start();
    }

    private void SelectionTimer_Tick(object sender, EventArgs e)
    {
        SelectedFiles = FileActions.IsAppDrive ? [] : ExplorerGrid.SelectedItems.OfType<FileClass>();
        SelectedPackages = FileActions.IsAppDrive ? ExplorerGrid.SelectedItems.OfType<Package>() : [];
        OnPropertyChanged(nameof(SelectedFilesTotalSize));
        OnPropertyChanged(nameof(SelectedFilesCount));
        FileActions.SelectedItemsCount = FileActions.IsAppDrive ? SelectedPackages.Count() : SelectedFiles.Count();

        FileActionLogic.UpdateFileActions();
        MainToolBar.Items?.Refresh();
        PasteGrid.Visibility = Visibility.Visible;

        SelectionTimer.Stop();
    }

    /// <summary>
    /// Refresh file actions menu to update their ToolTips
    /// </summary>
    private void FilterFileActions() => Dispatcher.Invoke(() => MainToolBar.Items?.Refresh());

    private void FilterExplorerContextMenu() => Dispatcher.Invoke(() =>
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

        collectionView.Filter = predicate;
    });

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

        UpdateFileOpFilterCheck();

        FileToIcon = new();
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

        Settings.RootArgs ??= ["root"];
        Settings.UnrootArgs ??= ["unroot"];
        Settings.UnrootOnDisconnect ??= false;

        RuntimeSettings.DefaultBrowserPath = Network.GetDefaultBrowser();
    }

    private void SetRenderMode() => Dispatcher.Invoke(() =>
    {
        if (Settings.SwRender)
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        else if (RenderOptions.ProcessRenderMode == RenderMode.SoftwareOnly)
            RenderOptions.ProcessRenderMode = RenderMode.Default;
    });

    private void InitFileOpColumns() => Dispatcher.Invoke(() =>
    {
        FileOpColumns.Init();

        FileOpColumns.List.ForEach(c => CurrentOperationDetailedDataGrid.Columns.Add(c.Column));
    });

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
        NavHistory.Navigate(NavHistory.SpecialLocation.DriveView);

        DriveList.ItemsSource = DevicesObject.Current.Drives;
        CurrentDrive = null;

        if (DriveList.SelectedIndex > -1)
            SelectionHelper.GetListViewItemContainer(DriveList).Focus();
    }

    private bool InitNavigation(string path = "")
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

        Task.Delay(EXPLORER_NAV_DELAY).ContinueWith(_ => Dispatcher.Invoke(() => RuntimeSettings.IsExplorerLoaded = true));

        Task.Delay(INIT_NAV_HIDE_FILTER_DELAY).ContinueWith(_ => Dispatcher.Invoke(() =>
        {
            if (!SelectionHelper.GetIsMenuOpen(ExplorerGrid.ContextMenu))
                RuntimeSettings.IsSearchBoxFocused = false;
        }));

        if (Width > MAX_WINDOW_WIDTH_FOR_SEARCH_AUTO_COLLAPSE)
            RuntimeSettings.IsSearchBoxFocused = true;

        return _navigateToPath(realPath);
    }

    private static void ListDevices(IEnumerable<LogicalDevice> devices)
    {
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
        if (ConnectTimer.Interval == CONNECT_TIMER_INIT)
            ConnectTimer.Interval = CONNECT_TIMER_INTERVAL;

        Task.Run(() =>
        {
            if (RuntimeSettings.IsPollingStopped || !ConnectTimerMutex.WaitOne(0))
                return;

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
                catch
                { }
            }

            if (RuntimeSettings.IsDevicesPaneOpen)
            {
                DeviceHelper.UpdateDevicesRootAccess();

                DeviceHelper.UpdateWsaPkgStatus();
            }

            ConnectTimerMutex.ReleaseMutex();
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

    public bool NavigateToPath(FileClass file)
    {
        if (file is null)
            return false;

        if (!bfNavigation)
            prevPath = file.FullPath;

        string realPath = !string.IsNullOrEmpty(file.LinkTarget)
            ? file.LinkTarget
            : file.FullPath;

        return realPath is not null && _navigateToPath(realPath);
    }

    public bool NavigateToPath(string path)
    {
        if (path is null)
            return false;

        if (!bfNavigation)
            prevPath = path;

        var realPath = FolderHelper.FolderExists(path);
        return realPath is not null && _navigateToPath(realPath);
    }

    private bool _navigateToPath(string realPath)
    {
        PasteGrid.Visibility = Visibility.Collapsed;
        FileActions.ListingInProgress = true;

        FileActions.ExplorerFilter = "";
        NavHistory.Navigate(realPath);

        SelectionHelper.SetFirstSelectedIndex(ExplorerGrid, -1);
        SelectionHelper.SetCurrentSelectedIndex(ExplorerGrid, -1);

        ExplorerGrid.Focus();
        CurrentPath = realPath;

        NavigationBox.Path = realPath == RECYCLE_PATH ? NavHistory.StringFromLocation(NavHistory.SpecialLocation.RecycleBin) : realPath;
        ParentPath = FileHelper.GetParentPath(CurrentPath);
        CurrentDrive = DriveHelper.GetCurrentDrive(CurrentPath);

        FileActions.IsRecycleBin = CurrentPath == RECYCLE_PATH;
        FileActions.IsAppDrive = CurrentPath == NavHistory.StringFromLocation(NavHistory.SpecialLocation.PackageDrive);
        FileActions.IsTemp = CurrentPath == TEMP_PATH;
        FileActions.ParentEnabled = CurrentPath != ParentPath && !FileActions.IsRecycleBin && !FileActions.IsAppDrive;

        FileActionLogic.IsPasteEnabled();

        FileActions.PushPackageEnabled = Settings.EnableApk && DevicesObject?.Current?.Type is not AbstractDevice.DeviceType.Recovery;
        FileActions.UninstallPackageEnabled = false;

        FileActions.ContextPushPackagesEnabled =
        FileActions.IsUninstallVisible.Value = FileActions.IsAppDrive;

        FileActions.PushFilesFoldersEnabled =
        FileActions.ContextNewEnabled =
        FileActions.ContextPushEnabled =
        FileActions.NewEnabled = !FileActions.IsRecycleBin && !FileActions.IsAppDrive;

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

        FileActions.CopyPathDescription.Value = FileActions.IsAppDrive ? Strings.Resources.S_COPY_APK_NAME : Strings.Resources.S_COPY_PATH;
        
        if (FileActions.IsRecycleBin)
        {
            TrashHelper.ParseIndexersAsync().ContinueWith(_ => DirList.Navigate(realPath));

            FileActions.DeleteDescription.Value = Strings.Resources.S_EMPTY_TRASH;
            FileActions.RestoreDescription.Value = Strings.Resources.S_RESTORE_ALL;
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

            FileActions.DeleteDescription.Value = Strings.Resources.S_DELETE_ACTION;
        }

        RuntimeSettings.ExplorerSource = DirList.FileList;
        FileActionLogic.UpdateFileActions();
        return true;
    }

    private void NavigateToLocation(object location)
    {
        SelectionHelper.SetIsMenuOpen(ExplorerGrid.ContextMenu, false);

        if (NavHistory.LocationFromString(location) is NavHistory.SpecialLocation.DriveView)
        {
            FileActions.IsRecycleBin = false;
            RuntimeSettings.IsPathBoxFocused = false;
            FileActionLogic.RefreshDrives();
            DriveViewNav();

            FileActionLogic.UpdateFileActions();
        }
        else
        {
            string path;
            if (location is NavHistory.SpecialLocation special)
                path = NavHistory.StringFromLocation(special);
            else if (location is string str)
                path = str;
            else
                return;

            if (!FileActions.IsExplorerVisible)
                InitNavigation(path);
            else
                NavigateToPath(path);
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
        switch (key)
        {
            case Key.Enter when IsInEditMode:
                return;
            case Key.Enter:
            {
                if (ExplorerGrid.SelectedItems.Count == 1 && ExplorerGrid.SelectedItem is FilePath { IsDirectory: true })
                    DoubleClick(ExplorerGrid.SelectedItem);
                break;
            }
            case Key.Back:
                NavHistory.NavigateBF(NavHistory.SpecialLocation.Back);
                break;

            case Key.Delete when FileActions.DeleteEnabled:
                FileActionLogic.DeleteFiles();
                break;

            case Key.Up or Key.Down when Keyboard.Modifiers.HasFlag(ModifierKeys.Shift):
                ExplorerGrid.MultiSelect(key);
                break;

            case Key.Up or Key.Down:
                ExplorerGrid.SingleSelect(key);
                break;

            case Key.F2:
                AppActions.List.First(action => action.Name is FileAction.FileActionType.Rename).Command.Execute();
                break;

            default:
                return;
        }

        e.Handled = true;
    }

    private void OnButtonKeyDown(object sender, KeyEventArgs e)
    {
        var NavKeys = new[] { Key.Enter, Key.Up, Key.Down, Key.Left, Key.Right, Key.Escape, Key.Home, Key.End };

        if (Keyboard.IsKeyDown(Key.LeftAlt)
            || Keyboard.IsKeyDown(Key.RightAlt)
            || !NavKeys.Contains(e.Key))
            return;

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

    private bool DriveViewKeyNavigation(Key key)
    {
        if (DriveList.Items.Count == 0)
            return false;

        if (DriveList.SelectedItems.Count == 0)
        {
            switch (key)
            {
                case Key.Left or Key.Up:
                    DriveList.SelectedIndex = DriveList.Items.Count - 1;
                    break;

                case Key.Right or Key.Down:
                    DriveList.SelectedIndex = 0;
                    break;

                default:
                    return false;
            }

            SelectionHelper.GetListViewItemContainer(DriveList).Focus();
            return true;
        }

        switch (key)
        {
            case Key.Enter:
                ((DriveViewModel)DriveList.SelectedItem).BrowseCommand.Execute();
                return true;

            case Key.Escape:
                // Should've been clear selected drives, but causes inconsistent behavior
                return true;

            default:
                return false;
        }
    }

    private bool ExplorerGridKeyNavigation(Key key)
    {
        if (ExplorerGrid.Items.Count < 1)
            return false;

        switch (key)
        {
            case Key.Down or Key.Up or Key.Home or Key.End:
                if (bfNavigation)
                {
                    SelectionHelper.SetCurrentSelectedIndex(ExplorerGrid, ExplorerGrid.SelectedIndex);
                    bfNavigation = false;
                }

                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
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
            case Key.Apps:
                ExplorerGrid.ContextMenu.IsOpen = true;
                break;
            default:
                return false;
        }

        return true;
    }

    private void DataGridRow_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is not MouseButton.Left)
            return;

        if (e.OriginalSource is Border)
        {
            ClickCount = -1;
            return;
        }

        WasDragging = false;
        var row = sender as DataGridRow;

        CopyPaste.DragStatus = e.OriginalSource is TextBlock or Image || row.IsSelected
            ? CopyPasteService.DragState.Pending
            : CopyPasteService.DragState.None;

        SelectionHelper.SetIndexSingle(ExplorerGrid, row.GetIndex());
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
        if (RowHeight is null && ExplorerGrid.ItemContainerGenerator.ContainerFromIndex(0) is DataGridRow row)
            RowHeight = row.ActualHeight;

        WasDragging = false;
        CopyPaste.DragStatus = e.OriginalSource is TextBlock or Image
                     ? CopyPasteService.DragState.Pending
                     : CopyPasteService.DragState.None;

        var point = e.GetPosition(ExplorerGrid);
        MouseDownPoint = point;

        int selectionIndex = ExplorerGrid.SelectedIndex;

        var actualRowWidth = ExplorerGrid.Columns
            .Where(col => col.Visibility == Visibility.Visible)
            .Sum(item => item.ActualWidth);

        if (point.Y > (ExplorerGrid.Items.Count * RowHeight + ColumnHeaderHeight)
            || point.Y > (ExplorerGrid.ActualHeight - StyleHelper.FindDescendant<ItemsPresenter>(ExplorerGrid)?.ActualHeight % RowHeight)
            || point.Y < ColumnHeaderHeight + ScrollContentPresenterMargin
            || point.X > actualRowWidth
            || point.X > DataGridContentWidth)
        {
            if (ExplorerGrid.SelectedItems.Count > 0 && IsInEditMode)
                IsInEditMode = false;

            if (Keyboard.Modifiers is not ModifierKeys.Control and not ModifierKeys.Shift)
            {
                ExplorerGrid.UnselectAll();
                selectionIndex = -1;
            }
        }

        SelectionHelper.SetCurrentSelectedIndex(ExplorerGrid, selectionIndex);

        if (SelectionHelper.GetFirstSelectedIndex(ExplorerGrid) < 0
            || Keyboard.Modifiers is not ModifierKeys.Control and not ModifierKeys.Shift)
        {
            SelectionHelper.SetFirstSelectedIndex(ExplorerGrid, selectionIndex);
        }
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        NavigationBox.Refresh();
        ResizeDetailedView();
        EnableSplitViewAnimation();
        SearchBoxMaxWidth();

        if (!RuntimeSettings.IsWindowLoaded)
            return;

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

        bool enableAnimation = Settings.IsAnimated
            && (NativeMethods.MonitorInfo.IsPrimaryMonitor(this) is true
            || WindowState is not WindowState.Maximized);

        StyleHelper.SetActivateAnimation(SettingsSplitView, enableAnimation);
        StyleHelper.SetActivateAnimation(DevicesSplitView, enableAnimation);
    }

    private void ResizeDetailedView()
    {
        double windowHeight = WindowState == WindowState.Maximized ? ActualHeight : Height;

        if (DetailedViewSize() is var val && val == -1)
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

        collectionView.Filter = predicate;

        if (collectionView.SortDescriptions.All(d => d.PropertyName != nameof(DriveViewModel.Type)))
            collectionView.SortDescriptions.Add(new(nameof(DriveViewModel.Type), ListSortDirection.Ascending));
    }

    private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        ScrollViewer scroller = StyleHelper.FindDescendant<ScrollViewer>((DependencyObject)sender, true);
        if (scroller is null)
            return;

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

    private void GridBackgroundBlock_MouseDown(object sender, MouseButtonEventArgs e)
    {
        RuntimeSettings.IsPathBoxFocused = false;
        DriveHelper.ClearSelectedDrives();
    }

    private void FilterExplorerItems(bool refreshOnly = false)
    {
        //https://docs.microsoft.com/en-us/dotnet/desktop/wpf/controls/how-to-group-sort-and-filter-data-in-the-datagrid-control?view=netframeworkdesktop-4.8

        if (!FileActions.IsExplorerVisible)
            return;

        var collectionView = CollectionViewSource.GetDefaultView(ExplorerGrid.ItemsSource);
        if (collectionView is null)
            return;

        if (refreshOnly)
            collectionView.Refresh();

        if (FileActions.IsAppDrive)
        {
            collectionView.Filter = Settings.ShowSystemPackages
                ? FileHelper.PkgFilter()
                : pkg => ((Package)pkg).Type is Package.PackageType.User;

            if (collectionView.SortDescriptions.All(d => d.PropertyName != nameof(Package.Type)))
            {
                ExplorerGrid.Columns[8].SortDirection = ListSortDirection.Descending;

                collectionView.SortDescriptions.Add(new(nameof(Package.Type), ListSortDirection.Descending));
            }
        }
        else
        {
            collectionView.Filter = !Settings.ShowHiddenItems
                ? FileHelper.HideFiles()
                : file => !FileHelper.IsHiddenRecycleItem((FileClass)file);

            if (!collectionView.SortDescriptions.Any(d => d.PropertyName
                    is nameof(FileClass.IsTemp)
                    or nameof(FileClass.IsDirectory)
                    or nameof(FileClass.SortName)))
            {
                ExplorerGrid.Columns[1].SortDirection = ListSortDirection.Ascending;

                collectionView.SortDescriptions.Add(new(nameof(FileClass.IsTemp), ListSortDirection.Descending));
                collectionView.SortDescriptions.Add(new(nameof(FileClass.IsDirectory), ListSortDirection.Descending));
                collectionView.SortDescriptions.Add(new(nameof(FileClass.SortName), ListSortDirection.Ascending));
            }
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
        => FileOpColumns.UpdateColumnIndexes();

    private void NameColumnEdit_Loaded(object sender, RoutedEventArgs e)
    {
        var textBox = sender as TextBox;
        TextHelper.SetAltObject(textBox, FileHelper.GetFromCell(ExplorerGrid.SelectedCells[1]));
        textBox.Focus();
        
        var editPoint = textBox.TranslatePoint(new(), ExplorerCanvas);
        Canvas.SetTop(RenameTooltip, editPoint.Y - RenameTooltip.ActualHeight - 4);
        Canvas.SetLeft(RenameTooltip, editPoint.X + 4);
        RenameTooltip.Visibility = Visibility.Visible;
    }

    private void NameColumnEdit_LostFocus(object sender, RoutedEventArgs e)
    {
        FileActionLogic.Rename(sender as TextBox);
        RenameTooltip.Visibility = Visibility.Hidden;
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

            AppActions.List.First(action => action.Name is FileActionType.Rename).Command.Execute();
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

    private void DataGridCell_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is not MouseButton.Left)
            return;

        if (e.OriginalSource is Border)
        {
            ClickCount = -1;
            return;
        }

        var cell = sender as DataGridCell;
        WasEditing = cell.IsEditing;

        if (WasEditing)
            return;

        var row = DataGridRow.GetRowContainingElement(cell);
        var current = row.GetIndex();

        WasSelected = row.IsSelected;
        CopyPaste.DragStatus = e.OriginalSource is TextBlock or Image || row.IsSelected
                     ? CopyPasteService.DragState.Pending
                     : CopyPasteService.DragState.None;

        MouseDownPoint = e.GetPosition(ExplorerGrid);
        e.Handled = true;
        ClickCount = e.ClickCount;

        if (ClickCount > 1)
        {
            DoubleClick(cell.DataContext);
            return;
        }

        RuntimeSettings.IsPathBoxFocused = false;

        if (!row.IsSelected
            && CopyPaste.DragStatus is not CopyPasteService.DragState.None
            && Keyboard.Modifiers is not ModifierKeys.Control and not ModifierKeys.Shift)
        {
            ExplorerGrid.UnselectAll();
            row.IsSelected = true;
        }

        SelectionHelper.SetNextSelectedIndex(ExplorerGrid, current);
        SelectionHelper.SetCurrentSelectedIndex(ExplorerGrid, current);
        if (ExplorerGrid.SelectedItems.Count < 1)
            SelectionHelper.SetFirstSelectedIndex(ExplorerGrid, current);
    }

    private void NameColumnEdit_TextChanged(object sender, TextChangedEventArgs e)
    {
        var textBox = sender as TextBox;
        var file = (FileClass)textBox.DataContext;
        textBox.FilterString(CurrentDrive.IsFUSE
            ? INVALID_NTFS_CHARS
            : INVALID_UNIX_CHARS);

        FileActions.IsRenameUnixLegal = FileHelper.FileNameLegal(textBox.Text, FileHelper.RenameTarget.Unix);
        FileActions.IsRenameFuseLegal = FileHelper.FileNameLegal(textBox.Text, FileHelper.RenameTarget.FUSE);
        FileActions.IsRenameWindowsLegal = FileHelper.FileNameLegal(textBox.Text, FileHelper.RenameTarget.Windows);
        FileActions.IsRenameDriveRootLegal = FileHelper.FileNameLegal(textBox.Text, FileHelper.RenameTarget.WinRoot);

        var fullName = Settings.ShowExtensions
            ? textBox.Text
            : textBox.Text + file.Extension;

        var comparison = CurrentDrive.IsFUSE
            ? StringComparison.InvariantCultureIgnoreCase
            : StringComparison.InvariantCulture;

        FileActions.IsRenameUnique = !DirList.FileList.Except([file]).Any(f => f.FullName.Equals(fullName, comparison));
    }

    private void NewItem(bool isFolder)
    {
        var fileName = FileHelper.DuplicateFile(DirList.FileList, isFolder
            ? Strings.Resources.S_NEW_FOLDER
            : Strings.Resources.S_NEW_ITEM);

        FileClass newItem = new(fileName, FileHelper.ConcatPaths(CurrentPath, fileName), isFolder ? FileType.Folder : FileType.File, isTemp: true);
        DirList.FileList.Insert(0, newItem);

        ExplorerGrid.ScrollIntoView(newItem);
        ExplorerGrid.SelectedItem = newItem;

        IsInEditMode = true;
        if (!IsInEditMode) // in case cell was not acquired
            FileActionLogic.CreateNewItem(newItem);
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
            collectionView.Filter = sett => ((AbstractSetting)sett).Description.Contains(RuntimeSettings.SearchText, StringComparison.OrdinalIgnoreCase)
                                            || (sett is EnumSetting enumSett && enumSett.Buttons.Any(button => button.Name.Contains(RuntimeSettings.SearchText, StringComparison.OrdinalIgnoreCase)));
        }
    }

    private void MainWin_StateChanged(object sender, EventArgs e)
    {
        SearchBoxMaxWidth();
    }

    private void SortedSettings_MouseMove(object sender, MouseEventArgs e)
    {
        // Prevent focus from being set on the ComboBox when it is open
        // this only works for the first combobox
        // when we'll have more, this will need to be adjusted
        var combobox = StyleHelper.FindDescendant<ComboBox>(SortedSettings);
        if (combobox is not null && 
            (combobox.IsDropDownOpen || e.LeftButton is MouseButtonState.Pressed && combobox.IsMouseOver))
            return;

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
        if (Mouse.LeftButton is MouseButtonState.Released)
            CopyPaste.ClearDrag();

        var point = e.GetPosition(ExplorerCanvas);
        bool withinEditingCell = false;
        DataGridCell cell = ExplorerGrid.SelectedCells.Count > 0
                            ? CellConverter.GetDataGridCell(ExplorerGrid.SelectedCells[1])
                            : null;

        if (IsInEditMode)
        {
            withinEditingCell = VisualTreeHelper.GetDescendantBounds(cell).Contains(e.GetPosition(cell));
        }

        var abortDrag = e.LeftButton == MouseButtonState.Released
            || !RuntimeSettings.IsExplorerLoaded
            || MouseDownPoint == NullPoint
            || withinEditingCell
            || SelectionHelper.GetIsMenuOpen(ExplorerGrid.ContextMenu);

        if (CopyPaste.DragStatus is CopyPasteService.DragState.Pending && (MouseDownPoint - point).LengthSquared >= 25)
        {
            if (ExplorerGrid.SelectedItems.Count > 0
                && ExplorerGrid.SelectedItems[0] is FileClass
                && !abortDrag)
            {
                CopyPaste.DragStatus = CopyPasteService.DragState.Active;
                WasDragging = true;

                var selectedItems = ExplorerGrid.SelectedItems.Cast<FileClass>();
                var vfdo = VirtualFileDataObject.PrepareTransfer(selectedItems, DragDropEffects.Copy | DragDropEffects.Move);

                if (vfdo is not null)
                {
                    CopyPaste.UpdateSelfVFDO();
                    RuntimeSettings.DragBitmap = FileToIconConverter.GetBitmapSource(selectedItems.First());

                    vfdo.SendObjectToShell(VirtualFileDataObject.DataObjectMethod.DragDrop, cell, DragDropEffects.Copy | DragDropEffects.Move);
                }
            }
            else
                CopyPaste.DragStatus = CopyPasteService.DragState.None;
        }

        if (abortDrag || CopyPaste.DragStatus is not CopyPasteService.DragState.None || WasDragging)
        {
            SelectionRect.Visibility = Visibility.Collapsed;
            return;
        }
        var scroller = StyleHelper.FindDescendant<ScrollViewer>(ExplorerGrid);
        var horizontal = scroller.ComputedHorizontalScrollBarVisibility is Visibility.Visible ? 1 : 0;
        var vertical = scroller.ComputedVerticalScrollBarVisibility is Visibility.Visible ? 1 : 0;

        if (!SelectionRect.IsVisible
            && ((point.Y > ExplorerCanvas.ActualHeight - SystemParameters.HorizontalScrollBarHeight * horizontal)
            || (point.X > ExplorerCanvas.ActualWidth - SystemParameters.VerticalScrollBarWidth * vertical)))
        {
            MouseDownPoint = point;
        }

        if (MouseDownPoint.Y > ExplorerCanvas.ActualHeight - SystemParameters.HorizontalScrollBarHeight * horizontal
            || MouseDownPoint.X > ExplorerCanvas.ActualWidth - SystemParameters.VerticalScrollBarWidth * vertical)
            return;

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

        SelectRows(point);
    }

    private void SelectRows(Point mousePosition)
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

            rowRect.Inflate(double.PositiveInfinity, 0);
            if (rowRect.Contains(mousePosition))
                SelectionHelper.SetCurrentSelectedIndex(ExplorerGrid, row.GetIndex());
        }

        if (ExplorerGrid.SelectedItems.Count == 1
            && (SelectionHelper.GetFirstSelectedIndex(ExplorerGrid) < 0
            || Keyboard.Modifiers is not ModifierKeys.Control and not ModifierKeys.Shift))
        {
            SelectionHelper.SetFirstSelectedIndex(ExplorerGrid, ExplorerGrid.SelectedIndex);
        }
    }

    private void ExplorerCanvas_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        SelectionRect.Visibility = Visibility.Collapsed;
        
        if (SelectionHelper.GetFirstSelectedIndex(ExplorerGrid) < 0
            || Keyboard.Modifiers is not ModifierKeys.Control and not ModifierKeys.Shift)
        {
            SelectionHelper.SetFirstSelectedIndex(ExplorerGrid, SelectionHelper.GetNextSelectedIndex(ExplorerGrid));
        }
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
        FileActions.SelectedFileOps.Value = CurrentOperationDetailedDataGrid.SelectedItems.OfType<FileOperation>();
    }

    private void KillAdbButton_Click(object sender, RoutedEventArgs e)
    {
        ADBService.KillAdbProcess();
    }

    private void Grid_MouseEnter(object sender, MouseEventArgs e)
    {
        MouseDownPoint = NullPoint;
    }

    private void DataGridRow_Drop(object sender, DragEventArgs e)
    {
        CopyPaste.AcceptDataObject(e.Data, (FrameworkElement)sender);
        e.Handled = true;
    }

    private void DataGridCell_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is not MouseButton.Left || ClickCount < 0)
            return;

        e.Handled = CellMouseUp(sender, e);

        CopyPaste.DragStatus = CopyPasteService.DragState.None;
    }

    private bool CellMouseUp(object sender, MouseButtonEventArgs e)
    {
        DataGridCell cell;
        DataGridRow row;

        if (CopyPaste.DragStatus is CopyPasteService.DragState.Active || WasDragging)
            return false;

        switch (sender)
        {
            case DataGridCell c:
            {
                cell = c;
                row = DataGridRow.GetRowContainingElement(cell);

                if (cell.IsEditing)
                    return false;
                break;
            }
            case DataGridRow r:
                row = r;
                cell = null;
                break;
            default:
                return false;
        }

        var current = row.GetIndex();
        SelectionHelper.SetCurrentSelectedIndex(ExplorerGrid, current);

        if (MultiRowSelect(row))
            return true;

        if (SelectionHelper.GetFirstSelectedIndex(ExplorerGrid) < 0
            || Keyboard.Modifiers is not ModifierKeys.Control and not ModifierKeys.Shift)
        {
            SelectionHelper.SetFirstSelectedIndex(ExplorerGrid, current);
        }

        if (!row.IsSelected || ExplorerGrid.SelectedItems?.Count != 1)
        {
            ExplorerGrid.UnselectAll();
            row.IsSelected = true;
            return true;
        }

        if (cell?.Column == NameColumn)
            MouseUpOnName(cell);

        return true;
    }

    private void MouseUpOnName(DataGridCell cell)
    {
        if (cell.IsReadOnly
            || (DevicesObject.Current.Root is not AbstractDevice.RootStatus.Enabled
                && ((FileClass)cell.DataContext).Type is not (FileType.File or FileType.Folder)))
            return;

        var path = ((FileClass)ExplorerGrid.SelectedItem).FullPath;

        if (ExplorerGrid.SelectedItems.Count == 1 && WasSelected && !WasEditing)
        {
            Task.Run(() =>
            {
                var start = DateTime.Now;

                while (true)
                {
                    Task.Delay(100);

                    if (DateTime.Now - start > RENAME_CLICK_DELAY)
                        break;

                    var currentPath = Dispatcher.Invoke(() => ((FileClass)ExplorerGrid.SelectedItem)?.FullPath);
                    if (ClickCount > 1 || currentPath != path)
                        return;
                }

                Dispatcher.Invoke(() =>
                {
                    cell.IsEditing = true;
                    FileActions.IsExplorerEditing = true;
                });
            });
        }
    }

    private bool MultiRowSelect(DataGridRow row)
    {
        var current = row.GetIndex();

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            ExplorerGrid.UnselectAll();

            var firstSelected = SelectionHelper.GetFirstSelectedIndex(ExplorerGrid);
            int firstUnselected = firstSelected, lastUnselected = current + 1;
            if (current < firstSelected)
            {
                firstUnselected = current;
                lastUnselected = firstSelected + 1;
            }

            for (int i = firstUnselected; i < lastUnselected; i++)
            {
                ExplorerGrid.SelectedItems.Add(ExplorerGrid.Items[i]);
            }

            return true;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            row.IsSelected = !row.IsSelected;
            return true;
        }

        return false;
    }

    private void AppDataHyperlink_Click(object sender, RoutedEventArgs e)
    {
        if (DateTime.Now - appDataClick < LINK_CLICK_DELAY)
            return;

        appDataClick = DateTime.Now;
        Process.Start("explorer.exe", AppDataPath);
    }

    private void ExplorerGrid_DragOver(object sender, DragEventArgs e)
    {
        var allowed = CopyPaste.GetAllowedDragEffects(e.Data, (FrameworkElement)sender);
        e.Effects = allowed;

        if (allowed.HasFlag(DragDropEffects.Move) && e.KeyStates.HasFlag(DragDropKeyStates.ShiftKey))
        {
            e.Effects = DragDropEffects.Move;
            CopyPaste.DropEffect = DragDropEffects.Move;
        }
        else if (allowed.HasFlag(DragDropEffects.Link) && e.KeyStates.HasFlag(DragDropKeyStates.AltKey))
        {
            e.Effects = DragDropEffects.Link;
            CopyPaste.DropEffect = DragDropEffects.Link;
        }
        else if (allowed.HasFlag(DragDropEffects.Copy)) // copy is the default and does not require Ctrl to be activated
        {
            e.Effects = DragDropEffects.Copy;
            CopyPaste.DropEffect = DragDropEffects.Copy;
        }

        if ((!allowed.HasFlag(DragDropEffects.Copy) && e.KeyStates.HasFlag(DragDropKeyStates.ControlKey))
            || (!allowed.HasFlag(DragDropEffects.Move) && e.KeyStates.HasFlag(DragDropKeyStates.ShiftKey))
            || (!allowed.HasFlag(DragDropEffects.Link) && e.KeyStates.HasFlag(DragDropKeyStates.AltKey)))
        {
            e.Effects = DragDropEffects.None;
        }

        CopyPaste.CurrentDropEffect = e.Effects;

        if (CopyPaste.CurrentFiles.Any())
            RuntimeSettings.DragBitmap = FileToIconConverter.GetBitmapSource(CopyPaste.CurrentFiles.First());

        e.Handled = true;
    }

    private void MainWin_LocationChanged(object sender, EventArgs e)
    {
        if (!RuntimeSettings.IsWindowLoaded)
            return;
    }

    private void MainWin_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (CopyPaste.IsDrag && CopyPaste.DragStatus is not CopyPasteService.DragState.Active && e.Key is Key.Escape)
            RuntimeSettings.DragBitmap = null;
    }

    private void MainWindow_OnPreviewQueryContinueDrag(object sender, QueryContinueDragEventArgs e)
    {
        if (e.EscapePressed)
            RuntimeSettings.DragBitmap = null;
    }

    private void MainWindow_OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.LeftAlt or Key.RightAlt or Key.System && CopyPaste.IsDrag)
            e.Handled = true;
    }

    private void SponsorButton_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(RuntimeSettings.DefaultBrowserPath, $"\"{Links.SPONSOR}\"");
    }

    private void ComboBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is ComboBox comboBox && !comboBox.IsDropDownOpen)
        {
            e.Handled = true;

            // Re-raise the event to scroll the parent ScrollViewer
            var eventArg = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = MouseWheelEvent,
                Source = sender
            };

            var parent = VisualTreeHelper.GetParent(comboBox) as UIElement;
            parent?.RaiseEvent(eventArg);
        }
    }

    private void EmptyNonRootTextBlock_Loaded(object sender, RoutedEventArgs e)
    {
        string xamlString = $"<Span xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xml:space=\"preserve\"{Strings.Resources.S_NON_ROOT_EMPTY_FOLDER[5..]}";

        Span parsedSpan;
        using (StringReader stringReader = new(xamlString))
        using (XmlReader xmlReader = XmlReader.Create(stringReader))
        {
            parsedSpan = (Span)XamlReader.Load(xmlReader);
        }

        EmptyNonRootTextBlock.TextAlignment = TextAlignment.Center;
        EmptyNonRootTextBlock.Inlines.Clear();
        EmptyNonRootTextBlock.Inlines.Add(parsedSpan);
    }
}
