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
    private readonly DispatcherTimer SplashTimer = new();
    private readonly Mutex connectTimerMutex = new();
    private readonly ThemeService themeService = new();
    private int clickCount = 0;
    private int firstSelectedRow = -1;
    public static MDNS MdnsService { get; set; } = new();
    public Devices DevicesObject { get; set; } = new();
    public PairingQrClass QrClass { get; set; }

    private ItemsPresenter _explorerContentPresenter;
    private ItemsPresenter ExplorerContentPresenter
    {
        get
        {
            if (_explorerContentPresenter is null && VisualTreeHelper.GetChild(ExplorerGrid, 0) is Border border && border.Child is ScrollViewer scroller && scroller.Content is ItemsPresenter presenter)
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
    protected virtual bool Set<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
    {
        if (Equals(storage, value))
        {
            return false;
        }

        storage = value;
        OnPropertyChanged(propertyName);

        return true;
    }
    protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public string SelectedFilesTotalSize => (selectedFiles is not null && FileClass.TotalSize(selectedFiles) is ulong size and > 0) ? size.ToSize() : "";

    private IEnumerable<FileClass> selectedFiles => FileActions.IsAppDrive ? null : ExplorerGrid.SelectedItems.OfType<FileClass>();

    private IEnumerable<Package> selectedPackages => FileActions.IsAppDrive ? ExplorerGrid.SelectedItems.OfType<Package>() : null;

    private string prevPath = "";

    public MainWindow()
    {
        InitializeComponent();

        KeyDown += new KeyEventHandler(OnButtonKeyDown);

        fileOperationQueue = new(this.Dispatcher);
        Task launchTask = Task.Run(() => LaunchSequence());

        ConnectTimer.Interval = CONNECT_TIMER_INIT;
        ConnectTimer.Tick += ConnectTimer_Tick;

        ServerWatchdogTimer.Interval = RESPONSE_TIMER_INTERVAL;
        ServerWatchdogTimer.Tick += ServerWatchdogTimer_Tick;

        Settings.PropertyChanged += Settings_PropertyChanged;
        RuntimeSettings.PropertyChanged += RuntimeSettings_PropertyChanged;
        themeService.PropertyChanged += ThemeService_PropertyChanged;
        CommandLog.CollectionChanged += CommandLog_CollectionChanged;
        fileOperationQueue.PropertyChanged += FileOperationQueue_PropertyChanged;
        FileActions.PropertyChanged += FileActions_PropertyChanged;
        SettingsSearchBox.GotFocus += SettingsSearchBox_FocusChanged;
        SettingsSearchBox.LostFocus += SettingsSearchBox_FocusChanged;
        DevicesObject.PropertyChanged += DevicesObject_PropertyChanged;
        DevicesObject.UIList.CollectionChanged += UIList_CollectionChanged;

        Task<bool> versionTask = Task.Run(() => CheckAdbVersion());

        versionTask.ContinueWith((t) =>
        {
            if (!t.IsCanceled && t.Result)
            {
                Dispatcher.Invoke(() =>
                {
                    ConnectTimer.Start();
                    OpenDevicesButton.IsEnabled = true;
                    RuntimeSettings.IsDevicesPaneOpen = true;
                });
            }
        });

        UpperProgressBar.DataContext = fileOperationQueue;
        CurrentOperationDataGrid.ItemsSource = fileOperationQueue.Operations;

#if DEBUG
        TestCurrentOperation();
        TestDevices();
#endif
        if (Settings.EnableSplash)
        {
            SplashTimer.Tick += SplashTimer_Tick;
            SplashTimer.Interval = SPLASH_DISPLAY_TIME;
            SplashTimer.Start();
        }
        else
            SplashScreen.Visible(false);

        Task.Run(() =>
        {
            Task.WaitAll(launchTask, versionTask);
            Settings.WindowLoaded = true;
        });
    }

    private void UIList_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
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
                                ClearDrives();
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
                            throw new NotImplementedException();
                    }

                    RuntimeSettings.DeviceToRemove = null;
                    break;

                case nameof(AppRuntimeSettings.DeviceToPair) when RuntimeSettings.DeviceToPair is not null:
                    var deviceToPair = RuntimeSettings.DeviceToPair;
                    _ = PairService(deviceToPair);
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
                        CollapseDevices();

                        if (RuntimeSettings.IsMdnsExpanderOpen)
                            UpdateQrClass();
                    }
                    break;

                case nameof(AppRuntimeSettings.GroupsExpanded):
                    SettingsAboutExpander.IsExpanded = RuntimeSettings.GroupsExpanded;
                    break;

                case nameof(AppRuntimeSettings.SearchText):
                    FilterSettings();
                    break;

                case nameof(AppRuntimeSettings.BrowseDrive) when RuntimeSettings.BrowseDrive:
                    InitNavigation(RuntimeSettings.BrowseDrive.Path);
                    break;
            }
        });
    }

    private void SettingsSearchBox_FocusChanged(object sender, RoutedEventArgs e)
    {
        if (!Settings.DisableAnimation)
        {
            Settings.IsAnimated = !SettingsSearchBox.IsFocused;
        }
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
        if (e.PropertyName is nameof(FileOperationQueue.IsActive) or nameof(FileOperationQueue.AnyFailedOperations) or nameof(fileOperationQueue.Progress))
        {
            if (fileOperationQueue.AnyFailedOperations)
                TaskbarItemInfo.ProgressState = TaskbarItemProgressState.Error;
            else if (fileOperationQueue.IsActive)
            {
                if (fileOperationQueue.Progress == 0)
                    TaskbarItemInfo.ProgressState = TaskbarItemProgressState.Indeterminate;
                else
                    TaskbarItemInfo.ProgressState = TaskbarItemProgressState.Normal;
            }
            else
                TaskbarItemInfo.ProgressState = TaskbarItemProgressState.None;
        }
    }

    private void SplashTimer_Tick(object sender, EventArgs e)
    {
        SplashScreen.Visible(false);
        SplashTimer.Stop();
    }

    private static void CheckForUpdates(Dispatcher dispatcher)
    {
        var version = Task.Run(() => Network.LatestAppRelease());
        version.ContinueWith((t) =>
        {
            if (t.Result is null || t.Result <= AppVersion)
                return;

            dispatcher.Invoke(() => DialogService.ShowMessage(S_NEW_VERSION(t.Result), S_NEW_VERSION_TITLE, DialogService.DialogIcon.Informational));
        });
    }

    private void CommandLog_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
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
                SetSymbolFont();
                PopulateButtons(CurrentPath);
                break;
            case nameof(AppSettings.Theme):
                SetTheme(Settings.Theme);
                break;
            case nameof(AppSettings.EnableMdns):
                EnableMdns();
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

            if (!DirList.InProgress)
            {
                if (FileActions.IsRecycleBin)
                {
                    EnableRecycleButtons();
                    UpdateIndexerFile();
                }

                if (!string.IsNullOrEmpty(prevPath))
                {
                    var prevItem = DirList.FileList.Where(item => item.FullPath == prevPath);
                    if (prevItem.Any())
                    {
                        ExplorerGrid.SelectedIndex = ExplorerGrid.Items.IndexOf(prevItem.First());
                    }
                }
            }
        }
    }

    private void UpdateIndexerFile()
    {
        Task.Run(() =>
        {
            var validIndexers = DirList.FileList.Where(file => file.TrashIndex is not null).Select(file => file.TrashIndex);
            if (!validIndexers.Any())
            {
                ShellFileOperation.SilentDelete(CurrentADBDevice, RECYCLE_INDEX_PATH);
                ShellFileOperation.SilentDelete(CurrentADBDevice, RECYCLE_INDEX_BACKUP_PATH);
                return;
            }
            if (DirList.FileList.Count(file => RECYCLE_INDEX_PATHS.Contains(file.FullPath)) < 2
                && validIndexers.Count() == RecycleIndex.Count
                && RecycleIndex.All(indexer => validIndexers.Contains(indexer)))
            {
                return;
            }

            var outString = string.Join("\r\n", validIndexers.Select(indexer => indexer.ToString()));
            var oldIndexFile = DirList.FileList.Where(file => file.FullPath == RECYCLE_INDEX_PATH);

            try
            {
                if (oldIndexFile.Any())
                    ShellFileOperation.MoveItem(CurrentADBDevice, oldIndexFile.First(), RECYCLE_INDEX_BACKUP_PATH);

                ShellFileOperation.WriteLine(CurrentADBDevice, RECYCLE_INDEX_PATH, ADBService.EscapeAdbShellString(outString));

                if (!string.IsNullOrEmpty(ShellFileOperation.ReadAllText(CurrentADBDevice, RECYCLE_INDEX_PATH)) && oldIndexFile.Any())
                    ShellFileOperation.SilentDelete(CurrentADBDevice, RECYCLE_INDEX_BACKUP_PATH);
            }
            catch (Exception)
            { }
        });
    }

    private void EnableRecycleButtons(IEnumerable<FileClass> fileList = null)
    {
        if (fileList is null)
            fileList = DirList.FileList;

        FileActions.RestoreEnabled = fileList.Any(file => file.TrashIndex is not null && !string.IsNullOrEmpty(file.TrashIndex.OriginalPath));
        FileActions.DeleteEnabled = fileList.Any(item => !RECYCLE_INDEX_PATHS.Contains(item.FullPath));
    }

    private void ThemeService_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            SetTheme();
        });
    }

    private bool CheckAdbVersion()
    {
        Version version = null;

        if (string.IsNullOrEmpty(Settings.ManualAdbPath))
        {
            version = ADBService.VerifyAdbVersion("adb");
            if (version >= MIN_ADB_VERSION)
                return true;
        }
        else
        {
            version = ADBService.VerifyAdbVersion(Settings.ManualAdbPath);
            if (version >= MIN_ADB_VERSION)
                return true;
        }

        Dispatcher.Invoke(() =>
        {
            SimpleStackPanel stack = new()
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock()
                    {
                        TextWrapping = TextWrapping.Wrap,
                        Text = version is null ? S_MISSING_ADB : S_ADB_VERSION_LOW,
                    },
                    new TextBlock()
                    {
                        TextWrapping = TextWrapping.Wrap,
                        Text = S_OVERRIDE_ADB,
                    },
                    new HyperlinkButton()
                    {
                        Content = S_ADB_LEARN_MORE,
                        ToolTip = L_ADB_PAGE,
                        NavigateUri = L_ADB_PAGE,
                        HorizontalAlignment = HorizontalAlignment.Center,
                    }
                }
            };

            DialogService.ShowDialog(stack, S_MISSING_ADB_TITLE, DialogService.DialogIcon.Critical);
        });

        return false;
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        if (DirList is not null)
        {
            DirList.Stop();
        }

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
                    NavigateBack();
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
        if (FileActions.IsAppDrive)
        {
            FileActions.CopyPathEnabled = selectedPackages.Count() == 1;
            return;
        }

        OnPropertyChanged(nameof(SelectedFilesTotalSize));

        FileActions.IsRegularItem = !(selectedFiles.Any() && DevicesObject.Current?.Root is not AbstractDevice.RootStatus.Enabled
            && selectedFiles.All(item => item is FileClass file && file.Type is not (FileType.File or FileType.Folder)));

        if (FileActions.IsRecycleBin)
        {
            EnableRecycleButtons(selectedFiles.Any() ? selectedFiles : DirList.FileList);
        }
        else
        {
            FileActions.DeleteEnabled = selectedFiles.Any() && FileActions.IsRegularItem;
            FileActions.RestoreEnabled = false;
        }

        FileActions.DeleteAction = FileActions.IsRecycleBin && !selectedFiles.Any() ? "Empty Recycle Bin" : "Delete";
        FileActions.RestoreAction = FileActions.IsRecycleBin && !selectedFiles.Any() ? "Restore All Items" : "Restore";

        FileActions.PullEnabled = FileActions.PushPullEnabled && !FileActions.IsRecycleBin && selectedFiles.Any() && FileActions.IsRegularItem;
        FileActions.ContextPushEnabled = FileActions.PushPullEnabled && !FileActions.IsRecycleBin && (!selectedFiles.Any() || (selectedFiles.Count() == 1 && selectedFiles.First().IsDirectory));

        FileActions.RenameEnabled = !FileActions.IsRecycleBin && selectedFiles.Count() == 1 && FileActions.IsRegularItem;

        FileActions.CutEnabled = !selectedFiles.All(file => file.CutState is FileClass.CutType.Cut) && FileActions.IsRegularItem;

        FileActions.CopyEnabled = !FileActions.IsRecycleBin && FileActions.IsRegularItem && !selectedFiles.All(file => file.CutState is FileClass.CutType.Copy);
        FileActions.PasteEnabled = IsPasteEnabled();

        FileActions.PackageActionsEnabled = Settings.EnableApk && selectedFiles.Any() && selectedFiles.All(file => file.IsInstallApk) && !FileActions.IsRecycleBin;
        FileActions.CopyPathEnabled = selectedFiles.Count() == 1 && !FileActions.IsRecycleBin;

        FileActions.ContextNewEnabled = !selectedFiles.Any() && !FileActions.IsRecycleBin;
        FileActions.SubmenuUninstallEnabled = FileActions.IsTemp && selectedFiles.Any() && selectedFiles.All(file => file.IsInstallApk);

        FileActions.UpdateModifiedEnabled = !FileActions.IsRecycleBin && selectedFiles.Any() && selectedFiles.All(file => file.Type is FileType.File);

        FileActions.EditFileEnabled = !FileActions.IsRecycleBin
            && selectedFiles.Count() == 1
            && selectedFiles.First().Type is FileType.File;
    }

    private bool IsPasteEnabled(bool ignoreSelected = false, bool isKeyboard = false)
    {
        if (CutItems.Count < 1 || FileActions.IsRecycleBin)
            return false;

        if (CutItems.Count == 1 && CutItems[0].Relation(CurrentPath) is RelationType.Descendant or RelationType.Self)
            return false;

        var selected = ignoreSelected ? 0 : selectedFiles?.Count();
        switch (selected)
        {
            case 0:
                return !(CutItems[0].ParentPath == CurrentPath && CutItems[0].CutState is FileClass.CutType.Cut);
            case 1:
                if (isKeyboard && CutItems[0].CutState is FileClass.CutType.Copy && CutItems[0].Relation(selectedFiles.First()) is RelationType.Self)
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

        if (Settings.CheckForUpdates is true)
            CheckForUpdates(Dispatcher);
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
            ClearDrives();
            DevicesObject.SetOpenDevice((LogicalDeviceViewModel)null);
        }

        if (!DevicesObject.DevicesAvailable())
        {
            ClearExplorer();
            NavHistory.Reset();
            ClearDrives();
            return;
        }
        else
        {
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
        }

        DevicesObject.SetOpenDevice(DevicesObject.Current);
        CurrentADBDevice = new(DevicesObject.Current);
        InitLister();
        if (init)
            InitDevice();
    }

    private void FilterDevices()
    {
        var collectionView = CollectionViewSource.GetDefaultView(DevicesList.ItemsSource);
        if (collectionView is null)
            return;

        if (collectionView.Filter is not null)
        {
            collectionView.Refresh();
            return;
        }

        Predicate<object> predicate = d =>
        {
            var device = (DeviceViewModel)d;

            // current device cannot be hidden
            if (device is LogicalDeviceViewModel ui && ui.IsOpen)
                return true;

            if (device is LogicalDeviceViewModel && device.Type is AbstractDevice.DeviceType.Service)
            {
                if (device.Status is AbstractDevice.DeviceStatus.Offline)
                {
                    // if a logical service is offline, and we have one of its services - hide the logical service
                    return !DevicesObject.ServiceDeviceViewModels.Any(s => s.IpAddress == device.IpAddress);
                }

                // if there's a logical service and a remote device with the same IP - hide the logical service
                return !DevicesObject.LogicalDeviceViewModels.Any(l => l.IpAddress == device.IpAddress
                                                        && l.Type is AbstractDevice.DeviceType.Remote or AbstractDevice.DeviceType.Local
                                                        && l.Status is AbstractDevice.DeviceStatus.Ok);
            }
            else if (device is LogicalDeviceViewModel && device.Type is AbstractDevice.DeviceType.Remote)
            {
                // if a remote device is also connected by USB and both are authorized - hide the remote device
                return !DevicesObject.LogicalDeviceViewModels.Any(usb => usb.Type is AbstractDevice.DeviceType.Local
                    && usb.Status is AbstractDevice.DeviceStatus.Ok
                    && usb.IpAddress == device.IpAddress);
            }
            else if (device is HistoryDeviceViewModel)
            {
                // if there's any device with the IP of a history device - hide the history device
                return Settings.SaveDevices && !DevicesObject.LogicalDeviceViewModels.Any(logical => logical.IpAddress == device.IpAddress)
                        && !DevicesObject.ServiceDeviceViewModels.Any(service => service.IpAddress == device.IpAddress);
            }
            else if (device is ServiceDeviceViewModel service)
            {
                // connect services are always hidden
                if (service is ConnectServiceViewModel)
                    return false;

                // if there's any online logical device with the IP of a pairing service - hide the pairing service
                if (DevicesObject.LogicalDeviceViewModels.Any(logical => logical.Status is not AbstractDevice.DeviceStatus.Offline && logical.IpAddress == service.IpAddress))
                    return false;

                // if there's any QR service with the IP of a code pairing service - hide the code pairing service
                if (service.MdnsType is ServiceDevice.ServiceType.PairingCode
                    && DevicesObject.ServiceDeviceViewModels.Any(qr => qr.MdnsType is ServiceDevice.ServiceType.QrCode
                                                              && qr.IpAddress == service.IpAddress))
                    return false;
            }

            return true;
        };

        collectionView.Filter = new(predicate);
        collectionView.SortDescriptions.Clear();
        collectionView.SortDescriptions.Add(new SortDescription(nameof(DeviceViewModel.Type), ListSortDirection.Ascending));
    }

    private void InitLister()
    {
        DirList = new(Dispatcher, CurrentADBDevice, ListerFileManipulator);
        DirList.PropertyChanged += DirectoryLister_PropertyChanged;
    }

    private FileClass ListerFileManipulator(FileClass item)
    {
        if (CutItems.Any() && (CutItems[0].ParentPath == DirList.CurrentPath))
        {
            var cutItem = CutItems.Where(f => f.FullPath == item.FullPath);
            if (cutItem.Any())
            {
                item.CutState = cutItem.First().CutState;
                CutItems.Remove(cutItem.First());
                CutItems.Add(item);
            }
        }

        if (FileActions.IsRecycleBin)
        {
            var query = RecycleIndex.Where(index => index.RecycleName == item.FullName);
            if (query.Any())
            {
                item.TrashIndex = query.First();
                item.UpdateType();
            }
        }

        return item;
    }

    private void LoadSettings()
    {
        Dispatcher.Invoke(() => SetSymbolFont());

        SetTheme(Settings.Theme);
        SetRenderMode();

        EnableMdns();

        UISettings.Init();
        Dispatcher.Invoke(() =>
        {
            SettingsList.ItemsSource = UISettings.GroupedSettings;
            SortedSettings.ItemsSource = UISettings.SortSettings;

            FilterDevices();
        });
    }

    private void VerifyProgressRedirection()
    {
        Task.Run(() =>
        {
            if (!File.Exists($"{Environment.CurrentDirectory}\\{ProgressRedirectionPath}"))
            {
                try
                {
                    string newPath = $"{IsolatedStorageLocation}\\{ProgressRedirectionPath}";
                    if (File.Exists(newPath))
                    {
                        ProgressRedirectionPath = newPath;
                    }
                    else
                    {
                        File.WriteAllBytes(newPath, Properties.Resources.AdbProgressRedirection);
                        ProgressRedirectionPath = newPath;
                    }

                    return;
                }
                catch (Exception e)
                {
                    Dispatcher.Invoke(() =>
                    {
                        DialogService.ShowMessage(S_MISSING_REDIRECTION(e.Message), S_MISSING_REDIRECTION_TITLE, DialogService.DialogIcon.Critical);
                        FileActions.PushPullEnabled = false;
                    });
                }
            }
        });
    }

    private void SetSymbolFont()
    {
        Application.Current.Resources["SymbolThemeFontFamily"] = new FontFamily(Settings.UseFluentStyles ? "Segoe Fluent Icons, Segoe MDL2 Assets" : "Segoe MDL2 Assets");
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
        _ => throw new NotImplementedException(),
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
            _ => throw new NotImplementedException(),
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
        return FileOpContextItems.Where(cb => cb.Name == $"FileOpContext{ColumnName(column).Split("Column")[0]}CheckBox").First();
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
        Width = column.ActualWidth
    };

    private void InitDevice()
    {
        SetAndroidVersion();
        RefreshDrives(true);
        DriveViewNav();
        NavHistory.Navigate(NavHistory.SpecialLocation.DriveView);
        FilterDrives();

        CurrentDeviceDetailsPanel.DataContext = DevicesObject.Current;
        DeleteMenuButton.DataContext = DevicesObject.Current;
        FileActions.PushPackageEnabled = Settings.EnableApk;
#if DEBUG
        TestCurrentOperation();
#endif

        VerifyProgressRedirection();
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

    private void DriveViewNav()
    {
        ClearExplorer(false);
        FileActions.IsDriveViewVisible = true;
        PathBox.IsEnabled = true;

        MenuItem button = CreatePathButton(DevicesObject.Current, DevicesObject.Current.Name);
        button.Click += HomeButton_Click;
        AddPathButton(button);
        TextHelper.SetAltText(PathBox, "");

        DriveList.ItemsSource = DevicesObject.Current.Drives;

        if (DriveList.SelectedIndex > -1)
            SelectionHelper.GetListViewItemContainer(DriveList).Focus();
    }

    private void CombineDisplayNames()
    {
        foreach (var drive in DevicesObject.Current.Drives.OfType<LogicalDriveViewModel>().Where(d => d.Type != AbstractDrive.DriveType.Root))
        {
            CurrentDisplayNames.TryAdd(drive.Path, drive.Type is AbstractDrive.DriveType.External
                ? drive.ID : drive.DisplayName);
        }
        foreach (var item in SPECIAL_FOLDERS_DISPLAY_NAMES)
        {
            CurrentDisplayNames.TryAdd(item.Key, item.Value);
        }
        foreach (var item in DRIVE_TYPES)
        {
            var names = DRIVE_DISPLAY_NAMES.Where(n => n.Key == item.Value);
            if (names.Any())
                CurrentDisplayNames.TryAdd(item.Key, names.First().Value);
        }
    }

    private bool InitNavigation(string path = "", bool bfNavigated = false)
    {
        if (path is null)
            return true;

        CombineDisplayNames();

        var realPath = FolderExists(string.IsNullOrEmpty(path) ? DEFAULT_PATH : path);
        if (realPath is null)
            return false;

        FileActions.IsDriveViewVisible = false;
        FileActions.IsExplorerVisible = true;
        FileActions.HomeEnabled = DevicesObject.Current.Drives.Any();
        RuntimeSettings.BrowseDrive = null;

        return _navigateToPath(realPath, bfNavigated);
    }

    private void UpdateRecycledItemsCount()
    {
        var countTask = Task.Run(() => ADBService.CountRecycle(DevicesObject.Current.ID));
        countTask.ContinueWith((t) =>
        {
            if (t.IsCanceled || DevicesObject.Current is null)
                return;

            var count = t.Result;
            if (count < 1)
                count = FolderExists(RECYCLE_PATH) is null ? -1 : 0;

            var trash = DevicesObject.Current.Drives.Find(d => d.Type is AbstractDrive.DriveType.Trash);
            Dispatcher.Invoke(() => ((VirtualDriveViewModel)trash)?.SetItemsCount(count));
        });
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

        var viewModels = devices.Select(d => new LogicalDeviceViewModel(d));

        if (DevicesObject.DevicesChanged(viewModels))
        {
            DeviceListSetup(viewModels);

            if (Settings.AutoRoot)
            {
                foreach (var item in DevicesObject.LogicalDeviceViewModels.Where(device => device.Root is AbstractDevice.RootStatus.Unchecked))
                {
                    Task.Run(() => item.EnableRoot(true));
                }
            }
        }
    }

    private void UpdateDevicesRootAccess()
    {
        foreach (var device in DevicesObject.LogicalDeviceViewModels.Where(d => d.Root is AbstractDevice.RootStatus.Unchecked))
        {
            bool root = ADBService.WhoAmI(device.ID);
            Dispatcher.Invoke(() => device.SetRootStatus(root ? AbstractDevice.RootStatus.Enabled : AbstractDevice.RootStatus.Unchecked));
        }
    }

    private void UpdateDevicesBatInfo()
    {
        DevicesObject.Current?.UpdateBattery();

        if (DateTime.Now - DevicesObject.LastUpdate > BATTERY_UPDATE_INTERVAL || RuntimeSettings.IsDevicesPaneOpen)
        {
            var items = DevicesObject.LogicalDeviceViewModels.Where(device => !device.IsOpen);
            foreach (var item in items)
            {
                item.UpdateBattery();
            }

            DevicesObject.LastUpdate = DateTime.Now;
        }
    }

    private void ListServices(IEnumerable<ServiceDevice> services)
    {
        RuntimeSettings.LastServerResponse = DateTime.Now;

        if (services is null)
            return;

        var viewModels = services.Select(s => ServiceDeviceViewModel.New(s));

        if (DevicesObject.ServicesChanged(viewModels))
        {
            DevicesObject.UpdateServices(viewModels);

            var qrServices = DevicesObject.ServiceDeviceViewModels.Where(service =>
                service.MdnsType == ServiceDevice.ServiceType.QrCode
                && service.ID == QrClass.ServiceName);

            if (qrServices.Any())
            {
                PairService(qrServices.First()).ContinueWith((t) => RuntimeSettings.LastServerResponse = DateTime.Now);
            }
        }
    }

    private void ConnectTimer_Tick(object sender, EventArgs e)
    {
        ServerWatchdogTimer.Start();
        ConnectTimer.Interval = CONNECT_TIMER_INTERVAL;
        var driveView = NavHistory.Current is NavHistory.SpecialLocation.DriveView;

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
                UpdateDevicesBatInfo();
            }

            if (driveView && Settings.PollDrives)
            {
                try
                {
                    Dispatcher.Invoke(() => RefreshDrives(true));
                }
                catch (Exception)
                { }
            }

            if (RuntimeSettings.IsDevicesPaneOpen)
                UpdateDevicesRootAccess();

            connectTimerMutex.ReleaseMutex();
        });
    }

    private void RefreshDevices()
    {
        Dispatcher.BeginInvoke(new Action<IEnumerable<LogicalDevice>>(ListDevices), ADBService.GetDevices()).Wait();

        if (!RuntimeSettings.IsDevicesPaneOpen)
            return;

        Dispatcher.Invoke(() => DevicesObject.UpdateLogicalIp());

        if (MdnsService.State == MDNS.MdnsState.Running)
            Dispatcher.BeginInvoke(new Action<IEnumerable<ServiceDevice>>(ListServices), WiFiPairingService.GetServices()).Wait();
    }

    private void MdnsCheck()
    {
        Task.Run(() =>
        {
            return MdnsService.State = ADBService.CheckMDNS() ? MDNS.MdnsState.Running : MDNS.MdnsState.NotRunning;
        });
        Task.Run(async () =>
        {
            while (MdnsService.State is MDNS.MdnsState.InProgress)
            {
                Dispatcher.Invoke(() => MdnsService.UpdateProgress());
                
                await Task.Delay(MDNS_STATUS_UPDATE_INTERVAL);
            }
        });
    }

    public static string FolderExists(string path)
    {
        if (path == PACKAGE_PATH)
            return path;

        try
        {
            return CurrentADBDevice.TranslateDevicePath(path);
        }
        catch (Exception e)
        {
            if (path != RECYCLE_PATH)
                DialogService.ShowMessage(e.Message, S_NAV_ERR_TITLE, DialogService.DialogIcon.Critical);

            return null;
        }
    }

    public bool NavigateToPath(string path, bool bfNavigated = false)
    {
        if (path is null) return false;
        var realPath = FolderExists(path);
        FileActions.ExplorerFilter = "";

        return realPath is null ? false : _navigateToPath(realPath, bfNavigated);
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
        UpdateNavButtons();

        TextHelper.SetAltText(PathBox, realPath);
        CurrentPath = realPath;
        PopulateButtons(realPath);
        ParentPath = CurrentADBDevice.TranslateDeviceParentPath(CurrentPath);

        FileActions.IsRecycleBin = CurrentPath == RECYCLE_PATH;
        FileActions.IsAppDrive = CurrentPath == PACKAGE_PATH;
        FileActions.IsTemp = CurrentPath == TEMP_PATH;
        FileActions.ParentEnabled = CurrentPath != ParentPath && !FileActions.IsRecycleBin && !FileActions.IsAppDrive;
        FileActions.PasteEnabled = IsPasteEnabled();
        FileActions.ContextPasteEnabled = IsPasteEnabled(isContextMenu: true);
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

        FileActions.CopyPathAction = FileActions.IsAppDrive ? S_COPY_APK_NAME : S_COPY_PATH;

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
            FileActions.DeleteAction = S_EMPTY_TRASH;
            FileActions.RestoreAction = S_RESTORE_ALL;
        }
        else if (FileActions.IsAppDrive)
        {
            UpdatePackages(true);

            return true;
        }
        else
        {
            DirList.Navigate(realPath);

            DateColumn.Header = S_DATE_MOD_COL;
            FileActions.DeleteAction = S_DELETE_ACTION;
        }

        ExplorerGrid.ItemsSource = DirList.FileList;
        FilterHiddenFiles();
        UpdateFileActions();
        return true;
    }

    private void UpdateNavButtons()
    {
        BackButton.IsEnabled = NavHistory.BackAvailable;
        ForwardButton.IsEnabled = NavHistory.ForwardAvailable;
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
            PathButtons[^1].Width = PathButtons[^1].ActualWidth - (ControlSize.GetWidth(PathMenu) - PathBox.ActualWidth) - 4;
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
            }
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
        }
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

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        UnfocusPathBox();
        RuntimeSettings.IsSettingsPaneOpen = true;
    }

    private void ParentButton_Click(object sender, RoutedEventArgs e)
    {
        NavigateToPath(ParentPath);
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
                    break;
                default:
                    throw new NotImplementedException();
            }

            UpdateNavButtons();
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        NavigateToLocation(NavHistory.GoBack(), true);
    }

    private void ForwardButton_Click(object sender, RoutedEventArgs e)
    {
        NavigateToLocation(NavHistory.GoForward(), true);
    }

    private void Window_MouseUp(object sender, MouseButtonEventArgs e)
    {
        switch (e.ChangedButton)
        {
            case MouseButton.XButton1:
                if (NavHistory.BackAvailable)
                    NavigateBack();
                break;
            case MouseButton.XButton2:
                if (NavHistory.ForwardAvailable)
                    NavigateForward();
                break;
        }
    }

    private void NavigateForward()
    {
        AnimateControl(ForwardButton);
        NavigateToLocation(NavHistory.GoForward(), true);
    }

    private void NavigateBack()
    {
        AnimateControl(BackButton);
        NavigateToLocation(NavHistory.GoBack(), true);
    }

    private void AnimateControl(Control control)
    {
        StyleHelper.SetActivateAnimation(control, true);
        Task.Delay(400).ContinueWith(_ => Dispatcher.Invoke(() => StyleHelper.SetActivateAnimation(control, false)));
    }

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
            NavigateBack();
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

    public static Key RealKey(KeyEventArgs e) => e.Key switch
    {
        Key.System => e.SystemKey,
        Key.ImeProcessed => e.ImeProcessedKey,
        Key.DeadCharProcessed => e.DeadCharProcessedKey,
        _ => e.Key,
    };

    private void OnButtonKeyDown(object sender, KeyEventArgs e)
    {
        var nonShortcuttableKeys = new[] { Key.LeftAlt, Key.RightAlt, Key.LeftCtrl, Key.RightCtrl, Key.LeftShift, Key.RightShift };
        var actualKey = RealKey(e);
        bool alt, ctrl, shift;

        if (!e.IsDown || nonShortcuttableKeys.Contains(actualKey))
            return;

        ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        if (actualKey == Key.Back)
        {
            NavigateBack();
        }
        else if (actualKey == Key.Delete && FileActions.DeleteEnabled)
        {
            DeleteFiles();
        }
        else if (actualKey == Key.F6)
        {
            if (!alt)
                FocusPathBox();
            else if (!FileActions.IsRecycleBin && FileActions.IsExplorerVisible)
                Clipboard.SetText(CurrentPath);
        }
        else if (actualKey == Key.A && ctrl)
        {
            if (ExplorerGrid.Items.Count == ExplorerGrid.SelectedItems.Count)
                ExplorerGrid.UnselectAll();
            else
                ExplorerGrid.SelectAll();
        }
        else if (actualKey == Key.X && ctrl && FileActions.CutEnabled)
        {
            CutFiles(selectedFiles);
        }
        else if (actualKey == Key.C && ctrl && FileActions.CopyEnabled)
        {
            CutFiles(selectedFiles, true);
        }
        else if (actualKey == Key.V && ctrl && (FileActions.PasteEnabled || IsPasteEnabled(true)))
        {
            PasteFiles();
        }
        else if (actualKey == Key.F2 && FileActions.RenameEnabled)
        {
            BeginRename();
        }
        else if (actualKey == Key.C && shift && ctrl && FileActions.CopyPathEnabled)
        {
            CopyItemPath();
        }
        else if (actualKey == Key.H && ctrl && FileActions.IsExplorerVisible)
        {
            AnimateControl(HomeButton);
            NavHistory.Navigate(NavHistory.SpecialLocation.DriveView);
            NavigateToLocation(NavHistory.SpecialLocation.DriveView);
        }
        else if (actualKey == Key.C && alt && FileActions.PullEnabled)
        {
            PullFiles();
        }
        else if (actualKey == Key.R && ctrl && FileActions.IsRecycleBin && FileActions.RestoreEnabled)
        {
            RestoreItems();
        }
        else if (actualKey == Key.V && alt && FileActions.PushFilesFoldersEnabled)
        {
            PushItems(false, true);
        }
        else if (actualKey == Key.F10 && shift && FileActions.InstallUninstallEnabled)
        {
            InstallPackages();
        }
        else if (actualKey == Key.F11 && shift && FileActions.UninstallPackageEnabled)
        {
            UninstallPackages();
        }
        else if (actualKey == Key.F12 && shift && FileActions.CopyToTempEnabled)
        {
            CopyToTemp();
        }
        else if (actualKey == Key.I && alt && FileActions.PushPackageEnabled)
        {
            PushPackages();
        }
        else if (actualKey == Key.U && ctrl && FileActions.UpdateModifiedEnabled)
        {
            UpdatedModifiedDates();
        }
        else if (actualKey == Key.E && ctrl && FileActions.EditFileEnabled)
        {
            OpenEditor();
        }
        else if (actualKey == Key.F10)
        { }
        else if (actualKey is Key.Enter or Key.Up or Key.Down or Key.Left or Key.Right or Key.Escape or Key.Home or Key.End)
        {
            bool handle = false;

            if (FileActions.IsExplorerVisible)
            {
                handle |= ExplorerGridKeyNavigation(actualKey);
            }
            else if (FileActions.IsDriveViewVisible)
            {
                handle |= DriveViewKeyNavigation(actualKey);
            }

            if (!handle)
                return;
        }
        else
        {
            return;
        }

        e.Handled = true;
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
        else
        {
            if (key is Key.Enter)
            {
                ((DriveViewModel)DriveList.SelectedItem).BrowseCommand.Action();
                return true;
            }
            else if (key is Key.Escape)
            {
                // Should've been clear selected drives, but causes inconsistent behavior
                return true;
            }
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

    private void CopyMenuButton_Click(object sender, RoutedEventArgs e)
    {
        CutFiles(selectedFiles, true);
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
            fileOperationQueue.AddOperation(new FilePullOperation(Dispatcher, CurrentADBDevice, item, dirPath));
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
            fileOperationQueue.AddOperation(pushOpeartion);
        }
    }

    private void PushOpeartion_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        var pushOperation = sender as FilePushOperation;

        // If operation completed now and current path is where the new file was pushed to and it is not shown yet
        if ((e.PropertyName == "Status") &&
            (pushOperation.Status == FileOperation.OperationStatus.Completed) &&
            (pushOperation.TargetPath.FullPath == CurrentPath) &&
            (!DirList.FileList.Where(f => f.FullName == pushOperation.FilePath.FullName).Any()))
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

    private void ContextMenuPullItem_Click(object sender, RoutedEventArgs e)
    {
        PullFiles();
    }

    private void DataGridRow_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is MouseButton.XButton1 or MouseButton.XButton2)
            return;

        var row = sender as DataGridRow;

        if (row.IsSelected == false)
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

    private void OpenDevicesButton_Click(object sender, RoutedEventArgs e)
    {
        UnfocusPathBox();
        RuntimeSettings.IsDevicesPaneOpen = true;
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


                    CollapseDevices();
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
        FileActions.CopyPathEnabled =
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
            PathBox.IsEnabled =
            BackButton.IsEnabled =
            ForwardButton.IsEnabled = false;
        }
    }

    private void ClearDrives()
    {
        DevicesObject.Current?.Drives.Clear();
        FileActions.IsDriveViewVisible = false;
    }

    private void DevicesSplitView_PaneClosing(SplitView sender, SplitViewPaneClosingEventArgs args)
    {
        CollapseDevices();
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

    private void HomeButton_Click(object sender, RoutedEventArgs e)
    {
        NavHistory.Navigate(NavHistory.SpecialLocation.DriveView);
        NavigateToLocation(NavHistory.SpecialLocation.DriveView);
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
                UpdateRecycledItemsCount();

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
                var update = await DevicesObject.Current?.UpdateDrives(t.Result, dispatcher, asyncClasify);
                if (update is true)
                    FilterDrives();
            });
        });
    }

    private void PushMenuButton_Click(object sender, RoutedEventArgs e)
    {
        UnfocusPathBox();
        var menuItem = (MenuItem)sender;

        // The file context menu does not have a name
        PushItems(menuItem.Name == "PushFoldersMenu", string.IsNullOrEmpty(((MenuItem)menuItem.Parent).Name));
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

    private void PathMenuEdit_Click(object sender, RoutedEventArgs e)
    {
        PathBox.Focus();
    }

    private void GridBackgroundBlock_MouseDown(object sender, MouseButtonEventArgs e)
    {
        UnfocusPathBox();
        ClearSelectedDrives();
    }

    private void PathMenuCopy_Click(object sender, RoutedEventArgs e)
    {
        var text = TextHelper.GetAltText(PathBox);

        if (text != RECYCLE_PATH && text != PACKAGE_PATH)
            Clipboard.SetText(text);
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
            ? (new(HideFiles()))
            : (new(file => !IsHiddenRecycleItem((FileClass)file)));

            ExplorerGrid.Columns[1].SortDirection = ListSortDirection.Ascending;
            collectionView.SortDescriptions.Clear();
            collectionView.SortDescriptions.Add(new(nameof(FileClass.IsDirectory), ListSortDirection.Descending));
            collectionView.SortDescriptions.Add(new(nameof(FileClass.SortName), ListSortDirection.Ascending));
        }
    }

    private Predicate<object> HideFiles() => file =>
    {
        if (file is not FileClass fileClass)
            return false;

        if (fileClass.IsHidden)
            return false;

        return !IsHiddenRecycleItem(fileClass);
    };

    private bool IsHiddenRecycleItem(FileClass file)
    {
        if (RECYCLE_PATHS.Contains(file.FullPath))
            return true;

        if (!string.IsNullOrEmpty(FileActions.ExplorerFilter) && !file.ToString().Contains(FileActions.ExplorerFilter, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private void PullMenuButton_Click(object sender, RoutedEventArgs e)
    {
        UnfocusPathBox();
        PullFiles();
    }

    private void GridSplitter_DragDelta(object sender, DragDeltaEventArgs e)
    {
        // -1 + 1 or 1 + -1 (0 + 0 shouldn't happen)
        if (SimplifyNumber(e.VerticalChange) + DetailedViewSize() == 0)
            return;

        if (FileOpDetailedGrid.Height is double.NaN)
            FileOpDetailedGrid.Height = FileOpDetailedGrid.ActualHeight;

        FileOpDetailedGrid.Height -= e.VerticalChange;
    }

    /// <summary>
    /// Reduces the number to 3 possible values
    /// </summary>
    /// <param name="num">The number to evaluate</param>
    /// <returns>-1 if less than 0, 1 if greater than 0, 0 if 0</returns>
    private static sbyte SimplifyNumber(double num) => num switch
    {
        < 0 => -1,
        > 0 => 1,
        _ => 0
    };

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
            MdnsCheck();
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

    private void UpdateQrClass()
    {
        QrClass.Background = QR_BACKGROUND;
        QrClass.Foreground = QR_FOREGROUND;
        PairingQrImage.Source = QrClass.Image;
    }

    private async Task<bool> PairService(ServiceDeviceViewModel service)
    {
        var code = service.MdnsType == ServiceDevice.ServiceType.QrCode
            ? QrClass.Password
            : service.PairingCode;

        return await Task.Run(() =>
        {
            try
            {
                ADBService.PairNetworkDevice(service.ID, code);
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => DialogService.ShowMessage(ex.Message, S_PAIR_ERR_TITLE, DialogService.DialogIcon.Critical));
                return false;
            }

            return true;
        });
    }

    private void EnableMdns() => Dispatcher.Invoke(() =>
    {
        ADBService.IsMdnsEnabled = Settings.EnableMdns;
        if (Settings.EnableMdns)
        {
            QrClass = new();
        }
        else
        {
            QrClass = null;
            MdnsService.State = MDNS.MdnsState.Disabled;
        }
    });

    private void RemovePending_Click(object sender, RoutedEventArgs e)
    {
        fileOperationQueue.ClearPending();
    }

    private void RemoveCompleted_Click(object sender, RoutedEventArgs e)
    {
        fileOperationQueue.ClearCompleted();
    }

    private void OpenDefaultFolder_Click(object sender, RoutedEventArgs e)
    {
        Process.Start("explorer.exe", Settings.DefaultFolder);
    }

    private void RemovePendingAndCompleted_Click(object sender, RoutedEventArgs e)
    {
        fileOperationQueue.Clear();
    }

    private void StopFileOperations_Click(object sender, RoutedEventArgs e)
    {
        if (fileOperationQueue.IsActive)
            fileOperationQueue.Stop();
        else
            fileOperationQueue.Start();
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

        CollapseDevices();
    }

    private static void CollapseDevices()
    {
        // To make sure value changes to true
        RuntimeSettings.CollapseDevices = false;
        RuntimeSettings.CollapseDevices = true;
    }

    private static void ClearSelectedDrives()
    {
        RuntimeSettings.CollapseDrives = true;
        RuntimeSettings.CollapseDrives = false;
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

    private void ContextMenuDeleteItem_Click(object sender, RoutedEventArgs e)
    {
        DeleteFiles();
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
            deletedString = DisplayName(itemsToDelete.First());
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
                EnableRecycleButtons(DirList.FileList.Except(itemsToDelete));
                if (!selectedFiles.Any() && DirList.FileList.Any(item => RECYCLE_INDEX_PATHS.Contains(item.FullPath)))
                {
                    _ = Task.Run(() => ShellFileOperation.SilentDelete(CurrentADBDevice, DirList.FileList.Where(item => RECYCLE_INDEX_PATHS.Contains(item.FullPath))));
                }
            }
        }
    }

    private void RenameFile(string newName, FileClass file)
    {
        var newPath = $"{file.ParentPath}{(file.ParentPath.EndsWith('/') ? "" : "/")}{newName}{(Settings.ShowExtensions ? "" : file.Extension)}";
        if (DirList.FileList.Any(file => file.FullName == newName))
        {
            DialogService.ShowMessage(S_PATH_EXIST(newPath), S_RENAME_CONF_TITLE, DialogService.DialogIcon.Exclamation);
            return;
        }

        try
        {
            ShellFileOperation.MoveItem(CurrentADBDevice, file, newPath);
        }
        catch (Exception e)
        {
            DialogService.ShowMessage(e.Message, S_RENAME_ERR_TITLE, DialogService.DialogIcon.Critical);
            throw;
        }

        file.UpdatePath(newPath);
        if (Settings.ShowExtensions)
            file.UpdateType();
    }

    private void ContextMenuRenameItem_Click(object sender, RoutedEventArgs e)
    {
        BeginRename();
    }

    private void BeginRename()
    {
        var cell = CellConverter.GetDataGridCell(ExplorerGrid.SelectedCells[1]);

        cell.IsEditing = !cell.IsEditing;
    }

    private static FileClass GetFromCell(DataGridCellInfo cell) => CellConverter.GetDataGridCell(cell).DataContext as FileClass;

    private void NameColumnEdit_Loaded(object sender, RoutedEventArgs e)
    {
        var textBox = sender as TextBox;
        TextHelper.SetAltObject(textBox, GetFromCell(ExplorerGrid.SelectedCells[1]));
        textBox.Focus();
    }

    private static string DisplayName(TextBox textBox) => DisplayName(textBox.DataContext as FilePath);
    private static string DisplayName(FilePath file) => Settings.ShowExtensions ? file.FullName : file.NoExtName;

    private void NameColumnEdit_LostFocus(object sender, RoutedEventArgs e)
    {
        Rename(sender as TextBox);
    }

    private void Rename(TextBox textBox)
    {
        FileClass file = TextHelper.GetAltObject(textBox) as FileClass;
        var name = DisplayName(textBox);
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
                RenameFile(textBox.Text, file);
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
            var name = DisplayName(textBox);
            if (string.IsNullOrEmpty(name))
            {
                DirList.FileList.Remove(ExplorerGrid.SelectedItem as FileClass);
            }
            else
            {
                textBox.Text = DisplayName(sender as TextBox);
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
            else if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
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

    private void ContextMenuCutItem_Click(object sender, RoutedEventArgs e)
    {
        CutFiles(selectedFiles);
    }

    private void CutFiles(IEnumerable<FileClass> items, bool isCopy = false)
    {
        ClearCutFiles();

        var itemsToCut = DevicesObject.Current.Root is not AbstractDevice.RootStatus.Enabled
                    ? items.Where(file => file.Type is FileType.File or FileType.Folder) : items;

        foreach (var item in itemsToCut)
        {
            item.CutState = isCopy ? FileClass.CutType.Copy : FileClass.CutType.Cut;
        }

        CutItems.AddRange(itemsToCut);

        FileActions.CopyEnabled = !isCopy;
        FileActions.CutEnabled = isCopy;

        FileActions.PasteEnabled = IsPasteEnabled();
    }

    private static void ClearCutFiles()
    {
        CutItems.ForEach(f => f.CutState = FileClass.CutType.None);
        CutItems.Clear();
    }

    private static void ClearCutFiles(IEnumerable<FileClass> items)
    {
        foreach (var item in items)
        {
            item.CutState = FileClass.CutType.None;
        }
        CutItems.RemoveAll(items.ToList());
    }

    private void PasteFiles()
    {
        bool isCopy = CutItems[0].CutState is FileClass.CutType.Copy;
        var firstSelectedFile = selectedFiles.Any() ? selectedFiles.First() : null;
        var targetName = "";
        var targetPath = "";

        if (selectedFiles.Count() != 1 || (isCopy && CutItems[0].Relation(firstSelectedFile) is RelationType.Self))
        {
            targetPath = CurrentPath;
            targetName = CurrentPath[CurrentPath.LastIndexOf('/')..];
        }
        else
        {
            targetPath = ((FileClass)ExplorerGrid.SelectedItem).FullPath;
            targetName = DisplayName((FilePath)ExplorerGrid.SelectedItem);
        }

        var pasteItems = CutItems.Where(f => f.Relation(targetPath) is not (RelationType.Self or RelationType.Descendant));
        var moveTask = Task.Run(() => ShellFileOperation.MoveItems(isCopy,
                                                                   targetPath,
                                                                   pasteItems,
                                                                   targetName,
                                                                   DirList.FileList,
                                                                   Dispatcher,
                                                                   CurrentADBDevice,
                                                                   CurrentPath));
        moveTask.ContinueWith((t) =>
        {
            Dispatcher.Invoke(() =>
            {
                if (!isCopy)
                    ClearCutFiles(pasteItems);

                FileActions.PasteEnabled = IsPasteEnabled();
            });
        });
    }

    private void ContextMenuPasteItem_Click(object sender, RoutedEventArgs e)
    {
        PasteFiles();
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
                throw new NotImplementedException();
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

    private void NewFolderMenuItem_Click(object sender, RoutedEventArgs e)
    {
        NewItem(true);
    }

    private void NewFileMenuItem_Click(object sender, RoutedEventArgs e)
    {
        NewItem(false);
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

    private void CopyPathMenuItem_Click(object sender, RoutedEventArgs e)
    {
        CopyItemPath();
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

    private void RestoreMenuButton_Click(object sender, RoutedEventArgs e)
    {
        RestoreItems();
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
                    else if (result.Item1 is ContentDialogResult.Secondary)
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
                    EnableRecycleButtons();
            });
        });
    }

    private void DataGridRow_Unselected(object sender, RoutedEventArgs e)
    {
        ControlHelper.SetCornerRadius(e.OriginalSource as DataGridRow, new(Settings.UseFluentStyles ? 2 : 0));
    }

    private void RestartButton_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(Environment.ProcessPath);
        Application.Current.Shutdown();
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
                    Children = { ccLink, apacheLink }
                }
            }
        };

        DialogService.ShowDialog(stack, S_ANDROID_ICONS_TITLE, DialogService.DialogIcon.Informational);
    }

    private void DisableAnimationInfo_Click(object sender, RoutedEventArgs e)
    {
        DialogService.ShowMessage(S_DISABLE_ANIMATION, S_ANIMATION_TITLE, DialogService.DialogIcon.Tip);
    }

    private ListSortDirection Invert(ListSortDirection? value)
    {
        return value is ListSortDirection and ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending;
    }

    private void ExplorerGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        if (FileActions.IsAppDrive)
            return;

        var collectionView = CollectionViewSource.GetDefaultView(ExplorerGrid.ItemsSource);
        var sortDirection = Invert(e.Column.SortDirection);
        e.Column.SortDirection = sortDirection;

        collectionView.SortDescriptions.Clear();
        collectionView.SortDescriptions.Add(new(nameof(FileClass.IsDirectory), Invert(sortDirection)));

        if (e.Column.SortMemberPath != nameof(FileClass.FullName))
        collectionView.SortDescriptions.Add(new(e.Column.SortMemberPath, sortDirection));

        collectionView.SortDescriptions.Add(new(nameof(FileClass.SortName), sortDirection));

        e.Handled = true;
    }

    private void InstallPackage_Click(object sender, RoutedEventArgs e)
    {
        InstallPackages();
    }

    private void UninstallPackage_Click(object sender, RoutedEventArgs e)
    {
        UninstallPackages();
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

        var packageTask = Task.Run(() =>
        {
            if (FileActions.IsAppDrive)
                return pkgs.Select(pkg => pkg.Name);
            else
                return from item in files select ShellFileOperation.GetPackageName(CurrentADBDevice, item.FullPath);
        });
        _ = packageTask.ContinueWith((t) =>
        {
            if (!t.IsCanceled)
                ShellFileOperation.UninstallPackages(CurrentADBDevice, t.Result, Dispatcher, Packages);
        });
    }

    private void CopyToTemp_Click(object sender, RoutedEventArgs e)
    {
        CopyToTemp();
    }

    private void CopyToTemp()
    {
        _ = ShellFileOperation.MoveItems(true, TEMP_PATH, selectedFiles, DisplayName(selectedFiles.First()), DirList.FileList, Dispatcher, CurrentADBDevice, CurrentPath);
    }

    private void PushPackages()
    {
        var dialog = new CommonOpenFileDialog()
        {
            IsFolderPicker = false,
            Multiselect = true,
            DefaultDirectory = Settings.DefaultFolder,
            Title = S_INSTALL_APK
        };
        dialog.Filters.Add(new("Android Package", string.Join(';', INSTALL_APK.Select(name => name[1..]))));

        if (dialog.ShowDialog() != CommonFileDialogResult.Ok)
            return;

        Task.Run(() => ShellFileOperation.PushPackages(CurrentADBDevice, dialog.FilesAsShellObject, Dispatcher, FileActions.IsAppDrive));
    }

    private void PushPackagesMenu_Click(object sender, RoutedEventArgs e)
    {
        PushPackages();
    }

    private void DefaultFolderSetButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CommonOpenFileDialog()
        {
            IsFolderPicker = true,
            Multiselect = false
        };
        if (Settings.DefaultFolder != "")
            dialog.DefaultDirectory = Settings.DefaultFolder;

        if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
        {
            Settings.DefaultFolder = dialog.FileName;
        }
    }

    private void OverrideAdbPathSetButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog()
        {
            Multiselect = false,
            Title = S_OVERRIDE_ADB_BROWSE,
            Filter = "ADB Executable|adb.exe",
        };

        if (!string.IsNullOrEmpty(Settings.ManualAdbPath))
        {
            try
            {
                dialog.InitialDirectory = Directory.GetParent(Settings.ManualAdbPath).FullName;
            }
            catch (Exception) { }
        }

        if (dialog.ShowDialog() == true)
        {
            var version = ADBService.VerifyAdbVersion(dialog.FileName);
            if (version >= MIN_ADB_VERSION)
            {
                Settings.ManualAdbPath = dialog.FileName;
                return;
            }

            DialogService.ShowMessage(version is null ? S_MISSING_ADB_OVERRIDE : S_ADB_VERSION_LOW_OVERRIDE,
                                      S_FAIL_OVERRIDE_TITLE,
                                      DialogService.DialogIcon.Exclamation);
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

    private void ModifiedFromName_Click(object sender, RoutedEventArgs e)
    {
        UpdatedModifiedDates();
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

    private void ContextMenuEditItem_Click(object sender, RoutedEventArgs e)
    {
        OpenEditor();
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
