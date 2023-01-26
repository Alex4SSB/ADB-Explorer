using ADB_Explorer.Converters;
using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;
using ADB_Explorer.ViewModels;
using static ADB_Explorer.Converters.FileTypeClass;
using static ADB_Explorer.Helpers.VisibilityHelper;
using static ADB_Explorer.Models.AdbExplorerConst;
using static ADB_Explorer.Models.Data;
using static ADB_Explorer.Resources.Links;
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

    private ItemsPresenter _explorerContentPresenter;
    private ItemsPresenter ExplorerContentPresenter
    {
        get
        {
            if (_explorerContentPresenter is null
                && VisualTreeHelper.GetChild(ExplorerGrid, 0) is Border border
                && border.Child is ScrollViewer scroller
                && scroller.Content is ItemsPresenter presenter)
            {
                _explorerContentPresenter = presenter;
            }

            return _explorerContentPresenter;
        }
    }

    private double ColumnHeaderHeight => (double)FindResource("DataGridColumnHeaderHeight");
    private double ScrollContentPresenterMargin => ((Thickness)FindResource("DataGridScrollContentPresenterMargin")).Top;
    private double DataGridContentWidth => ExplorerContentPresenter is null ? 0 : ExplorerContentPresenter.ActualWidth;

    private readonly List<MenuItem> PathButtons = new();


    public event PropertyChangedEventHandler PropertyChanged;
    protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public string SelectedFilesTotalSize => (selectedFiles is not null && FileClass.TotalSize(selectedFiles) is ulong size and > 0) ? size.ToSize() : "";

    private IEnumerable<FileClass> selectedFiles => FileActions.IsAppDrive ? null : ExplorerGrid.SelectedItems.OfType<FileClass>();

    private IEnumerable<Package> selectedPackages => FileActions.IsAppDrive ? ExplorerGrid.SelectedItems.OfType<Package>() : null;

    private string prevPath = "";

    public MainWindow()
    {
        InitializeComponent();

        KeyDown += new KeyEventHandler(OnButtonKeyDown);

        FileOpQ = new(this.Dispatcher);
        Task launchTask = Task.Run(() => LaunchSequence());

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
        SettingsSearchBox.GotFocus += SettingsSearchBox_FocusChanged;
        SettingsSearchBox.LostFocus += SettingsSearchBox_FocusChanged;
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
        CurrentOperationDataGrid.ItemsSource = FileOpQ.Operations;

#if DEBUG
        TestCurrentOperation();
        TestDevices();
#endif

        Task.Run(() =>
        {
            SettingsHelper.SplashScreenTask();
            Task.WaitAll(launchTask, versionTask);
            Settings.WindowLoaded = true;
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
    }

    private void RuntimeSettings_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            switch (e.PropertyName)
            {
                case nameof(AppRuntimeSettings.DeviceToOpen) when RuntimeSettings.DeviceToOpen:
                    DevicesObject.SetOpenDevice(RuntimeSettings.DeviceToOpen);
                    InitLister();
                    ClearExplorer();
                    NavHistory.Reset();
                    InitDevice();

                    RuntimeSettings.IsDevicesPaneOpen = false;
                    break;
                case nameof(AppRuntimeSettings.DeviceToRemove) when RuntimeSettings.DeviceToRemove is not null:
                    switch (RuntimeSettings.DeviceToRemove)
                    {
                        case LogicalDeviceViewModel logical:
                            if (logical.IsOpen)
                            {
                                DriveHelper.ClearDrives();
                                ClearExplorer();
                                NavHistory.Reset();
                                FileActions.IsExplorerVisible = false;
                                CurrentADBDevice = null;
                                DirList = null;
                                RuntimeSettings.DeviceToOpen = null;
                            }

                            DevicesObject.UIList.Remove(RuntimeSettings.DeviceToRemove);
                            FilterDevices();
                            DeviceListSetup();
                            break;
                        case HistoryDeviceViewModel hist:
                            DevicesObject.RemoveHistoryDevice(hist);
                            break;
                        default:
                            throw new NotSupportedException();
                    }

                    RuntimeSettings.DeviceToRemove = null;
                    break;

                case nameof(AppRuntimeSettings.DeviceToPair) when RuntimeSettings.DeviceToPair is not null:
                    var deviceToPair = RuntimeSettings.DeviceToPair;
                    _ = DeviceHelper.PairService(deviceToPair);
                    RuntimeSettings.DeviceToPair = null;
                    break;

                case nameof(AppRuntimeSettings.RootAttemptForbidden) when RuntimeSettings.RootAttemptForbidden:
                    RuntimeSettings.RootAttemptForbidden = false;
                    DialogService.ShowMessage(S_ROOT_FORBID, S_ROOT_FORBID_TITLE, DialogService.DialogIcon.Critical);
                    break;

                case nameof(AppRuntimeSettings.ConnectNewDevice) when RuntimeSettings.ConnectNewDevice is not null:
                    RuntimeSettings.IsManualPairingInProgress = true;

                    if (RuntimeSettings.ConnectNewDevice is NewDeviceViewModel newDevice && newDevice.IsPairingEnabled)
                        PairNewDevice();
                    else
                        ConnectNewDevice();
                    break;

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

                case nameof(AppRuntimeSettings.LocationToNavigate):
                    switch (RuntimeSettings.LocationToNavigate)
                    {
                        case NavHistory.SpecialLocation.DriveView:
                            NavHistory.Navigate(RuntimeSettings.LocationToNavigate);
                            NavigateToLocation(RuntimeSettings.LocationToNavigate);
                            break;
                        case NavHistory.SpecialLocation.Back:
                            NavigateToLocation(NavHistory.GoBack(), true);
                            break;
                        case NavHistory.SpecialLocation.Forward:
                            NavigateToLocation(NavHistory.GoForward(), true);
                            break;
                        case NavHistory.SpecialLocation.Up:
                            NavigateToPath(ParentPath);
                            break;
                        default:
                            break;
                    }
                    break;

                case nameof(AppRuntimeSettings.IsDevicesPaneOpen)
                    or nameof(AppRuntimeSettings.IsSettingsPaneOpen)
                    or nameof(AppRuntimeSettings.IsOperationsViewOpen):
                    UnfocusPathBox();
                    DeviceHelper.CollapseDevices();
                    break;

                case nameof(AppRuntimeSettings.BeginPull):
                    UnfocusPathBox();
                    PullFiles();
                    break;

                case nameof(AppRuntimeSettings.PushFolders):
                    UnfocusPathBox();
                    PushItems(true, false);
                    break;

                case nameof(AppRuntimeSettings.PushFiles):
                    UnfocusPathBox();
                    PushItems(false, false);
                    break;

                case nameof(AppRuntimeSettings.PushPackages):
                    PushPackages();
                    break;

                case nameof(AppRuntimeSettings.NewFolder):
                    NewItem(true);
                    break;

                case nameof(AppRuntimeSettings.NewFile):
                    NewItem(false);
                    break;

                case nameof(AppRuntimeSettings.Cut):
                    CutFiles(selectedFiles);
                    break;
                    
                case nameof(AppRuntimeSettings.Copy):
                    CutFiles(selectedFiles, true);
                    break;

                case nameof(AppRuntimeSettings.Paste):
                    PasteFiles();
                    break;

                case nameof(AppRuntimeSettings.Rename):
                    BeginRename();
                    break;

                case nameof(AppRuntimeSettings.Restore):
                    RestoreItems();
                    break;

                case nameof(AppRuntimeSettings.Delete):
                    DeleteFiles();
                    break;

                case nameof(AppRuntimeSettings.Uninstall):
                    UninstallPackages();
                    break;

                case nameof(AppRuntimeSettings.CopyItemPath):
                    CopyItemPath();
                    break;

                case nameof(AppRuntimeSettings.UpdateModifiedTime):
                    UpdatedModifiedDates();
                    break;

                case nameof(AppRuntimeSettings.EditItem):
                    OpenEditor();
                    break;

                case nameof(AppRuntimeSettings.InstallPackage):
                    InstallPackages();
                    break;

                case nameof(AppRuntimeSettings.CopyToTemp):
                    CopyToTemp();
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

                case nameof(AppRuntimeSettings.EditCurrentPath):
                    if (PathBox.IsFocused)
                        UnfocusPathBox();
                    else
                        FocusPathBox();
                    break;

                case nameof(AppRuntimeSettings.Filter):
                    if (SearchBox.IsFocused)
                        FileOperationsSplitView.Focus();
                    else
                        SearchBox.Focus();
                    break;
            }
        });
    }

    private void SettingsSearchBox_FocusChanged(object sender, RoutedEventArgs e)
    {
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
            FilterHiddenFiles();
        }
    }

    private void FileOperationQueue_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FileOperationQueue.IsActive) or nameof(FileOperationQueue.AnyFailedOperations) or nameof(FileOpQ.Progress))
        {
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
    }

    private void CommandLog_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is null)
            return;

        foreach (Log item in e.NewItems)
        {
            if (Dispatcher.HasShutdownStarted)
                return;

            Dispatcher.Invoke(() =>
            {
                if (PauseAutoScrollButton.IsChecked == false)
                {
                    LogTextBox.Text += $"{item}\n";
                    LogTextBox.CaretIndex = LogTextBox.Text.Length;
                    LogTextBox.ScrollToEnd();
                }
            });
        }
    }

    private void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppSettings.ForceFluentStyles):
                SettingsHelper.SetSymbolFont();
                PopulateButtons(CurrentPath);
                break;
            case nameof(AppSettings.Theme):
                SetTheme(Settings.Theme);
                break;
            case nameof(AppSettings.EnableMdns):
                AdbHelper.EnableMdns();
                break;
            case nameof(AppSettings.ShowHiddenItems):
                FilterHiddenFiles();
                break;
            case nameof(AppSettings.ShowSystemPackages):
                if (DevicesObject.Current is null)
                    return;

                if (FileActions.IsExplorerVisible)
                    FilterHiddenFiles();
                else
                    UpdatePackages();
                break;
            case nameof(AppSettings.EnableLog):
                if (!Settings.EnableLog)
                    ClearLogs();
                break;
            case nameof(AppSettings.SwRender):
                SetRenderMode();
                break;
            case nameof(AppSettings.EnableRecycle) or nameof(AppSettings.EnableApk):
                if (DevicesObject.Current is null)
                    return;

                FilterDrives();
                FileActions.PushPackageEnabled = Settings.EnableApk;

                if (NavHistory.Current is NavHistory.SpecialLocation.DriveView)
                    RefreshDrives(true);
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
            DirectoryLoadingProgressBar.Visible(DirList.IsProgressVisible);
            UnfinishedBlock.Visible(DirList.IsProgressVisible);
        }
        else if (e.PropertyName == nameof(DirectoryLister.InProgress))
        {
            FileActions.ListingInProgress = DirList.InProgress;

            if (DirList.InProgress)
                return;

            if (FileActions.IsRecycleBin)
            {
                TrashHelper.EnableRecycleButtons();
                TrashHelper.UpdateIndexerFile();
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
        Dispatcher.Invoke(() => SetTheme());

    

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        DirList?.Stop();

        ConnectTimer.Stop();
        ServerWatchdogTimer.Stop();
        StoreClosingValues();
    }

    private void StoreClosingValues()
    {
        Storage.StoreValue(SystemVals.windowMaximized, WindowState == WindowState.Maximized);

        var detailedVisible = RuntimeSettings.IsOperationsViewOpen && Settings.ShowExtendedView;
        Storage.StoreValue(SystemVals.detailedVisible, detailedVisible);
        if (detailedVisible)
            Storage.StoreValue(SystemVals.detailedHeight, FileOpDetailedGrid.Height);
    }

    private void SetTheme()
    {
        SetTheme(Settings.Theme is AppTheme.windowsDefault
            ? themeService.WindowsTheme
            : ThemeManager.Current.ApplicationTheme.Value);
    }

    private void SetTheme(AppTheme theme) => SetTheme(AppThemeToActual(theme));

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

    private void SetResourceColor(ApplicationTheme theme, string resource)
    {
        Dispatcher.Invoke(() => Application.Current.Resources[resource] = new SolidColorBrush((Color)Application.Current.Resources[$"{theme}{resource}"]));
    }

    private void PathBox_GotFocus(object sender, RoutedEventArgs e)
    {
        FocusPathBox();
    }

    private void FocusPathBox()
    {
        PathMenu.Visibility = Visibility.Collapsed;
        if (!FileActions.IsRecycleBin && !FileActions.IsAppDrive)
            PathBox.Text = TextHelper.GetAltText(PathBox);

        PathBox.IsReadOnly = false;
        PathBox.Focus();
        PathBox.SelectAll();
    }

    private void UnfocusPathBox()
    {
        PathMenu.Visibility = Visibility.Visible;
        PathBox.Clear();
        PathBox.IsReadOnly = true;
        FileOperationsSplitView.Focus();
    }

    private void PathBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape || (e.Key == Key.Enter && PathBox.Text == ""))
        {
            UnfocusPathBox();
        }
        else if (e.Key == Key.Enter)
        {
            if (ExplorerGrid.IsVisible)
            {
                if (PathBox.Text == "-")
                    NavHistory.NavigateBF(NavHistory.SpecialLocation.Back);
                else if (NavigateToPath(PathBox.Text.StartsWith(RECYCLE_PATH) ? RECYCLE_PATH : PathBox.Text))
                    return;
            }
            else
            {
                if (!InitNavigation(PathBox.Text))
                {
                    DriveViewNav();
                    NavHistory.Navigate(NavHistory.SpecialLocation.DriveView);
                    return;
                }
            }

            e.Handled = true;
            ExplorerGrid.Focus();
        }
    }

    private void PathBox_LostFocus(object sender, RoutedEventArgs e)
    {
        UnfocusPathBox();
    }

    private void DataGridRow_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && !FileActions.IsAppDrive && selectedFiles.Count() == 1 && !IsInEditMode())
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
                        case DoubleClickAction.pull when Settings.IsPullOnDoubleClickEnabled:
                            PullFiles(true);
                            break;
                        case DoubleClickAction.edit when FileActions.EditFileEnabled:
                            OpenEditor();
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

        if (SelectionHelper.GetIsMenuOpen(ExplorerGrid.ContextMenu))
        {
            Task.Run(() => Task.Delay(SELECTION_CHANGED_DELAY)).ContinueWith((t) => Dispatcher.Invoke(() => UpdateFileActions()));
        }
        else
            UpdateFileActions();
    }

    private void UpdateFileActions()
    {
        FileActions.UninstallPackageEnabled = FileActions.IsAppDrive && selectedPackages.Any();
        FileActions.ContextPushPackagesEnabled = FileActions.IsAppDrive && !selectedPackages.Any();

        FileActions.IsRefreshEnabled = FileActions.IsDriveViewVisible || FileActions.IsExplorerVisible;
        FileActions.IsCopyCurrentPathEnabled = FileActions.IsExplorerVisible && !FileActions.IsRecycleBin && !FileActions.IsAppDrive;

        if (FileActions.IsAppDrive)
        {
            FileActions.IsCopyItemPathEnabled = selectedPackages.Count() == 1;

            FilterFileActions();
            return;
        }

        OnPropertyChanged(nameof(SelectedFilesTotalSize));

        FileActions.IsRegularItem = !(selectedFiles.Any() && DevicesObject.Current?.Root is not AbstractDevice.RootStatus.Enabled
            && selectedFiles.All(item => item is FileClass file && file.Type is not (FileType.File or FileType.Folder)));

        if (FileActions.IsRecycleBin)
        {
            TrashHelper.EnableRecycleButtons(selectedFiles.Any() ? selectedFiles : DirList.FileList);
        }
        else
        {
            FileActions.DeleteEnabled = selectedFiles.Any() && FileActions.IsRegularItem;
            FileActions.RestoreEnabled = false;
        }

        FileActions.DeleteAction.Value = FileActions.IsRecycleBin && !selectedFiles.Any() ? "Empty Recycle Bin" : "Delete";
        FileActions.RestoreAction.Value = FileActions.IsRecycleBin && !selectedFiles.Any() ? "Restore All Items" : "Restore";

        FileActions.PullEnabled = FileActions.PushPullEnabled && !FileActions.IsRecycleBin && selectedFiles.Any() && FileActions.IsRegularItem;
        FileActions.ContextPushEnabled = FileActions.PushPullEnabled && !FileActions.IsRecycleBin && (!selectedFiles.Any() || (selectedFiles.Count() == 1 && selectedFiles.First().IsDirectory));

        FileActions.RenameEnabled = !FileActions.IsRecycleBin && selectedFiles.Count() == 1 && FileActions.IsRegularItem;

        FileActions.CutEnabled = !selectedFiles.All(file => file.CutState is FileClass.CutType.Cut) && FileActions.IsRegularItem;

        FileActions.CopyEnabled = !FileActions.IsRecycleBin && FileActions.IsRegularItem && !selectedFiles.All(file => file.CutState is FileClass.CutType.Copy);
        FileActions.PasteEnabled = IsPasteEnabled();
        FileActions.IsKeyboardPasteEnabled = IsPasteEnabled(true, true);

        FileActions.PackageActionsEnabled = Settings.EnableApk && selectedFiles.Any() && selectedFiles.All(file => file.IsInstallApk) && !FileActions.IsRecycleBin;
        FileActions.IsCopyItemPathEnabled = selectedFiles.Count() == 1 && !FileActions.IsRecycleBin;

        FileActions.ContextNewEnabled = !selectedFiles.Any() && !FileActions.IsRecycleBin;
        FileActions.SubmenuUninstallEnabled = FileActions.IsTemp && selectedFiles.Any() && selectedFiles.All(file => file.IsInstallApk);

        FileActions.UpdateModifiedEnabled = !FileActions.IsRecycleBin && selectedFiles.Any() && selectedFiles.All(file => file.Type is FileType.File && !file.IsApk);

        FileActions.EditFileEnabled = !FileActions.IsRecycleBin
            && selectedFiles.Count() == 1
            && selectedFiles.First().Type is FileType.File
            && !selectedFiles.First().IsApk;

        FilterFileActions();
    }

    private bool IsPasteEnabled(bool ignoreSelected = false, bool isKeyboard = false)
    {
        // Explorer view but not trash or app drive
        if (FileActions.IsPasteStateVisible)
        {
            FileActions.CutItemsCount.Value = CutItems.Count > 0 ? CutItems.Count.ToString() : "";
            FileActions.IsCutState.Value = FileActions.PasteState is FileClass.CutType.Cut;
            FileActions.IsCopyState.Value = FileActions.PasteState is FileClass.CutType.Copy;
        }
        else
        {
            FileActions.CutItemsCount.Value = "";
            FileActions.IsCopyState.Value = false;
            FileActions.IsCutState.Value = false;
        }
        
        FileActions.PasteAction.Value = $"Paste {CutItems.Count} {FileClass.CutTypeString(FileActions.PasteState)} Item{(CutItems.Count > 1 ? "s" : "")}";

        if (CutItems.Count < 1 || FileActions.IsRecycleBin || !FileActions.IsExplorerVisible)
            return false;

        if (CutItems.Count == 1 && CutItems[0].Relation(CurrentPath) is RelationType.Descendant or RelationType.Self)
            return false;

        var selected = ignoreSelected ? 0 : selectedFiles?.Count();
        switch (selected)
        {
            case 0:
                return !(CutItems[0].ParentPath == CurrentPath && FileActions.PasteState is FileClass.CutType.Cut);
            case 1:
                if (isKeyboard && FileActions.PasteState is FileClass.CutType.Copy && CutItems[0].Relation(selectedFiles.First()) is RelationType.Self)
                    return true;

                var item = ExplorerGrid.SelectedItem as FilePath;
                if (!item.IsDirectory
                    || (CutItems.Count == 1 && CutItems[0].FullPath == item.FullPath)
                    || (CutItems[0].ParentPath == item.FullPath))
                    return false;
                break;
            default:
                return false;
        }

        return true;
    }

    private void FilterFileActions(bool filterContextMenu = true)
    {
        var collectionView = CollectionViewSource.GetDefaultView(MainToolBar.ItemsSource);
        if (collectionView is null)
            return;

        Predicate<object> predicate = m =>
            ((ActionMenu)m).Icon switch
            {
                "\uECC8" => FileActions.NewMenuVisible,
                "\uE845" => FileActions.IsRecycleBin,
                "\uE25B" => FileActions.UninstallVisible,
                _ => true,
            };

        collectionView.Filter = new(predicate);

        if (filterContextMenu)
        {
            var contextCollectionView = CollectionViewSource.GetDefaultView(ExplorerGrid.ContextMenu.ItemsSource);
            if (contextCollectionView is null)
                return;

            Predicate<object> contextPredicate = m =>
            {
                var menu = m as SubMenu;

                if (menu.Children is SubMenu[] list)
                    return menu.Action.Command.IsEnabled && list.Any(child => child.Action.Command.IsEnabled);

                return menu.Action.Command.IsEnabled;
            };

            contextCollectionView.Filter = new(contextPredicate);
        }
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        UnfocusPathBox();
    }

    private void LaunchSequence()
    {
        var height = SystemParameters.PrimaryScreenHeight;
        Dispatcher.BeginInvoke(() =>
        {
            Height = height * WINDOW_HEIGHT_RATIO;
            Width = Height / WINDOW_WIDTH_RATIO;

            if (RuntimeSettings.MaxSearchBoxWidth >= DEFAULT_MAX_SEARCH_WIDTH)
                SearchColumn.Width = new(DEFAULT_MAX_SEARCH_WIDTH);
        });

        LoadSettings();
        InitFileOpColumns();

        SettingsHelper.CheckForUpdates();
    }

    private void DeviceListSetup(string selectedAddress = "")
    {
        Task.Run(() => ADBService.GetDevices()).ContinueWith((t) => Dispatcher.Invoke(() => DeviceListSetup(t.Result.Select(l => new LogicalDeviceViewModel(l)), selectedAddress)));
    }

    private void DeviceListSetup(IEnumerable<LogicalDeviceViewModel> devices, string selectedAddress = "")
    {
        var init = !DevicesObject.UpdateDevices(devices);
        FilterDevices();

        if (DevicesObject.Current is null || DevicesObject.Current.IsOpen && DevicesObject.Current.Status is not AbstractDevice.DeviceStatus.Ok)
        {
            DriveHelper.ClearDrives();
            DevicesObject.SetOpenDevice((LogicalDeviceViewModel)null);
        }

        if (!DevicesObject.DevicesAvailable())
        {
            ClearExplorer();
            NavHistory.Reset();
            DriveHelper.ClearDrives();
            return;
        }

        if (DevicesObject.DevicesAvailable(true))
            return;

        if (!Settings.AutoOpen)
        {
            DevicesObject.SetOpenDevice((LogicalDeviceViewModel)null);

            ClearExplorer();
            FileActions.IsExplorerVisible = false;
            NavHistory.Reset();
            return;
        }

        if (!DevicesObject.SetOpenDevice(selectedAddress))
            return;

        if (!ConnectTimer.IsEnabled)
            RuntimeSettings.IsDevicesPaneOpen = false;

        DevicesObject.SetOpenDevice(DevicesObject.Current);
        CurrentADBDevice = new(DevicesObject.Current);
        InitLister();
        if (init)
            InitDevice();
    }

    private void FilterDevices() => DeviceHelper.FilterDevices(CollectionViewSource.GetDefaultView(DevicesList.ItemsSource));

    private void InitLister()
    {
        DirList = new(Dispatcher, CurrentADBDevice, FileHelper.ListerFileManipulator);
        DirList.PropertyChanged += DirectoryLister_PropertyChanged;
    }

    private void LoadSettings()
    {
        Dispatcher.Invoke(() => SettingsHelper.SetSymbolFont());

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
            UpdateFileActions();

            FilterDevices();
        });
    }

    

    

    private void SetRenderMode() => Dispatcher.Invoke(() =>
    {
        if (Settings.SwRender)
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        else if (RenderOptions.ProcessRenderMode == RenderMode.SoftwareOnly)
            RenderOptions.ProcessRenderMode = RenderMode.Default;
    });

    private ApplicationTheme AppThemeToActual(AppTheme appTheme) => appTheme switch
    {
        AppTheme.light => ApplicationTheme.Light,
        AppTheme.dark => ApplicationTheme.Dark,
        AppTheme.windowsDefault => themeService.WindowsTheme,
        _ => throw new NotSupportedException(),
    };

    private IEnumerable<CheckBox> FileOpContextItems
    {
        get
        {
            var items = ((ContextMenu)FindResource("FileOpHeaderContextMenu")).Items;
            return from MenuItem item in ((ContextMenu)FindResource("FileOpHeaderContextMenu")).Items
                   let checkbox = item.Header as CheckBox
                   select checkbox;
        }
    }

    private void InitFileOpColumns() => Dispatcher.Invoke(() =>
    {
        var fileOpContext = FindResource("FileOpHeaderContextMenu") as ContextMenu;
        foreach (var item in fileOpContext.Items)
        {
            var checkbox = ((MenuItem)item).Header as CheckBox;
            checkbox.Click += ColumnCheckbox_Click;

            var config = Storage.Retrieve<FileOpColumn>(checkbox.Name);
            if (config is null)
                continue;

            var column = GetCheckboxColumn(checkbox);
            checkbox.DataContext = config;
            checkbox.IsChecked = config.IsVisible;
            column.Width = config.Width;
            column.Visibility = Visible(config.IsVisible);
            column.DisplayIndex = config.Index;
        }

        EnableContextItems();
    });

    private DataGridColumn GetCheckboxColumn(CheckBox checkBox)
    {
        return checkBox.Name.Split("FileOpContext")[1].Split("CheckBox")[0] switch
        {
            "OpType" => OpTypeColumn,
            "FileName" => FileNameColumn,
            "Progress" => ProgressColumn,
            "Source" => SourceColumn,
            "Dest" => DestColumn,
            _ => throw new NotSupportedException(),
        };
    }

    private string ColumnName(DataGridColumn column)
    {
        if (column == OpTypeColumn) return "OpTypeColumn";
        if (column == FileNameColumn) return "FileNameColumn";
        if (column == ProgressColumn) return "ProgressColumn";
        if (column == SourceColumn) return "SourceColumn";
        if (column == DestColumn) return "DestColumn";

        return "";
    }

    private CheckBox GetColumnCheckbox(DataGridColumn column)
    {
        return FileOpContextItems.First(cb => cb.Name == $"FileOpContext{ColumnName(column).Split("Column")[0]}CheckBox");
    }

    private void EnableContextItems()
    {
        var visibleColumns = FileOpContextItems.Count(cb => cb.IsChecked == true);
        foreach (var checkbox in FileOpContextItems)
        {
            checkbox.IsEnabled = visibleColumns > 1 ? true : checkbox.IsChecked == false;
        }
    }

    private void ColumnCheckbox_Click(object sender, RoutedEventArgs e)
    {
        var checkbox = sender as CheckBox;
        var column = GetCheckboxColumn(checkbox);

        column.Visibility = Visible(checkbox.IsChecked);
        if (checkbox.DataContext is FileOpColumn config)
        {
            config.IsVisible = checkbox.IsChecked;
        }
        else
        {
            checkbox.DataContext = CreateColumnConfig(column);
        }

        Storage.StoreValue(checkbox.Name, checkbox.DataContext);
        EnableContextItems();
    }

    private static FileOpColumn CreateColumnConfig(DataGridColumn column) => new FileOpColumn()
    {
        Index = column.DisplayIndex,
        IsVisible = column.Visibility == Visibility.Visible,
        Width = column.ActualWidth,
    };

    private void InitDevice()
    {
        SetAndroidVersion();
        RefreshDrives(true);
        DriveViewNav();
        NavHistory.Navigate(NavHistory.SpecialLocation.DriveView);

        FileHelper.ClearCutFiles();
        FilterDrives();

        CurrentDeviceDetailsPanel.DataContext = DevicesObject.Current;
        FileActions.PushPackageEnabled = Settings.EnableApk;

        UpdateFileActions();

#if DEBUG
        TestCurrentOperation();
#endif

        AdbHelper.VerifyProgressRedirection();
    }

    private void SetAndroidVersion()
    {
        var versionTask = Task.Run(async () => await CurrentADBDevice.GetAndroidVersion());
        versionTask.ContinueWith((t) =>
        {
            if (t.IsCanceled)
                return;

            Dispatcher.Invoke(() =>
            {
                DevicesObject.Current.SetAndroidVersion(t.Result);
            });
        });
    }

    private void RefreshLocation()
    {
        if (FileActions.IsDriveViewVisible)
            RefreshDrives(true);
        else
            _navigateToPath(CurrentPath);
    }

    private void DriveViewNav()
    {
        ClearExplorer(false);
        FileActions.IsDriveViewVisible = true;
        PathBox.IsEnabled = true;

        MenuItem button = CreatePathButton(DevicesObject.Current, DevicesObject.Current.Name);
        button.Command = AppActions.List.First(action => action.Name is FileAction.FileActionType.Refresh).Command.Command;
        
        AddPathButton(button);
        TextHelper.SetAltText(PathBox, "");

        DriveList.ItemsSource = DevicesObject.Current.Drives;

        if (DriveList.SelectedIndex > -1)
            SelectionHelper.GetListViewItemContainer(DriveList).Focus();
    }

    private bool InitNavigation(string path = "", bool bfNavigated = false)
    {
        if (path is null)
            return true;

        FolderHelper.CombineDisplayNames();

        var realPath = FolderHelper.FolderExists(string.IsNullOrEmpty(path) ? DEFAULT_PATH : path);
        if (realPath is null)
            return false;

        FileActions.IsDriveViewVisible = false;
        FileActions.IsExplorerVisible = true;
        FileActions.HomeEnabled = true;
        RuntimeSettings.BrowseDrive = null;

        return _navigateToPath(realPath, bfNavigated);
    }

    

    private void UpdateInstallersCount()
    {
        var countTask = Task.Run(() => ADBService.CountPackages(DevicesObject.Current.ID));
        countTask.ContinueWith((t) => Dispatcher.Invoke(() =>
        {
            if (!t.IsCanceled && DevicesObject.Current is not null)
            {
                var temp = DevicesObject.Current.Drives.Find(d => d.Type is AbstractDrive.DriveType.Temp);
                ((VirtualDriveViewModel)temp)?.SetItemsCount((long)t.Result);
            }
        }));
    }

    private void UpdatePackages(bool updateExplorer = false)
    {
        FileActions.ListingInProgress = true;

        var version = DevicesObject.Current.AndroidVersion;
        var packageTask = Task.Run(() => ShellFileOperation.GetPackages(CurrentADBDevice, Settings.ShowSystemPackages, version is not null && version >= MIN_PKG_UID_ANDROID_VER));

        packageTask.ContinueWith((t) =>
        {
            if (t.IsCanceled)
                return;

            Dispatcher.Invoke(() =>
            {
                Packages = t.Result;
                if (updateExplorer)
                {
                    ExplorerGrid.ItemsSource = Packages;
                    FilterHiddenFiles();
                }

                if (!updateExplorer && DevicesObject.Current is not null)
                {
                    var package = DevicesObject.Current.Drives.Find(d => d.Type is AbstractDrive.DriveType.Package);
                    ((VirtualDriveViewModel)package)?.SetItemsCount(Packages.Count);
                }

                FileActions.ListingInProgress = false;
            });
        });
    }

    private void ListDevices(IEnumerable<LogicalDevice> devices)
    {
        RuntimeSettings.LastServerResponse = DateTime.Now;

        if (devices is null)
            return;

        var deviceVMs = devices.Select(d => new LogicalDeviceViewModel(d));

        if (!DevicesObject.DevicesChanged(deviceVMs))
            return;

        DeviceListSetup(deviceVMs);

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
                    Dispatcher.Invoke(() => RefreshDrives(true));
                }
                catch (Exception)
                { }
            }

            if (RuntimeSettings.IsDevicesPaneOpen)
                DeviceHelper.UpdateDevicesRootAccess();

            connectTimerMutex.ReleaseMutex();
        });
    }

    private void RefreshDevices()
    {
        Dispatcher.BeginInvoke(new Action<IEnumerable<LogicalDevice>>(ListDevices), ADBService.GetDevices()).Wait();

        if (!RuntimeSettings.IsDevicesPaneOpen)
            return;

        Dispatcher.Invoke(() => DevicesObject.UpdateLogicalIp());

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

        TextHelper.SetAltText(PathBox, realPath);
        CurrentPath = realPath;
        PopulateButtons(realPath);
        ParentPath = CurrentADBDevice.TranslateDeviceParentPath(CurrentPath);

        FileActions.IsRecycleBin = CurrentPath == RECYCLE_PATH;
        FileActions.IsAppDrive = CurrentPath == PACKAGE_PATH;
        FileActions.IsTemp = CurrentPath == TEMP_PATH;
        FileActions.ParentEnabled = CurrentPath != ParentPath && !FileActions.IsRecycleBin && !FileActions.IsAppDrive;
        FileActions.PasteEnabled = IsPasteEnabled();
        FileActions.PushPackageEnabled = Settings.EnableApk;
        FileActions.InstallPackageEnabled = FileActions.IsTemp;
        FileActions.UninstallPackageEnabled = false;
        FileActions.ContextPushPackagesEnabled =
        FileActions.UninstallVisible = FileActions.IsAppDrive;

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
            var recycleTask = Task.Run(() =>
            {
                string text = "";
                try
                {
                    text = ShellFileOperation.ReadAllText(CurrentADBDevice, RECYCLE_INDEX_PATH);
                    if (string.IsNullOrEmpty(text))
                        throw new Exception();
                }
                catch (Exception)
                {
                    try
                    {
                        text = ShellFileOperation.ReadAllText(CurrentADBDevice, RECYCLE_INDEX_BACKUP_PATH);
                    }
                    catch (Exception)
                    { }
                }

                if (!string.IsNullOrEmpty(text))
                {
                    var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var item in lines)
                    {
                        if (!RecycleIndex.Any(indexer => indexer.ToString() == item))
                            RecycleIndex.Add(new TrashIndexer(item));
                    }
                }
            });

            recycleTask.ContinueWith((t) => DirList.Navigate(realPath));

            DateColumn.Header = S_DATE_DEL_COL;
            FileActions.DeleteAction.Value = S_EMPTY_TRASH;
            FileActions.RestoreAction.Value = S_RESTORE_ALL;
        }
        else
        {
            if (FileActions.IsAppDrive)
            {
                UpdatePackages(true);
                UpdateFileActions();
                return true;
            }

            DirList.Navigate(realPath);

            DateColumn.Header = S_DATE_MOD_COL;
            FileActions.DeleteAction.Value = S_DELETE_ACTION;
        }

        ExplorerGrid.ItemsSource = DirList.FileList;
        FilterHiddenFiles();
        UpdateFileActions();
        return true;
    }

    private void PopulateButtons(string path)
    {
        if (string.IsNullOrEmpty(path))
            return;

        var expectedLength = 0.0;
        List<MenuItem> tempButtons = new();
        List<string> pathItems = new();

        var pairs = CurrentDisplayNames.Where(kv => path.StartsWith(kv.Key));
        var specialPair = pairs.Count() > 1 ? pairs.OrderBy(kv => kv.Key.Length).Last() : pairs.First();
        if (specialPair.Key != null)
        {
            MenuItem button = CreatePathButton(specialPair);
            tempButtons.Add(button);
            pathItems.Add(specialPair.Key);
            path = path[specialPair.Key.Length..].TrimStart('/');
            expectedLength = ControlSize.GetWidth(button);
        }

        var dirs = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var dir in dirs)
        {
            pathItems.Add(dir);
            var dirPath = string.Join('/', pathItems).Replace("//", "/");
            MenuItem button = CreatePathButton(dirPath, dir);
            tempButtons.Add(button);
            expectedLength += ControlSize.GetWidth(button);
        }

        expectedLength += (tempButtons.Count - 1) * ControlSize.GetWidth(CreatePathArrow());

        int i = 0;
        for (; i < PathButtons.Count && i < tempButtons.Count; i++)
        {
            var oldB = PathButtons[i];
            var newB = tempButtons[i];
            if (oldB.Header.ToString() != newB.Header.ToString() ||
                TextHelper.GetAltObject(oldB).ToString() != TextHelper.GetAltObject(newB).ToString())
            {
                break;
            }
        }
        PathButtons.RemoveRange(i, PathButtons.Count - i);
        PathButtons.AddRange(tempButtons.GetRange(i, tempButtons.Count - i));

        ConsolidateButtons(expectedLength);
    }

    private void ConsolidateButtons(double expectedLength)
    {
        if (expectedLength > PathBox.ActualWidth)
            expectedLength += ControlSize.GetWidth(CreateExcessButton());

        double excessLength = expectedLength - PathBox.ActualWidth;
        List<MenuItem> excessButtons = new();
        PathMenu.Items.Clear();

        if (excessLength > 0)
        {
            int i = 0;
            while (excessLength >= 0 && PathButtons.Count - excessButtons.Count > 1)
            {
                var path = TextHelper.GetAltObject(PathButtons[i]).ToString();
                var drives = DevicesObject.Current.Drives.Where(drive => drive.Path == path);
                var icon = "\uE8B7";
                if (drives.Any())
                    icon = drives.First().DriveIcon;

                excessButtons.Add(PathButtons[i]);
                PathButtons[i].ContextMenu = null;
                PathButtons[i].Height = double.NaN;
                PathButtons[i].Padding = new(10, 4, 10, 4);
                PathButtons[i].Icon = new FontIcon() { Glyph = icon, Style = FindResource("GlyphFont") as Style };

                PathButtons[i].Margin = Settings.UseFluentStyles ? new(5, 1, 5, 1) : new(0);
                ControlHelper.SetCornerRadius(PathButtons[i], new(Settings.UseFluentStyles ? 4 : 0));

                excessLength -= ControlSize.GetWidth(PathButtons[i]);

                i++;
            }

            AddExcessButton(excessButtons);
        }

        foreach (var item in PathButtons.Except(excessButtons))
        {
            if (PathMenu.Items.Count > 0)
                AddPathArrow();

            AddPathButton(item);
        }

        if (excessLength > 0)
        {
            var width = PathButtons[^1].ActualWidth - (ControlSize.GetWidth(PathMenu) - PathBox.ActualWidth) - 4;
            if (width < 0)
                width = 0;

            PathButtons[^1].Width = width;
        }
        else
            PathButtons[^1].Width = double.NaN;
    }

    private MenuItem CreateExcessButton()
    {
        var menuItem = new MenuItem()
        {
            VerticalAlignment = VerticalAlignment.Center,
            Height = 24,
            Padding = new(10, 4, 10, 4),
            Margin = new(0),
            Header = new FontIcon()
            {
                Glyph = "\uE712",
                FontSize = 18,
                Style = FindResource("GlyphFont") as Style,
            },
        };

        return menuItem;
    }

    private void AddExcessButton(List<MenuItem> excessButtons = null)
    {
        if (excessButtons is not null && !excessButtons.Any())
            return;

        var button = CreateExcessButton();
        button.ItemsSource = excessButtons;

        PathMenu.Items.Add(button);
    }

    private MenuItem CreatePathButton(KeyValuePair<string, string> kv) => CreatePathButton(kv.Key, kv.Value);
    private MenuItem CreatePathButton(object path, string name)
    {
        MenuItem button = new()
        {
            Header = new TextBlock() { Text = name, Margin = new(0, 0, 0, 1), TextTrimming = TextTrimming.CharacterEllipsis },
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new(0),
            Padding = new(8, 0, 8, 0),
            Height = 24,
        };
        button.Click += PathButton_Click;
        TextHelper.SetAltObject(button, path);

        return button;
    }

    private void AddPathButton(MenuItem button)
    {
        if (TextHelper.GetAltObject(button) is string str && str == RECYCLE_PATH)
            button.ContextMenu = null;

        button.Height = 24;
        button.Padding = new(8, 0, 8, 0);
        button.Margin = new(0);

        ControlHelper.SetCornerRadius(button, new(Settings.UseFluentStyles ? 3 : 0));

        button.ContextMenu = Resources["PathButtonsMenu"] as ContextMenu;

        PathMenu.Items.Add(button);
    }

    private MenuItem CreatePathArrow() => new()
    {
        VerticalAlignment = VerticalAlignment.Center,
        Height = 24,
        Margin = new(0),
        Padding = new(3, 0, 3, 0),
        IsEnabled = false,
        Header = new FontIcon()
        {
            Glyph = "\uE970",
            FontSize = 8,
            Style = FindResource("GlyphFont") as Style,
        },
    };

    private void AddPathArrow(bool append = true)
    {
        var arrow = CreatePathArrow();

        if (append)
            PathMenu.Items.Add(arrow);
        else
            PathMenu.Items.Insert(0, arrow);
    }

    private void PathButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item)
        {
            if (TextHelper.GetAltObject(item) is string path and not "")
                NavigateToPath(path);
            else if (TextHelper.GetAltObject(item) is LogicalDevice)
                RefreshDrives(true);
        }
    }

    private void NavigateToLocation(object location, bool bfNavigated = false)
    {
        RecycleIndex.Clear();
        SelectionHelper.SetIsMenuOpen(ExplorerGrid.ContextMenu, false);

        if (location is string path)
        {
            if (!FileActions.IsExplorerVisible)
                InitNavigation(path, bfNavigated);
            else
                NavigateToPath(path, bfNavigated);
        }
        else if (location is NavHistory.SpecialLocation special)
        {
            switch (special)
            {
                case NavHistory.SpecialLocation.DriveView:
                    FileActions.IsRecycleBin = false;
                    UnfocusPathBox();
                    RefreshDrives();
                    DriveViewNav();

                    UpdateFileActions();
                    break;
                default:
                    throw new NotSupportedException();
            }
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
        if (key == Key.Enter)
        {
            if (IsInEditMode())
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
            DeleteFiles();
        }
        else if (key is Key.Up or Key.Down)
        {
            if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
                ExplorerGrid.MultiSelect(key);
            else
                ExplorerGrid.SingleSelect(key);
        }
        else
            return;

        e.Handled = true;
    }

    private bool IsInEditMode()
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

    private void IsInEditMode(bool isEditing) => CellConverter.GetDataGridCell(ExplorerGrid.SelectedCells[1]).IsEditing = isEditing;

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
                if (ExplorerGrid.SelectedCells.Count < 1 || IsInEditMode())
                    return false;

                if (ExplorerGrid.SelectedItems.Count == 1 && ((FilePath)ExplorerGrid.SelectedItem).IsDirectory)
                    DoubleClick(ExplorerGrid.SelectedItem);
                break;
            default:
                return false;
        }

        return true;
    }

    private void PullFiles(bool quick = false)
    {
        int itemsCount = selectedFiles.Count();
        ShellObject path;

        if (quick)
        {
            path = ShellObject.FromParsingName(Settings.DefaultFolder);
        }
        else
        {
            var dialog = new CommonOpenFileDialog()
            {
                IsFolderPicker = true,
                Multiselect = false,
                DefaultDirectory = Settings.DefaultFolder,
                Title = S_ITEMS_DESTINATION(itemsCount > 1, ExplorerGrid.SelectedItem),
            };

            if (dialog.ShowDialog() != CommonFileDialogResult.Ok)
                return;

            path = dialog.FileAsShellObject;
        }

        var dirPath = new FilePath(path);

        if (!Directory.Exists(path.ParsingName))
        {
            try
            {
                Directory.CreateDirectory(path.ParsingName);
            }
            catch (Exception e)
            {
                DialogService.ShowMessage(e.Message, S_DEST_ERR, DialogService.DialogIcon.Critical);
                return;
            }
        }

        foreach (FileClass item in ExplorerGrid.SelectedItems)
        {
            FileOpQ.AddOperation(new FilePullOperation(Dispatcher, CurrentADBDevice, item, dirPath));
        }
    }

    private void PushItems(bool isFolderPicker, bool isContextMenu)
    {
        FilePath targetPath;
        if (isContextMenu && selectedFiles.Count() == 1)
            targetPath = (FilePath)ExplorerGrid.SelectedItem;
        else
            targetPath = new(CurrentPath);

        var dialog = new CommonOpenFileDialog()
        {
            IsFolderPicker = isFolderPicker,
            Multiselect = true,
            DefaultDirectory = Settings.DefaultFolder,
            Title = S_PUSH_BROWSE_TITLE(isFolderPicker, targetPath.FullPath == CurrentPath ? "" : targetPath.FullName),
        };

        if (dialog.ShowDialog() != CommonFileDialogResult.Ok)
            return;

        foreach (var item in dialog.FilesAsShellObject)
        {
            var pushOpeartion = new FilePushOperation(Dispatcher, CurrentADBDevice, new FilePath(item), targetPath);
            pushOpeartion.PropertyChanged += PushOpeartion_PropertyChanged;
            FileOpQ.AddOperation(pushOpeartion);
        }
    }

    private void PushOpeartion_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        var pushOperation = sender as FilePushOperation;

        // If operation completed now and current path is where the new file was pushed to and it is not shown yet
        if ((e.PropertyName == "Status") &&
            (pushOperation.Status == FileOperation.OperationStatus.Completed) &&
            (pushOperation.TargetPath.FullPath == CurrentPath) &&
            (!DirList.FileList.Any(f => f.FullName == pushOperation.FilePath.FullName)))
        {
            DirList.FileList.Add(FileClass.FromWindowsPath(pushOperation.TargetPath, pushOperation.FilePath));
        }
    }

    private void TestCurrentOperation()
    {
        //fileOperationQueue.Clear();
        //fileOperationQueue.AddOperation(InProgressTestOperation.CreateProgressStart(Dispatcher, CurrentADBDevice, "File.exe"));
        //fileOperationQueue.AddOperation(InProgressTestOperation.CreateFileInProgress(Dispatcher, CurrentADBDevice, "File.exe"));
        //fileOperationQueue.AddOperation(InProgressTestOperation.CreateFolderInProgress(Dispatcher, CurrentADBDevice, "Folder"));
    }

    private void TestDevices()
    {
        //ConnectTimer.IsEnabled = false;

        //DevicesObject.UpdateServices(new List<ServiceDevice>() { new PairingService("sdfsdfdsf_adb-tls-pairing._tcp.", "192.168.1.20", "5555") { MdnsType = ServiceDevice.ServiceType.PairingCode } });
        //DevicesObject.UpdateDevices(new List<LogicalDevice>() { LogicalDevice.New("Test", "test.ID", "device") });
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

    private async void ConnectNewDevice()
    {
        var dev = (NewDeviceViewModel)RuntimeSettings.ConnectNewDevice;
        await Task.Run(() =>
        {
            try
            {
                ADBService.ConnectNetworkDevice(dev.ConnectAddress);
                return true;
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains(S_FAILED_CONN + dev.ConnectAddress) && dev.Type is AbstractDevice.DeviceType.New && ((NewDeviceViewModel)RuntimeSettings.ConnectNewDevice).IsPairingEnabled)
                {
                    DevicesObject.NewDevice.EnablePairing();
                }
                else
                    Dispatcher.Invoke(() => DialogService.ShowMessage(ex.Message, S_FAILED_CONN_TITLE, DialogService.DialogIcon.Critical));

                return false;
            }
        }).ContinueWith((t) =>
        {
            if (t.IsCanceled)
                return;

            Dispatcher.Invoke(() =>
            {
                if (t.Result)
                {
                    string newDeviceAddress = "";
                    if (RuntimeSettings.ConnectNewDevice.Type is AbstractDevice.DeviceType.New)
                    {
                        if (Settings.SaveDevices)
                            DevicesObject.AddHistoryDevice((HistoryDeviceViewModel)dev);

                        newDeviceAddress = dev.ConnectAddress;
                        ((NewDeviceViewModel)RuntimeSettings.ConnectNewDevice).ClearDevice();
                    }
                    else
                    {
                        newDeviceAddress = ((HistoryDeviceViewModel)RuntimeSettings.ConnectNewDevice).ConnectAddress;

                        // In case user has changed the port of the history device
                        if (Settings.SaveDevices)
                            DevicesObject.StoreHistoryDevices();
                    }


                    DeviceHelper.CollapseDevices();
                    DeviceListSetup(newDeviceAddress);
                }

                RuntimeSettings.ConnectNewDevice = null;
                RuntimeSettings.IsManualPairingInProgress = false;
            });
        });
    }

    private async void PairNewDevice()
    {
        var dev = (NewDeviceViewModel)RuntimeSettings.ConnectNewDevice;
        await Task.Run(() =>
        {
            try
            {
                ADBService.PairNetworkDevice(dev.PairingAddress, dev.PairingCode);
                return true;
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => DialogService.ShowMessage(ex.Message, S_PAIR_ERR_TITLE, DialogService.DialogIcon.Critical));
                return false;
            }
        }).ContinueWith((t) =>
        {
            if (t.IsCanceled)
                return;

            Dispatcher.Invoke(() =>
            {
                if (t.Result)
                    ConnectNewDevice();

                RuntimeSettings.ConnectNewDevice = null;
                RuntimeSettings.IsManualPairingInProgress = false;
            });
        });
    }

    private void ClearExplorer(bool clearDevice = true)
    {
        PathMenu.Items.Clear();
        DirList?.FileList?.Clear();
        Packages.Clear();
        FileActions.PushFilesFoldersEnabled =
        FileActions.PullEnabled =
        FileActions.DeleteEnabled =
        FileActions.RenameEnabled =
        FileActions.HomeEnabled =
        FileActions.NewEnabled =
        FileActions.PasteEnabled =
        FileActions.UninstallVisible =
        FileActions.CutEnabled =
        FileActions.CopyEnabled =
        FileActions.IsExplorerVisible =
        FileActions.PackageActionsEnabled =
        FileActions.IsCopyItemPathEnabled =
        FileActions.UpdateModifiedEnabled =
        FileActions.ParentEnabled = false;

        FileActions.PushPackageEnabled = Settings.EnableApk;

        FileActions.ExplorerFilter = "";

        if (clearDevice)
        {
            CurrentDisplayNames.Clear();
            CurrentPath = null;
            CurrentDeviceDetailsPanel.DataContext = null;
            TextHelper.SetAltText(PathBox, "");
            FileActions.PushPackageEnabled =
            PathBox.IsEnabled = false;
        }

        FilterFileActions();
    }

    private void ExplorerGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var point = Mouse.GetPosition(ExplorerGrid);
        if (point.Y < ColumnHeaderHeight)
            e.Handled = true;

        SelectionHelper.SetIsMenuOpen(ExplorerGrid.ContextMenu, true);
        UpdateFileActions();
    }

    private void ExplorerGrid_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var point = e.GetPosition(ExplorerGrid);
        var actualRowWidth = 0.0;
        int selectionIndex;

        foreach (var item in ExplorerGrid.Columns.Where(col => col.Visibility == Visibility.Visible))
        {
            actualRowWidth += item.ActualWidth;
        }

        if (point.Y > (ExplorerGrid.Items.Count * ExplorerGrid.MinRowHeight + ColumnHeaderHeight)
            || point.Y > (ExplorerGrid.ActualHeight - ExplorerContentPresenter.ActualHeight % ExplorerGrid.MinRowHeight)
            || point.Y < ColumnHeaderHeight + ScrollContentPresenterMargin
            || point.X > actualRowWidth
            || point.X > DataGridContentWidth)
        {
            if (ExplorerGrid.SelectedItems.Count > 0 && IsInEditMode())
                IsInEditMode(false);

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
        PopulateButtons(TextHelper.GetAltText(PathBox));
        ResizeDetailedView();
        EnableSplitViewAnimation();
        SearchBoxMaxWidth();
    }

    private void SearchBoxMaxWidth() => RuntimeSettings.MaxSearchBoxWidth = WindowState switch
    {
        WindowState.Maximized => SystemParameters.PrimaryScreenWidth * WIDE_WINDOW_SEARCH_WIDTH,
        WindowState.Normal when Width > MIN_WINDOW_WIDTH_FOR_SEARCH_RATIO => Width * WIDE_WINDOW_SEARCH_WIDTH,
        _ => Width * MAX_SEARCH_WIDTH_RATIO,
    };

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

    private void FileOperationsButton_Click(object sender, RoutedEventArgs e)
    {
        RuntimeSettings.IsOperationsViewOpen = !RuntimeSettings.IsOperationsViewOpen;
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

    private void RefreshDrives(bool asyncClasify = false)
    {
        if (DevicesObject.Current is null)
            return;
        
        if (!asyncClasify && DevicesObject.Current.Drives?.Count > 0 && !FileActions.IsExplorerVisible)
            asyncClasify = true;

        var dispatcher = Dispatcher;
        var driveTask = Task.Run(() =>
        {
            if (CurrentADBDevice is null)
                return null;

            var drives = CurrentADBDevice.GetDrives();

            if (DevicesObject.Current.Drives.Any(d => d.Type is AbstractDrive.DriveType.Trash))
                TrashHelper.UpdateRecycledItemsCount();

            if (DevicesObject.Current.Drives.Any(d => d.Type is AbstractDrive.DriveType.Temp))
                UpdateInstallersCount();

            if (DevicesObject.Current.Drives.Any(d => d.Type is AbstractDrive.DriveType.Package))
                UpdatePackages();

            return drives;
        });
        driveTask.ContinueWith((t) =>
        {
            if (t.IsCanceled || t.Result is null)
                return;

            dispatcher.Invoke(async () =>
            {
                var update = await DevicesObject.Current?.UpdateDrives(await t, dispatcher, asyncClasify);
                if (update is true)
                    FilterDrives();
            });
        });
    }

    private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is ScrollViewer sv)
        {
            sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta);
            e.Handled = true;
        }
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
        UnfocusPathBox();
        DriveHelper.ClearSelectedDrives();
    }

    private void FilterHiddenFiles()
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
                ? new(pkg => pkg.ToString().Contains(FileActions.ExplorerFilter, StringComparison.OrdinalIgnoreCase))
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

    private void RemovePending_Click(object sender, RoutedEventArgs e)
    {
        FileOpQ.ClearPending();
    }

    private void RemoveCompleted_Click(object sender, RoutedEventArgs e)
    {
        FileOpQ.ClearCompleted();
    }

    private void OpenDefaultFolder_Click(object sender, RoutedEventArgs e)
    {
        Process.Start("explorer.exe", Settings.DefaultFolder);
    }

    private void RemovePendingAndCompleted_Click(object sender, RoutedEventArgs e)
    {
        FileOpQ.Clear();
    }

    private void StopFileOperations_Click(object sender, RoutedEventArgs e)
    {
        if (FileOpQ.IsActive)
            FileOpQ.Stop();
        else
            FileOpQ.Start();
    }

    private void Window_SourceInitialized(object sender, EventArgs e)
    {
        if (Storage.RetrieveBool(SystemVals.windowMaximized) == true)
            WindowState = WindowState.Maximized;

        if (Storage.RetrieveBool(SystemVals.detailedVisible) is bool and true)
        {
            RuntimeSettings.IsOperationsViewOpen = true;
        }

        if (double.TryParse(Storage.RetrieveValue(SystemVals.detailedHeight)?.ToString(), out double detailedHeight))
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

        var checkbox = GetColumnCheckbox(column);

        if (checkbox.DataContext is FileOpColumn config)
        {
            config.Index = column.DisplayIndex;
        }
        else
        {
            checkbox.DataContext = CreateColumnConfig(column);
        }

        Storage.StoreValue(checkbox.Name, checkbox.DataContext);
    }

    private void DataGridColumnHeader_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (((DataGridColumnHeader)sender).Column is not DataGridColumn column)
            return;

        var checkbox = GetColumnCheckbox(column);

        if (checkbox.DataContext is FileOpColumn config)
        {
            config.Width = e.NewSize.Width;
        }
        else
        {
            checkbox.DataContext = CreateColumnConfig(column);
        }

        Storage.StoreValue(checkbox.Name, checkbox.DataContext);
    }

    private async void DeleteFiles()
    {
        IEnumerable<FileClass> itemsToDelete;
        if (FileActions.IsRecycleBin && !selectedFiles.Any())
        {
            itemsToDelete = DirList.FileList.Where(f => !RECYCLE_INDEX_PATHS.Contains(f.FullPath));
        }
        else
        {
            itemsToDelete = DevicesObject.Current.Root != AbstractDevice.RootStatus.Enabled
                    ? selectedFiles.Where(file => file.Type is FileType.File or FileType.Folder) : selectedFiles;
        }

        string deletedString;
        if (itemsToDelete.Count() == 1)
            deletedString = FileHelper.DisplayName(itemsToDelete.First());
        else
        {
            deletedString = $"{itemsToDelete.Count()} ";
            if (itemsToDelete.All(item => item.IsDirectory))
                deletedString += "folders";
            else if (itemsToDelete.All(item => !item.IsDirectory))
                deletedString += "files";
            else
                deletedString += "items";
        }

        var result = await DialogService.ShowConfirmation(
            S_DELETE_CONF(FileActions.IsRecycleBin, deletedString),
            S_DEL_CONF_TITLE,
            S_DELETE_ACTION,
            checkBoxText: Settings.EnableRecycle && !FileActions.IsRecycleBin ? S_PERM_DEL : "",
            icon: DialogService.DialogIcon.Delete);

        if (result.Item1 is not ContentDialogResult.Primary)
            return;

        if (!FileActions.IsRecycleBin && Settings.EnableRecycle && !result.Item2)
        {
            ShellFileOperation.MoveItems(CurrentADBDevice, itemsToDelete, RECYCLE_PATH, CurrentPath, DirList.FileList, Dispatcher, DevicesObject.Current);
        }
        else
        {
            ShellFileOperation.DeleteItems(CurrentADBDevice, itemsToDelete, DirList.FileList, Dispatcher);

            if (FileActions.IsRecycleBin)
            {
                TrashHelper.EnableRecycleButtons(DirList.FileList.Except(itemsToDelete));
                if (!selectedFiles.Any() && DirList.FileList.Any(item => RECYCLE_INDEX_PATHS.Contains(item.FullPath)))
                {
                    _ = Task.Run(() => ShellFileOperation.SilentDelete(CurrentADBDevice, DirList.FileList.Where(item => RECYCLE_INDEX_PATHS.Contains(item.FullPath))));
                }
            }
        }
    }

    private void BeginRename()
    {
        var cell = CellConverter.GetDataGridCell(ExplorerGrid.SelectedCells[1]);

        cell.IsEditing = !cell.IsEditing;
    }

    

    private void NameColumnEdit_Loaded(object sender, RoutedEventArgs e)
    {
        var textBox = sender as TextBox;
        TextHelper.SetAltObject(textBox, FileHelper.GetFromCell(ExplorerGrid.SelectedCells[1]));
        textBox.Focus();
    }

    

    private void NameColumnEdit_LostFocus(object sender, RoutedEventArgs e)
    {
        Rename(sender as TextBox);
    }

    private void Rename(TextBox textBox)
    {
        FileClass file = TextHelper.GetAltObject(textBox) as FileClass;
        var name = FileHelper.DisplayName(textBox);
        if (file.IsTemp)
        {
            if (string.IsNullOrEmpty(textBox.Text))
            {
                DirList.FileList.Remove(file);
                return;
            }
            try
            {
                CreateNewItem(file, textBox.Text);
            }
            catch (Exception e)
            {
                if (e is NotImplementedException)
                    throw;
            }
        }
        else if (!string.IsNullOrEmpty(textBox.Text) && textBox.Text != name)
        {
            try
            {
                FileHelper.RenameFile(textBox.Text, file);
            }
            catch (Exception)
            { }
        }
    }

    private void NameColumnEdit_KeyDown(object sender, KeyEventArgs e)
    {
        var cell = CellConverter.GetDataGridCell(ExplorerGrid.SelectedCells[1]);
        var textBox = sender as TextBox;

        if (e.Key == Key.Enter)
            e.Handled = true;
        else if (e.Key == Key.Escape)
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
        }
        else
            return;

        if (ExplorerGrid.SelectedCells.Count > 0)
            cell.IsEditing = false;
    }

    private void NameColumnCell_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;

        var cell = sender as DataGridCell;
        if (cell.IsEditing)
            return;

        e.Handled = true;
        clickCount = e.ClickCount;

        if (clickCount > 1)
        {
            DoubleClick(cell.DataContext);
        }
        else
        {
            UnfocusPathBox();
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
                if (selectedFiles is null || selectedFiles.Count() > 1)
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
                        if (e.LeftButton == MouseButtonState.Released && selectedFiles.Count() == 1)
                            cell.IsEditing = true;
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

    private void CutFiles(IEnumerable<FileClass> items, bool isCopy = false)
    {
        FileHelper.ClearCutFiles();
        FileActions.PasteState = isCopy ? FileClass.CutType.Copy : FileClass.CutType.Cut;

        var itemsToCut = DevicesObject.Current.Root is not AbstractDevice.RootStatus.Enabled
                    ? items.Where(file => file.Type is FileType.File or FileType.Folder) : items;

        foreach (var item in itemsToCut)
        {
            item.CutState = FileActions.PasteState;
        }

        CutItems.AddRange(itemsToCut);

        FileActions.CopyEnabled = !isCopy;
        FileActions.CutEnabled = isCopy;

        FileActions.PasteEnabled = IsPasteEnabled();
        FileActions.IsKeyboardPasteEnabled = IsPasteEnabled(true, true);

        FilterFileActions();
    }

    private async void PasteFiles()
    {
        var firstSelectedFile = selectedFiles.Any() ? selectedFiles.First() : null;
        var targetName = "";
        var targetPath = "";

        if (selectedFiles.Count() != 1 || (FileActions.PasteState is FileClass.CutType.Copy && CutItems[0].Relation(firstSelectedFile) is RelationType.Self))
        {
            targetPath = CurrentPath;
            targetName = CurrentPath[CurrentPath.LastIndexOf('/')..];
        }
        else
        {
            targetPath = ((FileClass)ExplorerGrid.SelectedItem).FullPath;
            targetName = FileHelper.DisplayName((FilePath)ExplorerGrid.SelectedItem);
        }

        var pasteItems = CutItems.Where(f => f.Relation(targetPath) is not (RelationType.Self or RelationType.Descendant));
        await Task.Run(() => ShellFileOperation.MoveItems(FileActions.PasteState is FileClass.CutType.Copy,
                                                          targetPath,
                                                          pasteItems,
                                                          targetName,
                                                          DirList.FileList,
                                                          Dispatcher,
                                                          CurrentADBDevice,
                                                          CurrentPath));

        if (FileActions.PasteState is FileClass.CutType.Cut)
            FileHelper.ClearCutFiles(pasteItems);

        FileActions.PasteEnabled = IsPasteEnabled();

        FilterFileActions();
    }

    private void NewItem(bool isFolder)
    {
        var namePrefix = S_NEW_ITEM(isFolder);
        var index = FileClass.ExistingIndexes(DirList.FileList, namePrefix);

        FileClass newItem = new($"{namePrefix}{index}", CurrentPath, isFolder ? FileType.Folder : FileType.File, isTemp: true);
        DirList.FileList.Insert(0, newItem);

        ExplorerGrid.ScrollIntoView(newItem);
        ExplorerGrid.SelectedItem = newItem;
        var cell = CellConverter.GetDataGridCell(ExplorerGrid.SelectedCells[1]);
        if (cell is not null)
            cell.IsEditing = true;
    }

    private void CreateNewItem(FileClass file, string newName)
    {
        file.UpdatePath($"{CurrentPath}{(CurrentPath == "/" ? "" : "/")}{newName}");

        if (Settings.ShowExtensions)
            file.UpdateType();

        try
        {
            if (file.Type is FileType.Folder)
                ShellFileOperation.MakeDir(CurrentADBDevice, file.FullPath);
            else if (file.Type is FileType.File)
                ShellFileOperation.MakeFile(CurrentADBDevice, file.FullPath);
            else
                throw new NotSupportedException();
        }
        catch (Exception e)
        {
            DialogService.ShowMessage(e.Message, S_CREATE_ERR_TITLE, DialogService.DialogIcon.Critical);
            DirList.FileList.Remove(file);
            throw;
        }

        file.IsTemp = false;
        file.ModifiedTime = DateTime.Now;
        if (file.Type is FileType.File)
            file.Size = 0;

        var index = DirList.FileList.IndexOf(file);
        DirList.FileList.Remove(file);
        DirList.FileList.Insert(index, file);
        ExplorerGrid.SelectedItem = file;
    }

    private void ExplorerGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete)
        {
            e.Handled = true;
            if (FileActions.DeleteEnabled)
                DeleteFiles();
        }
    }

    private void CopyItemPath()
    {
        var path = FileActions.IsAppDrive ? ((Package)ExplorerGrid.SelectedItem).Name : ((FilePath)ExplorerGrid.SelectedItem).FullPath;
        Clipboard.SetText(path);
    }

    private void ClearLogButton_Click(object sender, RoutedEventArgs e)
    {
        ClearLogs();
    }

    private void ClearLogs()
    {
        CommandLog.Clear();
        LogTextBox.Clear();
    }

    private void RefreshDevicesButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshDevices();
    }

    private void RestoreItems()
    {
        var restoreItems = (!selectedFiles.Any() ? DirList.FileList : selectedFiles).Where(file => file.TrashIndex is not null && !string.IsNullOrEmpty(file.TrashIndex.OriginalPath));
        string[] existingItems = Array.Empty<string>();
        List<FileClass> existingFiles = new();
        bool merge = false;

        var restoreTask = Task.Run(() =>
        {
            existingItems = ADBService.FindFiles(CurrentADBDevice.ID, restoreItems.Select(file => file.TrashIndex.OriginalPath));
            if (existingItems?.Any() is true)
            {
                if (restoreItems.Any(item => item.IsDirectory && existingItems.Contains(item.TrashIndex.OriginalPath)))
                    merge = true;

                existingItems = existingItems.Select(path => path[(path.LastIndexOf('/') + 1)..]).ToArray();
            }

            foreach (var item in restoreItems)
            {
                if (existingItems.Contains(item.FullName))
                    return;

                if (restoreItems.Count(file => file.FullName == item.FullName && file.TrashIndex.OriginalPath == item.TrashIndex.OriginalPath) > 1)
                {
                    existingItems = existingItems.Append(item.FullName).ToArray();
                    existingFiles.Add(item);
                    if (item.IsDirectory)
                        merge = true;
                }
            }
        });

        restoreTask.ContinueWith((t) =>
        {
            App.Current.Dispatcher.BeginInvoke(async () =>
            {
                if (existingItems.Length is int count and > 0)
                {
                    var result = await DialogService.ShowConfirmation(
                        S_CONFLICT_ITEMS(count),
                        S_RESTORE_CONF_TITLE,
                        primaryText: S_MERGE_REPLACE(merge),
                        secondaryText: count == restoreItems.Count() ? "" : "Skip",
                        cancelText: "Cancel",
                        icon: DialogService.DialogIcon.Exclamation);

                    if (result.Item1 is ContentDialogResult.None)
                    {
                        return;
                    }

                    if (result.Item1 is ContentDialogResult.Secondary)
                    {
                        restoreItems = existingFiles.Count != count
                            ? restoreItems.Where(item => !existingItems.Contains(item.FullName))
                            : restoreItems.Except(existingFiles);
                    }
                }

                ShellFileOperation.MoveItems(device: CurrentADBDevice,
                                         items: restoreItems,
                                         targetPath: null,
                                         currentPath: CurrentPath,
                                         fileList: DirList.FileList,
                                         dispatcher: Dispatcher);

                if (!selectedFiles.Any())
                    TrashHelper.EnableRecycleButtons();
            });
        });
    }

    private void DataGridRow_Unselected(object sender, RoutedEventArgs e)
    {
        ControlHelper.SetCornerRadius(e.OriginalSource as DataGridRow, new(Settings.UseFluentStyles ? 2 : 0));
    }

    private void AndroidRobotLicense_Click(object sender, RoutedEventArgs e)
    {
        HyperlinkButton ccLink = new()
        {
            Content = S_CC_NAME,
            ToolTip = L_CC_LIC,
            NavigateUri = L_CC_LIC,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        HyperlinkButton apacheLink = new()
        {
            Content = S_APACHE_NAME,
            ToolTip = L_APACHE_LIC,
            NavigateUri = L_APACHE_LIC,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        apacheLink.SetValue(Grid.ColumnProperty, 1);

        SimpleStackPanel stack = new()
        {
            Spacing = 8,
            Children =
            {
                new TextBlock()
                {
                    TextWrapping = TextWrapping.Wrap,
                    Text = S_ANDROID_ROBOT_LIC,
                },
                new TextBlock()
                {
                    TextWrapping = TextWrapping.Wrap,
                    Text = S_APK_ICON_LIC,
                },
                new Grid()
                {
                    ColumnDefinitions = { new(), new() },
                    Children = { ccLink, apacheLink },
                },
            },
        };

        DialogService.ShowDialog(stack, S_ANDROID_ICONS_TITLE, DialogService.DialogIcon.Informational);
    }

    private void ExplorerGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        if (FileActions.IsAppDrive)
            return;

        var collectionView = CollectionViewSource.GetDefaultView(ExplorerGrid.ItemsSource);
        var sortDirection = ListHelper.Invert(e.Column.SortDirection);
        e.Column.SortDirection = sortDirection;

        collectionView.SortDescriptions.Clear();
        collectionView.SortDescriptions.Add(new(nameof(FileClass.IsDirectory), ListHelper.Invert(sortDirection)));

        if (e.Column.SortMemberPath != nameof(FileClass.FullName))
            collectionView.SortDescriptions.Add(new(e.Column.SortMemberPath, sortDirection));

        collectionView.SortDescriptions.Add(new(nameof(FileClass.SortName), sortDirection));

        e.Handled = true;
    }

    private void InstallPackages()
    {
        var packages = selectedFiles;

        Task.Run(() =>
        {
            ShellFileOperation.InstallPackages(CurrentADBDevice, packages, Dispatcher);
        });
    }

    private async void UninstallPackages()
    {
        var pkgs = selectedPackages;
        var files = selectedFiles;

        var result = await DialogService.ShowConfirmation(
            S_REM_APK(!FileActions.IsAppDrive, FileActions.IsAppDrive ? pkgs.Count() : files.Count()),
            S_CONF_UNI_TITLE,
            "Uninstall",
        icon: DialogService.DialogIcon.Exclamation);

        if (result.Item1 is not ContentDialogResult.Primary)
            return;

        var packageTask = await Task.Run(() =>
        {
            if (FileActions.IsAppDrive)
                return pkgs.Select(pkg => pkg.Name);

            return files.Select(item => ShellFileOperation.GetPackageName(CurrentADBDevice, item.FullPath));
        });

        ShellFileOperation.UninstallPackages(CurrentADBDevice, packageTask, Dispatcher, Packages);
    }

    private void CopyToTemp()
    {
        _ = ShellFileOperation.MoveItems(true, TEMP_PATH, selectedFiles, FileHelper.DisplayName(selectedFiles.First()), DirList.FileList, Dispatcher, CurrentADBDevice, CurrentPath);
    }

    private void PushPackages()
    {
        var dialog = new CommonOpenFileDialog()
        {
            IsFolderPicker = false,
            Multiselect = true,
            DefaultDirectory = Settings.DefaultFolder,
            Title = S_INSTALL_APK,
        };
        dialog.Filters.Add(new("Android Package", string.Join(';', INSTALL_APK.Select(name => name[1..]))));

        if (dialog.ShowDialog() != CommonFileDialogResult.Ok)
            return;

        Task.Run(() => ShellFileOperation.PushPackages(CurrentADBDevice, dialog.FilesAsShellObject, Dispatcher, FileActions.IsAppDrive));
    }

    private void DefaultFolderSetButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CommonOpenFileDialog()
        {
            IsFolderPicker = true,
            Multiselect = false,
        };
        if (Settings.DefaultFolder != "")
            dialog.DefaultDirectory = Settings.DefaultFolder;

        if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
        {
            Settings.DefaultFolder = dialog.FileName;
        }
    }

    private async void ResetSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var result = await DialogService.ShowConfirmation(
                        S_RESET_SETTINGS,
                        S_RESET_SETTINGS_TITLE,
                        primaryText: "Confirm",
                        cancelText: "Cancel",
                        icon: DialogService.DialogIcon.Exclamation);

        if (result.Item1 == ContentDialogResult.None)
            return;

        RuntimeSettings.ResetAppSettings = true;
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

    private void UpdatedModifiedDates()
    {
        var items = selectedFiles;
        Task.Run(() => ShellFileOperation.ChangeDateFromName(CurrentADBDevice, items, DirList.FileList, Dispatcher));
    }

    private void MainWin_StateChanged(object sender, EventArgs e)
    {
        SearchBoxMaxWidth();
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Escape)
        {
            FileActions.ExplorerFilter = "";
            UnfocusPathBox();
        }
    }

    private void SaveReaderTextButton_Click(object sender, RoutedEventArgs e)
    {
        var file = selectedFiles.First();
        string text = FileActions.EditorText;

        var writeTask = Task.Run(() =>
        {
            ShellFileOperation.SilentDelete(CurrentADBDevice, file);

            ShellFileOperation.WriteLine(CurrentADBDevice, file.FullPath, ADBService.EscapeAdbShellString(text.Replace("\r", ""), '\''));
        });

        writeTask.ContinueWith((t) => Dispatcher.Invoke(() => FileActions.OriginalEditorText = FileActions.EditorText));
    }

    private void OpenEditor()
    {
        if (FileActions.IsEditorOpen)
        {
            FileActions.IsEditorOpen = false;
            return;
        }
        FileActions.IsEditorOpen = true;

        FileActions.EditorFilePath = selectedFiles.First().FullPath;

        var readTask = Task.Run(() =>
        {
            var text = "";
            try
            {
                text = ShellFileOperation.ReadAllText(CurrentADBDevice, FileActions.EditorFilePath);
            }
            catch (Exception)
            { }
            return text;
        });

        readTask.ContinueWith((t) => Dispatcher.Invoke(() =>
        {
            FileActions.EditorText =
            FileActions.OriginalEditorText = t.Result;
        }));
    }

    private void CloseEditorButton_Click(object sender, RoutedEventArgs e)
    {
        FileActions.IsEditorOpen = false;
    }

    private void SettingsSearchBox_GotFocus(object sender, RoutedEventArgs e)
    {
        RuntimeSettings.IsOperationsViewOpen = false;
    }

    private void SortedSettings_MouseMove(object sender, MouseEventArgs e)
    {
        SortedSettings.Focus();
    }

    private void MdnsCheckBox_Click(object sender, RoutedEventArgs e)
    {
        UpdateMdns();
    }
}
