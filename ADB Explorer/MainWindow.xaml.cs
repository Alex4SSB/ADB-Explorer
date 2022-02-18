using ADB_Explorer.Converters;
using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;
using Microsoft.Win32;
using Microsoft.WindowsAPICodePack.Dialogs;
using Microsoft.WindowsAPICodePack.Shell;
using ModernWpf;
using ModernWpf.Controls;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using static ADB_Explorer.Converters.FileTypeClass;
using static ADB_Explorer.Helpers.VisibilityHelper;
using static ADB_Explorer.Models.AdbExplorerConst;
using static ADB_Explorer.Models.Data;

namespace ADB_Explorer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly DispatcherTimer ConnectTimer = new();
        private Mutex connectTimerMutex = new();
        private Task listDirTask;
        private Task unknownFoldersTask;
        private DispatcherTimer dirListUpdateTimer;
        private CancellationTokenSource dirListCancelTokenSource;
        private CancellationTokenSource determineFoldersCancelTokenSource;
        private ConcurrentQueue<FileStat> waitingFileStats;
        private ItemsPresenter ExplorerContentPresenter;
        private ScrollViewer ExplorerScroller;
        private bool TextBoxChangedMutex;
        private SolidColorBrush qrForeground, qrBackground;
        private ThemeService themeService = new();

        public static MDNS MdnsService { get; set; } = new();
        public Devices DevicesObject { get; set; } = new();
        public PairingQrClass QrClass { get; set; }

        public bool ListingInProgress { get { return listDirTask is not null; } }

        private double ColumnHeaderHeight
        {
            get
            {
                GetExplorerContentPresenter();
                if (ExplorerContentPresenter is null)
                    return 0;
                
                double height = ExplorerGrid.ActualHeight - ExplorerContentPresenter.ActualHeight - HorizontalScrollBarHeight;
                
                return height;
            }
        }
        private double DataGridContentWidth
        {
            get
            {
                GetExplorerContentPresenter();
                return ExplorerContentPresenter is null ? 0 : ExplorerContentPresenter.ActualWidth;
            }
        }
        private double HorizontalScrollBarHeight => ExplorerScroller.ComputedHorizontalScrollBarVisibility == Visibility.Visible
                    ? SystemParameters.HorizontalScrollBarHeight : 0;

        private readonly List<MenuItem> PathButtons = new();

        private void GetExplorerContentPresenter()
        {
            if (ExplorerContentPresenter is null && VisualTreeHelper.GetChild(ExplorerGrid, 0) is Border border && border.Child is ScrollViewer scroller && scroller.Content is ItemsPresenter presenter)
            {
                ExplorerScroller = scroller;
                ExplorerContentPresenter = presenter;
            }
        }

        private string SelectedFilesTotalSize
        {
            get
            {
                var files = ExplorerGrid.SelectedItems.OfType<FileClass>().ToList();
                if (files.Any(i => i.Type != FileType.File)) return "0";

                ulong totalSize = 0;
                files.ForEach(f => totalSize += f.Size.GetValueOrDefault(0));

                return totalSize.ToSize();
            }
        }

        public MainWindow()
        {
            InitializeComponent();

            fileOperationQueue = new(this.Dispatcher);
            LaunchSequence();

            ConnectTimer.Interval = CONNECT_TIMER_INIT;
            ConnectTimer.Tick += ConnectTimer_Tick;

            themeService.PropertyChanged += ThemeService_PropertyChanged;

            if (CheckAdbVersion())
            {
                ConnectTimer.Start();
                OpenDevicesButton.IsEnabled = true;
                DevicesSplitView.IsPaneOpen = true;
            }

            UpperProgressBar.DataContext = fileOperationQueue;
        }

        private void ThemeService_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                SetTheme();
            });
        }

        private bool CheckAdbVersion()
        {
            int exitCode = 1;
            string stdout = "";
            try
            {
                exitCode = ADBService.ExecuteCommand("adb", "version", out stdout, out _, Encoding.UTF8);
            }
            catch (Exception) { }

            if (exitCode == 0)
            {
                string version = AdbRegEx.ADB_VERSION.Match(stdout).Groups["version"]?.Value;
                if (new Version(version) < MIN_ADB_VERSION)
                {
                    MissingAdbTextblock.Visibility = Visibility.Collapsed;
                    AdbVersionTooLowTextblock.Visibility = Visibility.Visible;
                }
                else
                    return true;
            }
            SettingsSplitView.IsPaneOpen = true;
            WorkingDirectoriesExpander.IsExpanded = true;
            MissingAdbGrid.Visibility = Visibility.Visible;

            return false;
        }

        private void InitializeExplorerContextMenu(MenuType type, object dataContext = null)
        {
            MenuItem pullMenu = FindResource("ContextMenuCopyItem") as MenuItem;
            MenuItem deleteMenu = FindResource("ContextMenuDeleteItem") as MenuItem;
            ExplorerGrid.ContextMenu.Items.Clear();

            switch (type)
            {
                case MenuType.ExplorerItem:
                    pullMenu.IsEnabled = false;
                    if (dataContext is FileClass file && file.Type is FileType.File or FileType.Folder)
                        pullMenu.IsEnabled = true;

                    ExplorerGrid.ContextMenu.Items.Add(pullMenu);
                    //ExplorerGrid.ContextMenu.Items.Add(deleteMenu);
                    break;
                case MenuType.EmptySpace:
                    //ExplorerGrid.ContextMenu.Items.Add(deleteMenu);
                    break;
                case MenuType.Header:
                    break;
                default:
                    break;
            }
        }

        private void InitializePathContextMenu(FrameworkElement sender)
        {
            if (sender.ContextMenu is null)
                return;

            MenuItem headerMenu = FindResource("PathMenuHeader") as MenuItem;
            MenuItem editMenu = FindResource("PathMenuEdit") as MenuItem;
            MenuItem copyMenu = FindResource("PathMenuCopy") as MenuItem;
            sender.ContextMenu.Items.Clear();

            sender.ContextMenu.Items.Add(headerMenu);
            sender.ContextMenu.Items.Add(new Separator());
            sender.ContextMenu.Items.Add(editMenu);
            sender.ContextMenu.Items.Add(copyMenu);

            sender.ContextMenu.Visibility = Visible(sender.ContextMenu.HasItems);
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (listDirTask is not null)
            {
                StopDirectoryList();
                StopDetermineFolders();
                AndroidFileList.RemoveAll();
            }

            ConnectTimer.Stop();
            StoreClosingValues();
        }

        private void StoreClosingValues()
        {
            Storage.StoreValue(SystemVals.windowMaximized, WindowState == WindowState.Maximized);

            var detailedVisible = FileOpVisibility() && FileOpDetailedRadioButton.IsChecked == true;
            Storage.StoreValue(SystemVals.detailedVisible, detailedVisible);
            if (detailedVisible)
                Storage.StoreValue(SystemVals.detailedHeight, FileOpDetailedGrid.Height);
        }

        private void SetTheme()
        {
            SetTheme(SettingsTheme() is AppTheme.windowsDefault
                ? themeService.WindowsTheme
                : ThemeManager.Current.ApplicationTheme.Value);
        }

        private void SetTheme(ApplicationTheme theme)
        {
            ThemeManager.Current.ApplicationTheme = theme;

            foreach (string key in ((ResourceDictionary)Application.Current.Resources["DynamicBrushes"]).Keys)
            {
                SetResourceColor(theme, key);
            }

            Storage.StoreEnum(SettingsTheme());

            if (EnableMdnsCheckBox.IsChecked == true)
            {
                SetQrColors(theme);
                UpdateQrClass();
            }
        }

        private void SettingsTheme(AppTheme theme)
        {
            switch (theme)
            {
                case AppTheme.light:
                    LightThemeRadioButton.IsChecked = true;
                    break;
                case AppTheme.dark:
                    DarkThemeRadioButton.IsChecked = true;
                    break;
                case AppTheme.windowsDefault:
                    DefaultThemeRadioButton.IsChecked = true;
                    break;
                default:
                    throw new NotImplementedException();
            }
        }

        private AppTheme SettingsTheme()
        {
            if (LightThemeRadioButton.IsChecked == true) return AppTheme.light;
            else if (DarkThemeRadioButton.IsChecked == true) return AppTheme.dark;
            else return AppTheme.windowsDefault;
        }

        private static void SetResourceColor(ApplicationTheme theme, string resource)
        {
            Application.Current.Resources[resource] = new SolidColorBrush((Color)Application.Current.Resources[$"{theme}{resource}"]);
        }

        private void SetQrColors(ApplicationTheme theme)
        {
            switch (theme)
            {
                case ApplicationTheme.Light:
                    qrBackground = QR_BACKGROUND_LIGHT;
                    qrForeground = QR_FOREGROUND_LIGHT;
                    break;
                case ApplicationTheme.Dark:
                    qrBackground = QR_BACKGROUND_DARK;
                    qrForeground = QR_FOREGROUND_DARK;
                    break;
                default:
                    break;
            }
        }

        private void PathBox_GotFocus(object sender, RoutedEventArgs e)
        {
            FocusPathBox();
        }

        private void FocusPathBox()
        {
            PathStackPanel.Visibility = Visibility.Collapsed;
            PathBox.Text = PathBox.Tag?.ToString();
            PathBox.IsReadOnly = false;
            PathBox.SelectAll();
        }

        private void UnfocusPathBox()
        {
            PathStackPanel.Visibility = Visibility.Visible;
            PathBox.Clear();
            PathBox.IsReadOnly = true;
            FileOperationsSplitView.Focus();
        }

        private void PathBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (ExplorerGrid.IsVisible)
                {
                    if (NavigateToPath(PathBox.Text))
                        return;
                }
                else
                {
                    if (!InitNavigation(PathBox.Text))
                    {
                        DriveViewNav();
                        return;
                    }
                }

                ExplorerGrid.Focus();
            }
            else if (e.Key == Key.Escape)
            {
                UnfocusPathBox();
            }
        }

        private void PathBox_LostFocus(object sender, RoutedEventArgs e)
        {
            UnfocusPathBox();
        }

        private void DataGridRow_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && ExplorerGrid.SelectedItems.Count == 1)
                DoubleClick(ExplorerGrid.SelectedItem);
        }

        private void DoubleClick(object source)
        {
            if (source is FileClass file)
            {
                switch (file.Type)
                {
                    case FileType.File:
                        if (PullOnDoubleClickCheckBox.IsChecked == true)
                            CopyFiles(true);
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
            TotalSizeBlock.Text = SelectedFilesTotalSize;

            var items = ExplorerGrid.SelectedItems.Cast<FileClass>();
            PullMenuButton.IsEnabled = items.Any() && items.All(f => f.Type
                is FileType.File
                or FileType.Folder);
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            UnfocusPathBox();
        }

        private void LaunchSequence()
        {
            LoadSettings();

            TestCurrentOperation();
            TestDevices();
        }

        private void DeviceListSetup(IEnumerable<LogicalDevice> devices = null, string selectedAddress = "")
        {
            var init = !DevicesObject.UpdateDevices(devices is null ? ADBService.GetDevices() : devices);

            if (DevicesObject.Current is null || DevicesObject.Current.IsOpen && DevicesObject.CurrentDevice.Status != AbstractDevice.DeviceStatus.Online)
                ClearDrives();

            if (!DevicesObject.DevicesAvailable())
            {
                Title = $"{Properties.Resources.AppDisplayName} - NO CONNECTED DEVICES";
                ClearExplorer();
                ClearDrives();
                return;
            }
            else
            {
                if (DevicesObject.DevicesAvailable(true))
                    return;

                if (AutoOpenCheckBox.IsChecked != true)
                {
                    DevicesObject.CloseAll();

                    Title = Properties.Resources.AppDisplayName;
                    ClearExplorer();
                    return;
                }

                if (!DevicesObject.SetCurrentDevice(selectedAddress))
                    return;

                if (!ConnectTimer.IsEnabled)
                    DevicesSplitView.IsPaneOpen = false;
            }

            DevicesObject.SetOpen(DevicesObject.Current, true);
            CurrentADBDevice = new(DevicesObject.CurrentDevice);
            if (init)
                InitDevice();
        }

        private void LoadSettings()
        {
            Title = $"{Properties.Resources.AppDisplayName} - NO CONNECTED DEVICES";

            var appTheme = Storage.RetrieveEnum<AppTheme>();
            SettingsTheme(appTheme);
            SetTheme(appTheme switch
            {
                AppTheme.light => ApplicationTheme.Light,
                AppTheme.dark => ApplicationTheme.Dark,
                AppTheme.windowsDefault => themeService.WindowsTheme,
                _ => throw new NotImplementedException(),
            });

            if (Storage.RetrieveBool(UserPrefs.forceFluentStyles) is bool forceFluent)
                ForceFluentStylesCheckbox.IsChecked = forceFluent;
            
            if (Storage.RetrieveValue(UserPrefs.manualAdbPath) is string adbPath)
                ManualAdbPath.Text = adbPath;

            if (Storage.RetrieveBool(UserPrefs.enableMdns) is bool enable)
            {
                // Intentional invocation of the checked event
                EnableMdnsCheckBox.IsChecked = enable;

                if (enable)
                    QrClass = new();
            }

            if (Storage.RetrieveBool(UserPrefs.autoRoot) is bool autoRoot)
                AutoRootCheckBox.IsChecked = autoRoot;

            if (Storage.RetrieveValue(UserPrefs.defaultFolder) is string path && !string.IsNullOrEmpty(path))
                DefaultFolderBlock.Text = path;

            if (Storage.RetrieveBool(UserPrefs.pullOnDoubleClick) is bool copy)
                PullOnDoubleClickCheckBox.IsChecked = copy;

            if (Storage.RetrieveBool(UserPrefs.rememberIp) is bool remIp)
                RememberIpCheckBox.IsChecked = remIp;

            RememberPortCheckBox.IsEnabled = (bool)RememberIpCheckBox.IsChecked;

            if (RememberPortCheckBox.IsEnabled
                    && Storage.RetrieveBool(UserPrefs.rememberPort) is bool remPort)
            {
                RememberPortCheckBox.IsChecked = remPort;
            }

            if (Storage.RetrieveBool(UserPrefs.autoOpen) is bool autoOpen)
                AutoOpenCheckBox.IsChecked = autoOpen;

            if (Storage.RetrieveBool(UserPrefs.showExtensions) is bool showExt)
                ShowExtensionsCheckBox.IsChecked = showExt;

            if (Storage.RetrieveBool(UserPrefs.showHiddenItems) is bool showHidden)
                ShowHiddenCheckBox.IsChecked = showHidden;

            bool extendedView = Storage.RetrieveBool(UserPrefs.showExtendedView) is bool val && val;
            FileOpDetailedRadioButton.IsChecked = extendedView;
            FileOpCompactRadioButton.IsChecked = !extendedView;

            CurrentOperationDataGrid.ItemsSource = fileOperationQueue.CurrentOperations;
            PendingOperationsDataGrid.ItemsSource = fileOperationQueue.PendingOperations;
            CompletedOperationsDataGrid.ItemsSource = fileOperationQueue.CompletedOperations;
        }

        private void InitDevice()
        {
            Title = $"{Properties.Resources.AppDisplayName} - {DevicesObject.Current.Name}";

            RefreshDrives();
            DriveViewNav();
            UpdateAndroidVersion();

            if (DevicesObject.CurrentDevice.Drives.Count < 1)
            {
                // Shouldn't actually happen
                InitNavigation();
            }

            TestCurrentOperation();
        }

        private void DriveViewNav()
        {
            ClearExplorer();
            ExplorerGrid.Visibility = Visibility.Collapsed;
            DrivesItemRepeater.Visibility = Visibility.Visible;
            PathBox.IsEnabled = true;

            MenuItem button = CreatePathButton(DevicesObject.Current, DevicesObject.Current.Name);
            button.ContextMenu = Resources["PathButtonsMenu"] as ContextMenu;
            AddPathButton(button);
        }

        private void UpdateAndroidVersion()
        {
            string androidVer = CurrentADBDevice.GetAndroidVersion();
            AndroidVersionBlock.Text = $"Android {androidVer}";

            sbyte ver = 0;
            sbyte.TryParse(androidVer.Split('.')[0], out ver);
            UnsupportedAndroidIcon.Visibility = Visible(ver > 0 && ver < MIN_SUPPORTED_ANDROID_VER);
        }

        private void CombinePrettyNames()
        {
            foreach (var drive in DevicesObject.CurrentDevice.Drives.Where(d => d.Type != Models.DriveType.Root))
            {
                CurrentPrettyNames.TryAdd(drive.Path, drive.Type == Models.DriveType.External
                    ? drive.ID : drive.PrettyName);
            }
            foreach (var item in SPECIAL_FOLDERS_PRETTY_NAMES)
            {
                CurrentPrettyNames.TryAdd(item.Key, item.Value);
            }
        }

        private bool InitNavigation(string path = "")
        {
            DrivesItemRepeater.Visibility = Visibility.Collapsed;
            ExplorerGrid.Visibility = Visibility.Visible;
            CombinePrettyNames();

            unknownFoldersTask = Task.Run(() => { });
            dirListCancelTokenSource = new CancellationTokenSource();
            determineFoldersCancelTokenSource = new CancellationTokenSource();
            waitingFileStats = new ConcurrentQueue<FileStat>();
            dirListUpdateTimer = new DispatcherTimer
            {
                Interval = DIR_LIST_UPDATE_INTERVAL
            };
            dirListUpdateTimer.Tick += DirListUpdateTimer_Tick;

            ExplorerGrid.ItemsSource = AndroidFileList;
            PushMenuButton.IsEnabled = true;
            HomeButton.IsEnabled = DevicesObject.CurrentDevice.Drives.Any();
            NavHistory.Reset();

            return string.IsNullOrEmpty(path)
                ? NavigateToPath(DEFAULT_PATH)
                : NavigateToPath(path);
        }

        private void ListDevices(IEnumerable<LogicalDevice> devices)
        {
            if (devices is not null && DevicesObject.DevicesChanged(devices))
            {
                DeviceListSetup(devices);

                if (AutoRootCheckBox.IsChecked == true)
                {
                    foreach (var item in DevicesObject.LogicalDevices.Where(device => device.Root is AbstractDevice.RootStatus.Unchecked))
                    {
                        Task.Run(() => item.EnableRoot(true));
                    }
                }
            }
        }

        private void ListServices(IEnumerable<ServiceDevice> services)
        {
            if (services is not null && DevicesObject.ServicesChanged(services))
            {
                DevicesObject.UpdateServices(services);

                var qrServices = DevicesObject.ServiceDevices.Where(service => 
                    service.MdnsType == ServiceDevice.ServiceType.QrCode
                    && service.ID == QrClass.ServiceName).ToList();

                if (qrServices.Any() && PairService(qrServices.First()))
                    PairingExpander.IsExpanded = false;
            }
        }

        private void ConnectTimer_Tick(object sender, EventArgs e)
        {
            ConnectTimer.Interval = CONNECT_TIMER_INTERVAL;
            var devicesVisible = DevicesSplitView.IsPaneOpen;

            Task.Run(() =>
            {
                if (!connectTimerMutex.WaitOne(0))
                {
                    return;
                }

                Dispatcher.BeginInvoke(new Action<IEnumerable<LogicalDevice>>(ListDevices), ADBService.GetDevices()).Wait();

                if (MdnsService.State == MDNS.MdnsState.Running && devicesVisible)
                {
                    Dispatcher.BeginInvoke(new Action<IEnumerable<ServiceDevice>>(ListServices), WiFiPairingService.GetServices()).Wait();
                }

                connectTimerMutex.ReleaseMutex();
            });
        }

        private static void MdnsCheck()
        {
            Task.Run(() =>
            {
                return MdnsService.State = ADBService.CheckMDNS() ? MDNS.MdnsState.Running : MDNS.MdnsState.NotRunning;
            });
        }

        private void StartDirectoryList(string path)
        {
            Cursor = Cursors.AppStarting;

            Dispatcher.BeginInvoke(() =>
            {
                StopDirectoryList();
                StopDetermineFolders();

                AndroidFileList.RemoveAll();

                determineFoldersCancelTokenSource = new CancellationTokenSource();
                dirListCancelTokenSource = new CancellationTokenSource();
                waitingFileStats = new ConcurrentQueue<FileStat>();

                listDirTask = Task.Run(() => CurrentADBDevice.ListDirectory(path, ref waitingFileStats, dirListCancelTokenSource.Token));

                if (listDirTask.Wait(DIR_LIST_SYNC_TIMEOUT))
                {
                    StopDirectoryList();
                }
                else
                {
                    DirectoryLoadingProgressBar.Visibility =
                    UnfinishedBlock.Visibility = Visibility.Visible;

                    UpdateDirectoryList();
                    dirListUpdateTimer.Start();
                    listDirTask.ContinueWith((t) => Application.Current?.Dispatcher.BeginInvoke(() => StopDirectoryList()));
                }
            });
        }

        private void UpdateDirectoryList()
        {
            if (listDirTask is null) return;

            bool wasEmpty = (AndroidFileList.Count == 0);

            var newFiles = waitingFileStats.DequeueAllExisting().Select(f => FileClass.GenerateAndroidFile(f)).ToArray();
            AndroidFileList.AddRange(newFiles);
            var unknownFiles = newFiles.Where(f => f.Type == FileType.Unknown);
            StartDetermineFolders(unknownFiles);

            if (wasEmpty && (AndroidFileList.Count > 0))
            {
                ExplorerGrid.ScrollIntoView(ExplorerGrid.Items[0]);
            }
        }

        private void StopDirectoryList()
        {
            if (listDirTask is null) return;

            Cursor = null;
            DirectoryLoadingProgressBar.Visibility =
            UnfinishedBlock.Visibility = Visibility.Collapsed;

            dirListUpdateTimer.Stop();
            dirListCancelTokenSource.Cancel();
            listDirTask.Wait();
            UpdateDirectoryList();
            listDirTask = null;
            dirListCancelTokenSource = null;
        }

        private void StartDetermineFolders(IEnumerable<FileClass> files)
        {
            unknownFoldersTask.ContinueWith((t) =>
            {
                if (determineFoldersCancelTokenSource != null)
                {
                    DetermineFolders(files, determineFoldersCancelTokenSource.Token);
                }
            });
        }

        private static void DetermineFolders(IEnumerable<FileClass> files, CancellationToken cancellationToken)
        {
            foreach (var file in files)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                else if (CurrentADBDevice.IsDirectory(file.FullPath))
                {
                    Application.Current?.Dispatcher.BeginInvoke(() => { file.Type = FileType.Folder; });
                }
            }
        }

        private void StopDetermineFolders()
        {
            determineFoldersCancelTokenSource.Cancel();
            unknownFoldersTask.Wait();
            determineFoldersCancelTokenSource = null;
        }

        public bool NavigateToPath(string path, bool bfNavigated = false)
        {
            ExplorerGrid.Focus();
            if (path is null) return false;

            string realPath;
            try
            {
                realPath = CurrentADBDevice.TranslateDevicePath(path);
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message, "Navigation Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            if (!bfNavigated)
                NavHistory.Navigate(realPath);

            UpdateNavButtons();

            PathBox.Tag =
            CurrentPath = realPath;
            PopulateButtons(realPath);
            ParentPath = CurrentADBDevice.TranslateDeviceParentPath(CurrentPath);

            ParentButton.IsEnabled = CurrentPath != ParentPath;

            StartDirectoryList(realPath);
            return true;
        }

        private void UpdateNavButtons()
        {
            BackButton.IsEnabled = NavHistory.BackAvailable;
            ForwardButton.IsEnabled = NavHistory.ForwardAvailable;
        }

        private void DirListUpdateTimer_Tick(object sender, EventArgs e)
        {
            UpdateDirectoryList();
        }

        private void PopulateButtons(string path)
        {
            if (string.IsNullOrEmpty(path))
                return;

            var expectedLength = 0.0;
            List<MenuItem> tempButtons = new();
            List<string> pathItems = new();

            // On special cases, cut prefix of the path and replace with a pretty button
            var specialPair = CurrentPrettyNames.FirstOrDefault(kv => path.StartsWith(kv.Key));
            if (specialPair.Key != null)
            {
                MenuItem button = CreatePathButton(specialPair);
                tempButtons.Add(button);
                pathItems.Add(specialPair.Key);
                path = path[specialPair.Key.Length..].TrimStart('/');
                expectedLength = PathButtonLength.ButtonLength(button);
            }

            var dirs = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            foreach (var dir in dirs)
            {
                pathItems.Add(dir);
                var dirPath = string.Join('/', pathItems).Replace("//", "/");
                MenuItem button = CreatePathButton(dirPath, dir);
                tempButtons.Add(button);
                expectedLength += PathButtonLength.ButtonLength(button);
            }

            expectedLength += (tempButtons.Count - 1) * PathButtonLength.ButtonLength(CreatePathArrow());

            int i = 0;
            for (; i < PathButtons.Count && i < tempButtons.Count; i++)
            {
                var oldB = PathButtons[i];
                var newB = tempButtons[i];
                if (oldB.Header.ToString() != newB.Header.ToString() ||
                    oldB.Tag.ToString() != newB.Tag.ToString())
                {
                    break;
                }
            }
            PathButtons.RemoveRange(i, PathButtons.Count - i);
            PathButtons.AddRange(tempButtons.GetRange(i, tempButtons.Count - i));

            // StackPanel's margin is 10, while TextBox's margins is 6, thus the offset is 4x2
            ConsolidateButtons(expectedLength + 8);
        }

        private void ConsolidateButtons(double expectedLength)
        {
            if (expectedLength > PathBox.ActualWidth)
                expectedLength += PathButtonLength.ButtonLength(CreateExcessButton());

            double excessLength = expectedLength - PathBox.ActualWidth;
            List<MenuItem> excessButtons = new();
            PathStackPanel.Children.Clear();

            if (excessLength > -10)
            {
                int i = 0;
                while (excessLength >= -10 && PathButtons.Count - excessButtons.Count > 1)
                {
                    excessButtons.Add(PathButtons[i]);
                    PathButtons[i].ContextMenu = null;
                    excessLength -= PathButtonLength.ButtonLength(PathButtons[i]);

                    i++;
                }

                AddExcessButton(excessButtons);
            }

            foreach (var item in PathButtons.Except(excessButtons))
            {
                if (PathStackPanel.Children.Count > 0)
                    AddPathArrow();

                item.ContextMenu = Resources["PathButtonsMenu"] as ContextMenu;
                AddPathButton(item);
            }
        }

        private MenuItem CreateExcessButton() => new MenuItem()
        {
            Height = 24,
            Header = new FontIcon() { Glyph = "\uE712", FontSize = 16, Style = Resources["GlyphFont"] as Style, ContextMenu = Resources["PathButtonsMenu"] as ContextMenu }
        };

        private void AddExcessButton(List<MenuItem> excessButtons = null)
        {
            if (excessButtons is not null && !excessButtons.Any())
                return;

            var button = CreateExcessButton();
            button.ItemsSource = excessButtons;
            Menu excessMenu = new() { Height = 24 };
            excessMenu.ContextMenuOpening += PathButton_ContextMenuOpening;

            excessMenu.Items.Add(button);
            PathStackPanel.Children.Add(excessMenu);
        }

        private MenuItem CreatePathButton(KeyValuePair<string, string> kv) => CreatePathButton(kv.Key, kv.Value);
        private MenuItem CreatePathButton(object path, string name)
        {
            MenuItem button = new()
            {
                Header = new TextBlock() { Text = name, Margin = new(0, 0, 0, 1) },
                Tag = path,
                Padding = new Thickness(10, 0, 10, 0),
                Height = 24,
            };
            button.Click += PathButton_Click;
            button.ContextMenuOpening += PathButton_ContextMenuOpening;

            return button;
        }

        private void PathButton_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            InitializePathContextMenu((FrameworkElement)sender);
        }

        private void AddPathButton(MenuItem button)
        {
            Menu menu = new() { Height = 24 };
            ((Menu)button.Parent)?.Items.Clear();
            menu.Items.Add(button);
            PathStackPanel.Children.Add(menu);
        }

        private FontIcon CreatePathArrow() => new()
        {
            Glyph = " \uE970 ",
            FontSize = 8,
            Style = Resources["GlyphFont"] as Style,
            ContextMenu = Resources["PathButtonsMenu"] as ContextMenu
        };

        private void AddPathArrow(bool append = true)
        {
            var arrow = CreatePathArrow();
            arrow.ContextMenuOpening += PathButton_ContextMenuOpening;

            if (append)
                PathStackPanel.Children.Add(arrow);
            else
                PathStackPanel.Children.Insert(0, arrow);
        }

        private void PathButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem item)
            {
                if (item.Tag is string path and not "")
                    NavigateToPath(path);
                else if (item.Tag is LogicalDevice)
                    RefreshDrives(true);
            }
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            UnfocusPathBox();
            SettingsSplitView.IsPaneOpen = true;
        }

        private void ParentButton_Click(object sender, RoutedEventArgs e)
        {
            NavigateToPath(ParentPath);
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            NavigateToPath(NavHistory.GoBack(), true);
        }

        private void ForwardButton_Click(object sender, RoutedEventArgs e)
        {
            NavigateToPath(NavHistory.GoForward(), true);
        }

        private void Window_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.XButton1)
                NavigateToPath(NavHistory.GoBack(), true);
            else if (e.ChangedButton == MouseButton.XButton2)
                NavigateToPath(NavHistory.GoForward(), true);
        }

        private void DataGridRow_KeyDown(object sender, KeyEventArgs e)
        {
            var key = e.Key;
            if (key == Key.Enter)
            {
                if (ExplorerGrid.SelectedItems.Count == 1)
                    DoubleClick(ExplorerGrid.SelectedItem);
            }
            else if (key == Key.Back)
            {
                NavigateToPath(NavHistory.GoBack(), true);
            }
            else
                return;

            e.Handled = true;
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (ExplorerGrid.Items.Count < 1) return;

            var key = e.Key;
            if (key == Key.Down)
            {
                if (ExplorerGrid.SelectedItems.Count == 0)
                    ExplorerGrid.SelectedIndex = 0;
                else
                    ExplorerGrid.SelectedIndex++;

                ExplorerGrid.ScrollIntoView(ExplorerGrid.SelectedItem);
            }
            else if (key == Key.Up)
            {
                if (ExplorerGrid.SelectedItems.Count == 0)
                    ExplorerGrid.SelectedItem = ExplorerGrid.Items[^1];
                else if (ExplorerGrid.SelectedIndex > 0)
                    ExplorerGrid.SelectedIndex--;

                ExplorerGrid.ScrollIntoView(ExplorerGrid.SelectedItem);
            }
            else if (key == Key.Enter)
            {
                if (ExplorerGrid.SelectedItems.Count == 1)
                    DoubleClick(ExplorerGrid.SelectedItem);
            }
            else if (key == Key.Back)
            {
                NavigateToPath(NavHistory.GoBack(), true);
            }
            else
                return;

            e.Handled = true;
        }

        private void CopyMenuButton_Click(object sender, RoutedEventArgs e)
        {
            
        }

        private void CopyFiles(bool quick = false)
        {
            int itemsCount = ExplorerGrid.SelectedItems.Count;
            ShellObject path;

            if (quick)
            {
                path = ShellObject.FromParsingName(DefaultFolderBlock.Text);
            }
            else
            {
                var dialog = new CommonOpenFileDialog()
                {
                    IsFolderPicker = true,
                    Multiselect = false,
                    DefaultDirectory = DefaultFolderBlock.Text,
                    Title = "Select destination for " + (itemsCount > 1 ? "multiple items" : ExplorerGrid.SelectedItem)
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
                    MessageBox.Show(e.Message, "Destination Path Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            foreach (FileClass item in ExplorerGrid.SelectedItems)
            {
                fileOperationQueue.AddOperation(new FilePullOperation(Dispatcher, CurrentADBDevice, item, dirPath));
            }
        }

        private void PasteFiles(bool isFolderPicker)
        {
            var dialog = new CommonOpenFileDialog()
            {
                IsFolderPicker = isFolderPicker,
                Multiselect = true,
                DefaultDirectory = DefaultFolderBlock.Text,
                Title = $"Select {(isFolderPicker ? "folder" : "file")}s to copy"
            };

            if (dialog.ShowDialog() != CommonFileDialogResult.Ok)
                return;

            foreach (var item in dialog.FilesAsShellObject)
            {
                fileOperationQueue.AddOperation(new FilePushOperation(Dispatcher, CurrentADBDevice, new FilePath(item), new FilePath(CurrentPath)));
            }
        }

        private void TestCurrentOperation()
        {
            //fileOperationQueue.Clear();
            //fileOperationQueue.AddOperation(InProgressTestOperation.CreateProgressStart(Dispatcher, CurrentADBDevice, "Shalom.exe"));
            //fileOperationQueue.AddOperation(InProgressTestOperation.CreateFileInProgress(Dispatcher, CurrentADBDevice, "Shalom.exe"));
            //fileOperationQueue.AddOperation(InProgressTestOperation.CreateFolderInProgress(Dispatcher, CurrentADBDevice, "Shalom"));
        }

        private void TestDevices()
        {
            //ConnectTimer.IsEnabled = false;
            //DevicesObject.UpdateDevices(new List<LogicalDevice>() { LogicalDevice.New("Test", "test.ID", "offline") });
        }

        private void LightThemeRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            SetTheme(ApplicationTheme.Light);
        }

        private void DarkThemeRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            SetTheme(ApplicationTheme.Dark);
        }

        private void ContextMenuCopyItem_Click(object sender, RoutedEventArgs e)
        {
            CopyFiles();
        }

        private void DataGridRow_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var row = sender as DataGridRow;

            if (row.IsSelected == false)
            {
                ExplorerGrid.SelectedItems.Clear();
            }

            if (e.ChangedButton == MouseButton.Right)
            {
                InitializeExplorerContextMenu(e.OriginalSource is Border ? MenuType.EmptySpace : MenuType.ExplorerItem, row.DataContext);
            }

            if (e.OriginalSource is Border)
                return;

            row.IsSelected = true;
        }

        private void ChangeDefaultFolderButton_Click(object sender, MouseButtonEventArgs e)
        {
            var dialog = new CommonOpenFileDialog()
            {
                IsFolderPicker = true,
                Multiselect = false
            };
            if (DefaultFolderBlock.Text != "")
                dialog.DefaultDirectory = DefaultFolderBlock.Text;

            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                DefaultFolderBlock.Text = dialog.FileName;
                Storage.StoreValue(UserPrefs.defaultFolder, dialog.FileName);
            }
        }

        private void PullOnDoubleClickCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            Storage.StoreValue(UserPrefs.pullOnDoubleClick, PullOnDoubleClickCheckBox.IsChecked);
        }

        private void OpenDevicesButton_Click(object sender, RoutedEventArgs e)
        {
            UnfocusPathBox();
            DevicesSplitView.IsPaneOpen = true;
        }

        private void ConnectNewDeviceButton_Click(object sender, RoutedEventArgs e)
        {
            ConnectNewDevice();
        }

        private void ConnectNewDevice()
        {
            string deviceAddress = $"{NewDeviceIpBox.Text}:{NewDevicePortBox.Text}";
            try
            {
                ADBService.ConnectNetworkDevice(deviceAddress);
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains($"failed to connect to {deviceAddress}"))
                {
                    ManualPairingPanel.IsEnabled = true;
                    ManualPairingPortBox.Clear();
                    ManualPairingCodeBox.Clear();
                }
                else
                    MessageBox.Show(ex.Message, "Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);

                return;
            }

            if (RememberIpCheckBox.IsChecked == true)
                Storage.StoreValue(UserPrefs.lastIp, NewDeviceIpBox.Text);

            if (RememberPortCheckBox.IsChecked == true)
                Storage.StoreValue(UserPrefs.lastPort, NewDevicePortBox.Text);

            NewDeviceIpBox.Clear();
            NewDevicePortBox.Clear();
            PairingExpander.IsExpanded = false;
            DeviceListSetup(selectedAddress: deviceAddress);
        }

        private void EnableConnectButton()
        {
            ConnectNewDeviceButton.IsEnabled = NewDeviceIpBox.Text is string ip
                && !string.IsNullOrWhiteSpace(ip)
                && ip.Count(c => c == '.') == 3
                && ip.Split('.').Count(i => byte.TryParse(i, out _)) == 4
                && NewDevicePortBox.Text is string port
                && !string.IsNullOrWhiteSpace(port)
                && ushort.TryParse(port, out _);
        }

        private void EnablePairButton()
        {
            PairNewDeviceButton.IsEnabled = ManualPairingPortBox.Text is string port
                && !string.IsNullOrWhiteSpace(port)
                && ushort.TryParse(port, out _)
                && ManualPairingCodeBox.Text is string text
                && text.Replace("-", "") is string password
                && !string.IsNullOrWhiteSpace(password)
                && uint.TryParse(password, out _)
                && password.Length == 6;
        }

        private void NewDeviceIpBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            TextBoxSeparation(sender as TextBox, ref TextBoxChangedMutex, numeric:true, allowedChars: '.');
            EnableConnectButton();
        }

        private void RetrieveIp()
        {
            if (!string.IsNullOrWhiteSpace(NewDeviceIpBox.Text) && !string.IsNullOrWhiteSpace(NewDevicePortBox.Text))
                return;

            if (RememberIpCheckBox.IsChecked == true
                && Storage.RetrieveValue(UserPrefs.lastIp) is string lastIp
                && !DevicesObject.UIList.Find(d => d.Device.ID.Split(':')[0] == lastIp))
            {
                NewDeviceIpBox.Text = lastIp;
                if (RememberPortCheckBox.IsChecked == true
                    && Storage.RetrieveValue(UserPrefs.lastPort) is string lastPort)
                {
                    NewDevicePortBox.Text = lastPort;
                }
            }
        }

        private void RememberIpCheckBox_Click(object sender, RoutedEventArgs e)
        {
            RememberPortCheckBox.IsEnabled = (bool)RememberIpCheckBox.IsChecked;
            if (!RememberPortCheckBox.IsEnabled)
                RememberPortCheckBox.IsChecked = false;

            Storage.StoreValue(UserPrefs.rememberIp, RememberIpCheckBox.IsChecked);
        }

        private void OpenDeviceButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is UILogicalDevice device && device.Device.Status != AbstractDevice.DeviceStatus.Offline)
            {
                DevicesObject.SetOpen(device);
                CurrentADBDevice = new(device);

                ClearExplorer();
                InitDevice();

                DevicesSplitView.IsPaneOpen = false;
            }
        }

        private void DisconnectDeviceButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is UILogicalDevice device)
            {
                RemoveDevice(device);
            }
        }

        private void RemoveDevice(UILogicalDevice device)
        {
            try
            {
                if (device.Device.Type == AbstractDevice.DeviceType.Emulator)
                {
                    ADBService.KillEmulator(device.Device.ID);
                }
                else
                {
                    ADBService.DisconnectNetworkDevice(device.Device.ID);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Disconnection Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (device.IsOpen)
            {
                ClearDrives();
                ClearExplorer();
                DevicesObject.SetOpen(device, false);
                CurrentADBDevice = null;
            }
            DeviceListSetup();
        }

        private void ClearExplorer()
        {
            CurrentPrettyNames.Clear();
            AndroidFileList.Clear();
            ExplorerGrid.Items.Refresh();
            PathStackPanel.Children.Clear();
            CurrentPath = null;
            PathBox.Tag = null;
            NavHistory.Reset();
            PushMenuButton.IsEnabled =
            PathBox.IsEnabled =
            NewMenuButton.IsEnabled =
            PullMenuButton.IsEnabled =
            BackButton.IsEnabled =
            ForwardButton.IsEnabled =
            HomeButton.IsEnabled =
            ParentButton.IsEnabled = false;
        }

        private void ClearDrives()
        {
            DevicesObject.CurrentDevice?.Drives.Clear();
            DrivesItemRepeater.ItemsSource = null;
        }

        private void DevicesSplitView_PaneClosing(SplitView sender, SplitViewPaneClosingEventArgs args)
        {
            PairingExpander.IsExpanded = false;
            DevicesObject.UnselectAll();
        }

        private void NewDeviceIpBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (ConnectNewDeviceButton.Visible() && ConnectNewDeviceButton.IsEnabled)
                    ConnectNewDevice();
                else if (PairNewDeviceButton.Visible() && PairNewDeviceButton.IsEnabled)
                    PairDeviceManual();    
            }
                
        }

        private void RememberPortCheckBox_Click(object sender, RoutedEventArgs e)
        {
            Storage.StoreValue(UserPrefs.rememberPort, RememberPortCheckBox.IsChecked);
        }

        private void ExplorerGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            ExplorerGrid.ContextMenu.Visibility = Visible(ExplorerGrid.ContextMenu.HasItems);
        }

        private void SelectDevice(object sender)
        {
            if (sender is ModernWpf.Controls.ListViewItem item && item.DataContext is UIDevice device && !device.DeviceSelected)
            {
                if (device is UIServiceDevice service && (PasswordConnectionRadioButton.IsChecked == false || string.IsNullOrEmpty(((ServiceDevice)service.Device).PairingPort)))
                    return;

                DevicesObject.SetSelected(device);
            }
        }

        private void AutoOpenCheckBox_Click(object sender, RoutedEventArgs e)
        {
            Storage.StoreValue(UserPrefs.autoOpen, AutoOpenCheckBox.IsChecked);
        }

        private void ExplorerGrid_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var point = e.GetPosition(ExplorerGrid);
            var actualRowWidth = 0.0;
            foreach (var item in ExplorerGrid.Columns)
            {
                actualRowWidth += item.ActualWidth;
            }

            if (point.Y > ExplorerGrid.Items.Count * ExplorerGrid.MinRowHeight
                || point.Y < ColumnHeaderHeight
                || point.X > actualRowWidth
                || point.X > DataGridContentWidth)
            {
                ExplorerGrid.SelectedItems.Clear();

                if (point.Y < ColumnHeaderHeight || point.X > DataGridContentWidth)
                    InitializeExplorerContextMenu(MenuType.Header);
            }
        }

        private void ExplorerGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            DependencyObject DepObject = (DependencyObject)e.OriginalSource;
            ExplorerGrid.ContextMenu.IsOpen = false;
            ExplorerGrid.ContextMenu.Visibility = Visibility.Collapsed;

            while (DepObject is not null and not DataGridColumnHeader)
            {
                DepObject = VisualTreeHelper.GetParent(DepObject);
            }

            if (DepObject is DataGridColumnHeader)
            {
                InitializeExplorerContextMenu(MenuType.Header);
            }
            else if (e.OriginalSource is FrameworkElement element && element.DataContext is FileClass file && ExplorerGrid.SelectedItems.Count > 0)
            {
                InitializeExplorerContextMenu(MenuType.ExplorerItem, file);
            }
            else
                InitializeExplorerContextMenu(MenuType.EmptySpace);
        }

        private void DevicesSplitView_PaneOpening(SplitView sender, object args)
        {
            PairingExpander.IsExpanded = false;
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            PopulateButtons((string)PathBox.Tag);
            ResizeDetailedView();
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
            FileOpVisibility(null);
        }

        private void FileOpVisibility(bool? value = null)
        {
            if (value is not null)
            {
                FileOperationsButton.Tag = value;
                return;
            }

            FileOperationsButton.Tag = !FileOpVisibility();
        }

        private bool FileOpVisibility()
        {
            return FileOperationsButton.Tag is bool and true;
        }

        private void PairingCodeBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            TextBoxSeparation(sender as TextBox, ref TextBoxChangedMutex, '-', 6);
            EnablePairButton();
        }

        private void PairNewDeviceButton_Click(object sender, RoutedEventArgs e)
        {
            PairDeviceManual();
        }

        private void PairDeviceManual()
        {
            try
            {
                ADBService.PairNetworkDevice($"{NewDeviceIpBox.Text}:{ManualPairingPortBox.Text}", ManualPairingCodeBox.Text.Replace("-", ""));
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Pairing Error", MessageBoxButton.OK, MessageBoxImage.Error);
                (FindResource("PairServiceFlyout") as Flyout).Hide();
                return;
            }

            ConnectNewDevice();
            ManualPairingPanel.IsEnabled = false;
            PairingExpander.IsExpanded = false;
        }

        private void HomeButton_Click(object sender, RoutedEventArgs e)
        {
            UnfocusPathBox();
            RefreshDrives();
            DriveViewNav();
        }

        private void RefreshDrives(bool findMmc = false)
        {
            DevicesObject.CurrentDevice.SetDrives(CurrentADBDevice.GetDrives(), findMmc);
            DrivesItemRepeater.ItemsSource = DevicesObject.CurrentDevice.Drives;
        }

        private void PushMenuButton_Click(object sender, RoutedEventArgs e)
        {
            UnfocusPathBox();
            PasteFiles(((MenuItem)sender).Name == nameof(PasteFoldersMenu));
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
            ((MenuItem)sender).FindAscendant<SplitView>().IsPaneOpen = false;
        }

        private void PathMenuEdit_Click(object sender, RoutedEventArgs e)
        {
            PathBox.Focus();
        }

        private void GridBackgroundBlock_MouseDown(object sender, MouseButtonEventArgs e)
        {
            UnfocusPathBox();
        }

        private void DriveItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is ModernWpf.Controls.ListViewItem item && item.DataContext is Drive drive)
            {
                InitNavigation(drive.Path);
            }
        }

        private void PathMenuCopy_Click(object sender, RoutedEventArgs e)
        {
            object tag = PathBox.Tag;
            Clipboard.SetText(tag is null ? "" : (string)tag);
        }

        private void ShowHiddenCheckBox_Click(object sender, RoutedEventArgs e)
        {
            Storage.StoreValue(UserPrefs.showHiddenItems, ShowHiddenCheckBox.IsChecked);
        }

        private void ShowExtensionsCheckBox_Click(object sender, RoutedEventArgs e)
        {
            Storage.StoreValue(UserPrefs.showExtensions, ShowExtensionsCheckBox.IsChecked);
        }

        private void PullMenuButton_Click(object sender, RoutedEventArgs e)
        {
            UnfocusPathBox();
            CopyFiles();
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

        private void FileOpDetailedRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            Storage.StoreValue(UserPrefs.showExtendedView, true);
        }

        private void FileOpCompactRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            Storage.StoreValue(UserPrefs.showExtendedView, false);
        }

        private void ConnectionTypeRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            ChangeConnectionType();
        }

        private void ChangeConnectionType()
        {
            if (ManualConnectionRadioButton.IsChecked == false
                            && PairingExpander.IsExpanded
                            && MdnsService.State == MDNS.MdnsState.Disabled)
            {
                MdnsService.State = MDNS.MdnsState.Unchecked;
                MdnsCheck();
            }
            else if (ManualConnectionRadioButton.IsChecked == true)
            {
                MdnsService.State = MDNS.MdnsState.Disabled;
                DevicesObject.UIList.RemoveAll(device => device is UIServiceDevice);
                //DevicesList?.Items.Refresh();
            }

            if (QrConnectionRadioButton?.IsChecked == true)
            {
                UpdateQrClass();
            }

            if (ManualPairingPanel is not null)
            {
                ManualPairingPanel.IsEnabled = false;
            }
        }

        private void UpdateQrClass()
        {
            if (qrBackground == null)
                SetTheme();

            QrClass.Background = qrBackground;
            QrClass.Foreground = qrForeground;
            PairingQrImage.Source = QrClass.Image;
        }

        private static void TextBoxSeparation(TextBox textBox,
                                              ref bool inProgress,
                                              char? separator = null,
                                              int maxChars = -1,
                                              bool numeric = true,
                                              params char[] allowedChars)
        {
            if (inProgress)
                return;
            else
                inProgress = true;

            var caretIndex = textBox.CaretIndex;
            var output = "";
            var numbers = "";
            var deletedChars = 0;
            var text = textBox.Text;

            if (numeric)
            {
                foreach (var c in text)
                {
                    if (!char.IsDigit(c) && !allowedChars.Contains(c))
                    {
                        if (c != separator)
                            deletedChars++;

                        continue;
                    }

                    numbers += c;
                }
            }

            if (separator is null)
            {
                output = numbers;
            }
            else
            {
                for (int i = 0; i < numbers.Length; i++)
                {
                    output += $"{(i > 0 ? separator : "")}{numbers[i]}";
                }
            }

            if (deletedChars > 0 && textBox.Tag is string prev && prev.Length > output.Length)
            {
                textBox.Text = prev;
                textBox.CaretIndex = caretIndex - deletedChars;
                return;
            }

            if (maxChars > -1)
                textBox.MaxLength = separator is null ? maxChars : (maxChars * 2) - 1;

            textBox.Text = output;

            if ($"{textBox.Tag}" != output)
            {
                caretIndex -= deletedChars;
                if (separator is not null)
                    caretIndex += output.Count(c => c == separator) - text.Count(c => c == separator);

                if (caretIndex < 0)
                    caretIndex = 0;
            }

            textBox.CaretIndex = caretIndex;

            textBox.Tag = output;

            inProgress = false;
        }

        private static string NumericText(string text)
        {
            var output = "";
            foreach (var c in text)
            {
                if (!char.IsDigit(c))
                    continue;

                output += c;
            }

            return output;
        }

        private void PairingCodeTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            TextBoxSeparation(sender as TextBox, ref TextBoxChangedMutex, '-', 6);
        }

        private void NewDevicePortBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            TextBoxSeparation(sender as TextBox, ref TextBoxChangedMutex, maxChars:5);
            EnableConnectButton();
        }

        private void PairServiceButton_Click(object sender, RoutedEventArgs e)
        {
            PairService((ServiceDevice)DevicesObject.SelectedDevice.Device);
        }

        private bool PairService(ServiceDevice service)
        {
            var code = service.MdnsType == ServiceDevice.ServiceType.QrCode
                ? QrClass.Password
                : service.PairingCode;

            try
            {
                ADBService.PairNetworkDevice(service.ID, code);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Pairing Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            return true;
        }

        private void ManualPairingPortBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            TextBoxSeparation(sender as TextBox, ref TextBoxChangedMutex, maxChars: 5);
            EnablePairButton();
        }

        private void CancelManualPairing_Click(object sender, RoutedEventArgs e)
        {
            ManualPairingPanel.IsEnabled = false;
        }

        private void PairFlyoutCloseButton_Click(object sender, RoutedEventArgs e)
        {
            if (FindResource("PairServiceFlyout") is Flyout flyout)
            {
                flyout.Hide();
                PairingExpander.IsExpanded = false;
                DevicesObject.UnselectAll();
                DevicesObject.ConsolidateDevices();
            }
        }

        private void PairingCodeTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                PairService((ServiceDevice)DevicesObject.SelectedDevice.Device);
            }
        }

        private void EnableMdnsCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            // Intentionally invoked from InitSettings

            bool isChecked = EnableMdnsCheckBox.IsChecked == true;
            Storage.StoreValue(UserPrefs.enableMdns, isChecked);

            ADBService.IsMdnsEnabled = isChecked;
            if (isChecked)
            {
                QrClass = new();
            }
            else
            {
                ManualConnectionRadioButton.IsChecked = true;
            }
        }

        private void RemovePending_Click(object sender, RoutedEventArgs e)
        {
            fileOperationQueue.PendingOperations.Clear();
        }

        private void RemoveCompleted_Click(object sender, RoutedEventArgs e)
        {
            fileOperationQueue.CompletedOperations.Clear();
        }

        private void OpenDefaultFolder_Click(object sender, RoutedEventArgs e)
        {
            Process.Start("explorer.exe", DefaultFolderBlock.Text);
        }

        private void RemovePendingAndCompleted_Click(object sender, RoutedEventArgs e)
        {
            fileOperationQueue.Clear();
        }

        private void StopFileOperations_Click(object sender, RoutedEventArgs e)
        {
            fileOperationQueue.Stop();
        }

        private void Window_SourceInitialized(object sender, EventArgs e)
        {
            if (Storage.RetrieveBool(SystemVals.windowMaximized) == true)
                WindowState = WindowState.Maximized;

            if (Storage.RetrieveBool(SystemVals.detailedVisible) is bool and true)
            {
                FileOpVisibility(true);
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
            ChangeConnectionType();
        }

        private void ManualAdbPath_MouseUp(object sender, MouseButtonEventArgs e)
        {
            var dialog = new OpenFileDialog()
            {
                Multiselect = false,
                Title = "Select ADB Executable",
                Filter = "ADB Executable|adb.exe",
            };

            if (ManualAdbPath.Text != "")
            {
                try
                {
                    dialog.InitialDirectory = Directory.GetParent(ManualAdbPath.Text).FullName;
                }
                catch (Exception) { }
            }

            if (dialog.ShowDialog() == true)
            {
                ManualAdbPath.Text = dialog.FileName;
                Storage.StoreValue(UserPrefs.manualAdbPath, dialog.FileName);
            }
        }

        private void PairingExpander_Expanded(object sender, RoutedEventArgs e)
        {
            DevicesObject.UnselectAll();

            if (PairingExpander.IsExpanded)
            {
                RetrieveIp();
            }
        }

        private void DeviceStyle_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            SelectDevice(sender);
        }

        private void DefaultThemeRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            SetTheme(themeService.WindowsTheme);
        }

        private void ForceFluentStylesCheckbox_Checked(object sender, RoutedEventArgs e)
        {
            SetTheme();
            Storage.StoreValue(UserPrefs.forceFluentStyles, ForceFluentStylesCheckbox.IsChecked);
        }

        private void Border_MouseDown(object sender, MouseButtonEventArgs e)
        {
            DevicesObject.UnselectAll();
        }

        private void AutoRootCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            Storage.StoreValue(UserPrefs.autoRoot, AutoRootCheckBox.IsChecked);
        }

        private void EnableDeviceRootToggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton toggle && toggle.DataContext is UILogicalDevice device && device.Device is LogicalDevice logical)
            {
                bool rootToggle = toggle.IsChecked.Value;
                var rootTask = Task.Run(() =>
                {
                    logical.EnableRoot(rootToggle);
                });
                rootTask.ContinueWith((t) => Dispatcher.BeginInvoke(() =>
                {
                    if (logical.Root is AbstractDevice.RootStatus.Forbidden)
                        MessageBox.Show("Root access cannot be enabled on selected device.", "Root Access", MessageBoxButton.OK, MessageBoxImage.Error);
                }));
            }
        }

        private void FileOperationsSplitView_PaneClosing(SplitView sender, SplitViewPaneClosingEventArgs args)
        {
            FileOperationsButton.Tag = false;
        }
    }
}
