using ADB_Explorer.Converters;
using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;
using Microsoft.WindowsAPICodePack.Dialogs;
using ModernWpf;
using ModernWpf.Controls;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
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
        private Task listDirTask;
        private Task unknownFoldersTask;
        private Task<ADBService.Device.AdbSyncStatsInfo> syncOprationTask;
        private DispatcherTimer dirListUpdateTimer;
        private DispatcherTimer syncOprationProgressUpdateTimer;
        private bool isPullInProgress = false;
        private CancellationTokenSource dirListCancelTokenSource;
        private CancellationTokenSource determineFoldersCancelTokenSource;
        private CancellationTokenSource syncOperationCancelTokenSource;
        private ConcurrentQueue<FileStat> waitingFileStats;
        private ConcurrentQueue<ADBService.Device.AdbSyncProgressInfo> waitingProgress;

        public static Visibility Visible(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

        private string SelectedFilesTotalSize
        {
            get
            {
                var files = ExplorerGrid.SelectedItems.OfType<FileClass>().ToList();
                if (files.Any(i => i.Type != FileStat.FileType.File)) return "0";

                ulong totalSize = 0;
                files.ForEach(f => totalSize += f.Size.GetValueOrDefault(0));

                return totalSize.ToSize();
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            LaunchSequence();

            ConnectTimer.Interval = CONNECT_TIMER_INTERVAL;
            ConnectTimer.Tick += ConnectTimer_Tick;
            ConnectTimer.Start();

            InputLanguageManager.Current.InputLanguageChanged +=
                new InputLanguageEventHandler((sender, e) =>
                {
                    UpdateInputLang();
                });
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
        }

        private void SetTheme(object theme) => SetTheme((ApplicationTheme)theme);

        private void SetTheme(ApplicationTheme theme)
        {
            ThemeManager.Current.ApplicationTheme = theme;

            GridBackgroundBlock.Style = FindResource($"TextBlock{theme}Style") as Style;
            ExplorerGrid.RowStyle = FindResource($"Row{theme}Style") as Style;
            ExplorerGrid.CellStyle = FindResource($"Cell{theme}Style") as Style;
            DevicesList.ItemContainerStyle = FindResource($"Device{theme}Style") as Style;

            Storage.StoreEnum(ThemeManager.Current.ApplicationTheme);
        }

        private void PathBox_GotFocus(object sender, RoutedEventArgs e)
        {
            PathStackPanel.Visibility = Visibility.Collapsed;
            PathBox.Text = PathBox.Tag?.ToString();
            PathBox.IsReadOnly = false;
            PathBox.SelectAll();

            UpdateInputLang();
        }

        private void PathBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && NavigateToPath(PathBox.Text))
            {
                ExplorerGrid.Focus();
            }
        }

        private void PathBox_LostFocus(object sender, RoutedEventArgs e)
        {
            PathStackPanel.Visibility = Visibility.Visible;
            PathBox.Text = "";
            PathBox.IsReadOnly = true;
        }

        private void DataGridRow_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DoubleClick(ExplorerGrid.SelectedItem);
        }

        private void DoubleClick(object source)
        {
            if (source is FileClass file)
            {
                switch (file.Type)
                {
                    case FileStat.FileType.File:
                        if (CopyOnDoubleClickCheckBox.IsChecked == true)
                            CopyFiles(true);
                        break;
                    case FileStat.FileType.Folder:
                        NavigateToPath(file.Path);
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
            CopyMenuButton.IsEnabled = items.Any() && items.All(f => f.Type
                is FileStat.FileType.File
                or FileStat.FileType.Folder);
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            ExplorerGrid.Focus();
        }

        private void LaunchSequence()
        {
            var theme = Storage.RetrieveEnum<ApplicationTheme>();
            SetTheme(theme);
            if (theme == ApplicationTheme.Light)
                LightThemeRadioButton.IsChecked = true;
            else
                DarkThemeRadioButton.IsChecked = true;

            Title = Properties.Resources.AppDisplayName;
            LoadSettings();
            DeviceListSetup();
        }

        private void DeviceListSetup(string selectedAddress = "")
        {
            var init = true;

            Devices.RemoveAll(d => d.ID != CurrentDevice?.ID);
            var devices = ADBService.GetDevices();

            if (devices.Find(d => d.ID == CurrentDevice?.ID) is DeviceClass curr)
            {
                if (CurrentDevice.Type == curr.Type)
                    init = false;

                CurrentDevice.Type = curr.Type;
            }
            else
            {
                CurrentDevice = null;
                Devices.Clear();
            }

            Devices.AddRange(devices.Where(d => d.ID != CurrentDevice?.ID));
            DevicesList.ItemsSource = Devices;
            DevicesList.Items.Refresh();

            if (Devices.Count(d => d.Type is DeviceClass.DeviceType.Local or DeviceClass.DeviceType.Remote) == 0
                || (CurrentDevice && CurrentDevice.Type is not DeviceClass.DeviceType.Local and not DeviceClass.DeviceType.Remote))
            {
                Title = $"{Properties.Resources.AppDisplayName} - NO CONNECTED DEVICES";
                ClearExplorer();
                return;
            }
            else if (!CurrentDevice)
            {
                var connectedDevices = Devices.Where(d => d.Type is DeviceClass.DeviceType.Local or DeviceClass.DeviceType.Remote);
                if (connectedDevices.Count() == 1)
                    CurrentDevice = connectedDevices.First();
                else
                {
                    var selectedDevice = connectedDevices?.Where(d => d.ID == selectedAddress);
                    if (selectedDevice.Any())
                        CurrentDevice = selectedDevice.First();
                }

                if (!CurrentDevice)
                    return;
            }

            foreach (var item in Devices.Where(d => d.ID != CurrentDevice.ID))
            {
                item.IsOpen = item.IsSelected = false;
            }

            CurrentDevice.IsOpen = true;
            CurrentADBDevice = new(CurrentDevice.ID);
            if (init)
                InitDevice();
        }

        private void LoadSettings()
        {
            if (Storage.RetrieveValue(Settings.defaultFolder) is string path && !string.IsNullOrEmpty(path))
                DefaultFolderBlock.Text = path;

            if (Storage.RetrieveBool(Settings.copyOnDoubleClick) is bool copy)
                CopyOnDoubleClickCheckBox.IsChecked = copy;

            if (Storage.RetrieveBool(Settings.rememberIp) is bool remIp)
                RememberIpCheckBox.IsChecked = remIp;

            RememberPortCheckBox.IsEnabled = (bool)RememberIpCheckBox.IsChecked;

            if (RememberPortCheckBox.IsEnabled
                    && Storage.RetrieveBool(Settings.rememberPort) is bool remPort)
            {
                RememberPortCheckBox.IsChecked = remPort;
            }
        }

        private void InitDevice()
        {
            Title = $"{Properties.Resources.AppDisplayName} - {CurrentDevice.Name}";

            unknownFoldersTask = Task.Run(() => { });
            dirListCancelTokenSource = new CancellationTokenSource();
            determineFoldersCancelTokenSource = new CancellationTokenSource();
            waitingFileStats = new ConcurrentQueue<FileStat>();
            waitingProgress = new ConcurrentQueue<ADBService.Device.AdbSyncProgressInfo>();
            dirListUpdateTimer = new DispatcherTimer
            {
                Interval = DIR_LIST_UPDATE_INTERVAL
            };
            dirListUpdateTimer.Tick += DirListUpdateTimer_Tick;
            syncOprationProgressUpdateTimer = new DispatcherTimer
            {
                Interval = SYNC_PROG_UPDATE_INTERVAL
            };
            syncOprationProgressUpdateTimer.Tick += SyncOprationProgressUpdateTimer_Tick;

            ExplorerGrid.ItemsSource = AndroidFileList;
            PathBox.IsEnabled = true;

            if (string.IsNullOrEmpty(CurrentPath))
            {
                NavigateToPath(DEFAULT_PATH);
            }
            else if (AndroidFileList.Any())
            {
                PathBox.Tag = CurrentPath;
                PopulateButtons(CurrentPath);
            }
            else
                NavigateToPath(CurrentPath);

            ProgressCountTextBlock.Tag = 0;
        }

        private void ConnectTimer_Tick(object sender, EventArgs e)
        {
            var devices = ADBService.GetDevices();

            // do nothing if amount of devices and their types haven't changed
            if (devices is null
                || (devices.Count == Devices.Count
                && devices.All(d => Devices.Find(dev => dev.ID == d.ID)?.Type == d.Type)))
                return;

            DeviceListSetup();
        }

        private void StartDirectoryList(string path)
        {
            Cursor = Cursors.AppStarting;

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
                UnfinishedBlock.Visibility = Visibility.Visible;

                UpdateDirectoryList();
                dirListUpdateTimer.Start();
                listDirTask.ContinueWith((t) => Application.Current?.Dispatcher.BeginInvoke(() => StopDirectoryList()));
            }
        }

        private void UpdateDirectoryList()
        {
            if (listDirTask is null) return;

            bool wasEmpty = (AndroidFileList.Count == 0);

            var newFiles = waitingFileStats.DequeueAllExisting().Select(f => FileClass.GenerateAndroidFile(f)).ToArray();
            AndroidFileList.AddRange(newFiles);
            var unknownFiles = newFiles.Where(f => f.Type == FileStat.FileType.Unknown);
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

        private void DetermineFolders(IEnumerable<FileClass> files, CancellationToken cancellationToken)
        {
            foreach (var file in files)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                else if (CurrentADBDevice.IsDirectory(file.Path))
                {
                    Application.Current?.Dispatcher.BeginInvoke(() => { file.Type = FileStat.FileType.Folder; });
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
            PathStackPanel.Children.Clear();
            var pathItems = new List<string>();

            // On special cases, cut prefix of the path and replace with a pretty button
            var specialPair = SPECIAL_FOLDERS_PRETTY_NAMES.FirstOrDefault((kv) => path.StartsWith(kv.Key));
            if (specialPair.Key != null)
            {
                AddPathButton(specialPair.Key, specialPair.Value);
                pathItems.Add(specialPair.Key);
                path = path[specialPair.Key.Length..].TrimStart('/');
            }

            var dirs = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            foreach (var dir in dirs)
            {
                pathItems.Add(dir);
                var dirPath = string.Join('/', pathItems);
                var dirName = pathItems.Last();
                AddPathButton(dirPath, dirName);
            }
        }

        private void AddPathButton(string path, string name)
        {
            if (PathStackPanel.Children.Count > 0)
            {
                TextBlock tb = new()
                {
                    Text = " \uE970 ",
                    FontFamily = new("Segoe MDL2 Assets"),
                    FontSize = 7,
                };
                PathStackPanel.Children.Add(tb);
            }

            MenuItem button = new() { Header = name, Tag = path, Height = 24 };
            Menu menu = new() { Height = 24 };
            menu.Items.Add(button);
            button.Click += PathButton_Click;
            PathStackPanel.Children.Add(menu);
        }

        private void PathButton_Click(object sender, RoutedEventArgs e)
        {
            NavigateToPath((sender as MenuItem).Tag.ToString());
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
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

        private void UpdateInputLang()
        {
            InputLangBlock.Text = InputLanguageManager.Current.CurrentInputLanguage.TwoLetterISOLanguageName.ToUpper();
        }

        private void CopyMenuButton_Click(object sender, RoutedEventArgs e)
        {
            CopyFiles();
        }

        private void CopyFiles(bool quick = false)
        {
            int itemsCount = ExplorerGrid.SelectedItems.Count;
            string path;

            if (quick)
            {
                path = DefaultFolderBlock.Text;
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

                path = dialog.FileName;
            }

            int totalCount = (int)ProgressCountTextBlock.Tag + itemsCount;
            ProgressCountTextBlock.Tag = totalCount;
         
            foreach (FileClass item in ExplorerGrid.SelectedItems)
            {
                PullQ.Enqueue(new(path, item.Path));
            }

            ProgressCountTextBlock.Text = $"{totalCount - PullQ.Count + 1}/{totalCount}";

            // Initiate pull sequence if needed
            if (!isPullInProgress)
            {
                isPullInProgress = true;
                HandleNextPull();
            }
        }

        private void HandleNextPull()
        {
            OperationCompletedTextBlock.Text = "";
            OverallProgressBar.IsIndeterminate = true;
            if (ProgressGrid.Visibility == Visibility.Collapsed)
                ProgressGrid.Visibility = Visibility.Visible;

            if (ProgressCountTextBlock.Tag is int totalCount)
            {
                ProgressCountTextBlock.Text = $"{totalCount - PullQ.Count + 1}/{totalCount}";
            }

            var item = PullQ.Dequeue();

            waitingProgress = new ConcurrentQueue<ADBService.Device.AdbSyncProgressInfo>();
            syncOperationCancelTokenSource = new CancellationTokenSource();
            syncOprationTask = Task.Run(() => CurrentADBDevice.PullFile(item.Item1, item.Item2, ref waitingProgress, syncOperationCancelTokenSource.Token));

            syncOprationTask.ContinueWith((t) => Application.Current?.Dispatcher.BeginInvoke(() => AdbSyncCompleteHandler(t.Result)));
            syncOprationProgressUpdateTimer.Start();
        }

        private void SyncOprationProgressUpdateTimer_Tick(object sender, EventArgs e)
        {
            var currProgresses = waitingProgress.DequeueAllExisting();
            if (currProgresses.Any() && (currProgresses.LastOrDefault()?.TotalPrecentage is var percents) && percents.HasValue)
            {
                if (OverallProgressBar.IsIndeterminate)
                {
                    OverallProgressBar.IsIndeterminate = false;
                }
                OverallProgressBar.Value = percents.Value;
            }
        }

        private void AdbSyncCompleteHandler(ADBService.Device.AdbSyncStatsInfo statsInfo)
        {
            syncOprationProgressUpdateTimer.Stop();

            if (PullQ.Any())
            {
                HandleNextPull();
            }
            else
            {
                isPullInProgress = false;

                if ((int)ProgressCountTextBlock.Tag > 0)
                {
                    OperationCompletedTextBlock.Tag = ProgressCountTextBlock.Tag;
                }

                ProgressCountTextBlock.Tag = 0;

                ProgressGrid.Visibility = Visibility.Collapsed;
                var fileCount = (int)OperationCompletedTextBlock.Tag;
                OperationCompletedTextBlock.Text = $"{DateTime.Now:HH:mm:ss} - {fileCount} file{(fileCount > 1 ? "s" : "")} done";
            }
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
                if (e.OriginalSource is Border)
                    return;
            }

            ((DataGridRow)sender).IsSelected = true;
        }

        private void ChangeDefaultFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new CommonOpenFileDialog()
            {
                IsFolderPicker = true,
                Multiselect = false
            };
            if (DefaultFolderBlock.Text != "[not set]")
                dialog.DefaultDirectory = DefaultFolderBlock.Text;

            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                DefaultFolderBlock.Text = dialog.FileName;
                Storage.StoreValue(Settings.defaultFolder, dialog.FileName);
            }
        }

        private void CopyOnDoubleClickCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            Storage.StoreValue(Settings.copyOnDoubleClick, CopyOnDoubleClickCheckBox.IsChecked);
        }

        private void InputLangBlock_MouseDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
        }

        private void OpenDevicesButton_Click(object sender, RoutedEventArgs e)
        {
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
                MessageBox.Show(ex.Message, "Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (RememberIpCheckBox.IsChecked == true)
                Storage.StoreValue(Settings.lastIp, NewDeviceIpBox.Text);

            if (RememberPortCheckBox.IsChecked == true)
                Storage.StoreValue(Settings.lastPort, NewDevicePortBox.Text);

            NewDeviceIpBox.Text = "";
            NewDevicePortBox.Text = "";
            NewDevicePanel.Visibility = Visibility.Collapsed;

            ClearExplorer();
            DeviceListSetup(deviceAddress);
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

        private void NewDeviceIpBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            EnableConnectButton();
        }

        private void OpenNewDeviceButton_Click(object sender, RoutedEventArgs e)
        {
            NewDevicePanel.Visibility = Visible(!NewDevicePanel.IsVisible);
            Devices.ForEach(d => d.IsSelected = false);
            DevicesList.Items.Refresh();
        }

        private void RetrieveIp()
        {
            NewDeviceIpBox.Clear();
            NewDevicePortBox.Clear();

            if (RememberIpCheckBox.IsChecked == true
                && Storage.RetrieveValue(Settings.lastIp) is string lastIp
                && !Devices.Find(d => d.ID.Split(':')[0] == lastIp))
            {
                NewDeviceIpBox.Text = lastIp;
                if (RememberPortCheckBox.IsChecked == true
                    && Storage.RetrieveValue(Settings.lastPort) is string lastPort)
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

            Storage.StoreValue(Settings.rememberIp, RememberIpCheckBox.IsChecked);
        }

        private void OpenDeviceButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is DeviceClass device && device.Type != DeviceClass.DeviceType.Offline)
            {
                Devices.ForEach(d => d.IsOpen = false);
                device.IsOpen = true;
                CurrentDevice = device;
                CurrentADBDevice = new(CurrentDevice.ID);

                ClearExplorer();
                DevicesList.Items.Refresh();
                InitDevice();
            }
        }

        private void DisconnectDeviceButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is DeviceClass device)
            {
                try
                {
                    ADBService.DisconnectNetworkDevice(device.ID);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Disconnection Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (device.IsOpen)
                {
                    CurrentDevice = null;
                    CurrentADBDevice = null;
                    ClearExplorer();
                }
                DeviceListSetup();
                DevicesList.Items.Refresh();
            }
        }

        private void ClearExplorer()
        {
            AndroidFileList.Clear();
            ExplorerGrid.Items.Refresh();
            PathStackPanel.Children.Clear();
            CurrentPath = null;
            PathBox.Tag = null;
            NavHistory.PathHistory.Clear();
            PathBox.IsEnabled =
            NewMenuButton.IsEnabled =
            CopyMenuButton.IsEnabled =
            BackButton.IsEnabled =
            ForwardButton.IsEnabled =
            ParentButton.IsEnabled = false;
        }

        private void DevicesSplitView_PaneClosing(SplitView sender, SplitViewPaneClosingEventArgs args)
        {
            NewDevicePanel.Visibility = Visibility.Collapsed;
            Devices.ForEach(d => d.IsSelected = false);
            DevicesList.Items.Refresh();
        }

        private void NewDevicePanel_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (NewDevicePanel.IsVisible)
                RetrieveIp();
        }

        private void NewDeviceIpBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && ConnectNewDeviceButton.IsEnabled)
                ConnectNewDevice();
        }

        private void RememberPortCheckBox_Click(object sender, RoutedEventArgs e)
        {
            Storage.StoreValue(Settings.rememberPort, RememberPortCheckBox.IsChecked);
        }

        private void ExplorerGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (ExplorerGrid.SelectedItems.Count < 1)
            {
                ((MenuItem)ExplorerGrid.ContextMenu.Items[0]).Visibility = Visibility.Collapsed;
            }
            else
                ((MenuItem)ExplorerGrid.ContextMenu.Items[0]).Visibility = Visibility.Visible;
        }

        private void ListViewItem_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is ModernWpf.Controls.ListViewItem item && item.DataContext is DeviceClass device && !device.IsSelected)
            {
                Devices.ForEach(d => d.IsSelected = false);
                device.IsSelected = true;
                DevicesList.Items.Refresh();
            }
        }
    }
}
