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
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
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
        private DispatcherTimer dirListUpdateTimer;
        private CancellationTokenSource dirListCancelTokenSource;
        private CancellationTokenSource determineFoldersCancelTokenSource;
        private ConcurrentQueue<FileStat> waitingFileStats;

        private ItemsPresenter ExplorerContentPresenter;
        private double ColumnHeaderHeight
        {
            get
            {
                GetExplorerContentPresenter();
                return ExplorerContentPresenter is null ? 0 : ExplorerGrid.ActualHeight - ExplorerContentPresenter.ActualHeight;
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

        private Point MouseDownPoint;

        private void GetExplorerContentPresenter()
        {
            if (ExplorerContentPresenter is null && VisualTreeHelper.GetChild(ExplorerGrid, 0) is Border border && border.Child is ScrollViewer scroller && scroller.Content is ItemsPresenter presenter)
                ExplorerContentPresenter = presenter;
        }

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

            fileOperationQueue.Operations.CollectionChanged += FileOperationProgressUpdateHandler;

            InputLanguageManager.Current.InputLanguageChanged +=
                new InputLanguageEventHandler((sender, e) =>
                {
                    UpdateInputLang();
                });
        }

        private void InitializeContextMenu(MenuType type)
        {
            ExplorerGrid.ContextMenu.Items.Clear();
            switch (type)
            {
                case MenuType.ExplorerItem:
                    ExplorerGrid.ContextMenu.Items.Add(FindResource("ContextMenuCopyItem"));
                    ExplorerGrid.ContextMenu.Items.Add(FindResource("ContextMenuDeleteItem"));
                    break;
                case MenuType.EmptySpace:
                    ExplorerGrid.ContextMenu.Items.Add(FindResource("ContextMenuDeleteItem"));
                    break;
                case MenuType.Header:
                    break;
                default:
                    break;
            }
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
            if (e.ChangedButton == MouseButton.Left && ExplorerGrid.SelectedItems.Count == 1)
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
            MouseDownPoint = e.GetPosition(ExplorerGrid);
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
            var init = !Devices.Update(ADBService.GetDevices());

            DevicesList.ItemsSource = Devices.List;
            DevicesList.Items.Refresh();

            if (!Devices.DevicesAvailable())
            {
                Title = $"{Properties.Resources.AppDisplayName} - NO CONNECTED DEVICES";
                ClearExplorer();
                return;
            }
            else
            {
                if (Devices.DevicesAvailable(true))
                    return;

                if (AutoOpenCheckBox.IsChecked != true)
                {
                    Devices.Current?.SetOpen(false);

                    Title = Properties.Resources.AppDisplayName;
                    ClearExplorer();
                    return;
                }

                if (!Devices.SetCurrentDevice(selectedAddress))
                    return;

                if (!ConnectTimer.IsEnabled)
                    DevicesSplitView.IsPaneOpen = false;
            }

            Devices.Current.SetOpen(true);
            CurrentADBDevice = new(Devices.Current.ID);
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

            if (Storage.RetrieveBool(Settings.autoOpen) is bool autoOpen)
                AutoOpenCheckBox.IsChecked = autoOpen;

            FileOperationsList.ItemsSource = fileOperationQueue.Operations;
        }

        private void InitDevice()
        {
            Title = $"{Properties.Resources.AppDisplayName} - {Devices.Current.Name}";

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
            PathBox.IsEnabled = true;
            NavHistory.Reset();

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
            // do nothing if amount of devices and their types haven't changed
            if (Devices.DevicesChanged(ADBService.GetDevices()))
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

        private static void DetermineFolders(IEnumerable<FileClass> files, CancellationToken cancellationToken)
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
            var expectedLength = 0;
            PathStackPanel.Children.Clear();
            var pathItems = new List<string>();

            // On special cases, cut prefix of the path and replace with a pretty button
            var specialPair = SPECIAL_FOLDERS_PRETTY_NAMES.FirstOrDefault(kv => path.StartsWith(kv.Key));
            if (specialPair.Key != null)
            {
                AddPathButton(specialPair);
                pathItems.Add(specialPair.Key);
                path = path[specialPair.Key.Length..].TrimStart('/');
                expectedLength = PathButtonLength.ButtonLength(specialPair.Value);
            }

            var dirs = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            foreach (var dir in dirs)
            {
                pathItems.Add(dir);
                var dirPath = string.Join('/', pathItems).Replace("//", "/");
                var dirName = pathItems.Last();
                AddPathButton(dirPath, dirName);

                if (PathStackPanel.Children.Count > 2)
                    expectedLength += PATH_ARROW_WIDTH;

                expectedLength += PathButtonLength.ButtonLength(dirName);
            }

            ConsolidateButtons(expectedLength);
        }

        private void ConsolidateButtons(double expectedLength)
        {
            List<MenuItem> excessButtons = new();
            while (expectedLength >= PathBox.ActualWidth && PathStackPanel.Children.Count > 2)
            {
                var item = ((Menu)PathStackPanel.Children[0]).Items[0] as MenuItem;
                if (item.Header is FontIcon)
                {
                    throw new NotImplementedException();
                }

                if (!excessButtons.Any())
                    expectedLength += PATH_EXCESS_BUTTON_WIDTH;

                excessButtons.Add(item);
                expectedLength -= PathButtonLength.ButtonLength((string)item.Header);
                expectedLength -= PATH_ARROW_WIDTH;
                PathStackPanel.Children.RemoveRange(0, 2);
            }

            CreateExcessButton(excessButtons);
        }

        private MenuItem CreateExcessButton(List<MenuItem> excessButtons = null)
        {
            if (excessButtons is not null && !excessButtons.Any())
                return null;

            AddPathArrow(false);
            Menu excessMenu = new() { Height = 24 };
            MenuItem excessButton = new()
            {
                ItemsSource = excessButtons,
                Height = 24,
                Header = new FontIcon() { Glyph = "\uE712", FontSize = 12, FontWeight = FontWeights.Bold }
            };
            excessMenu.Items.Add(excessButton);
            PathStackPanel.Children.Insert(0, excessMenu);

            return excessButton;
        }

        //private void ConsolidateButtons()
        //{
        //    if (PathStackPanel.ActualWidth / PathBox.ActualWidth > 0.98)
        //    {
        //        List<MenuItem> excessButtons = new();
        //        var item = ((Menu)PathStackPanel.Children[0]).Items[0] as MenuItem;
        //        if (item.Header is FontIcon)
        //            excessButtons = (List<MenuItem>)item.ItemsSource;

        //        var menu = PathStackPanel.Children[2] as Menu;
        //        var excess = menu.Items[0] as MenuItem;
        //        menu.Items.Clear();
        //        PathStackPanel.Children.RemoveRange(2, 2);
        //        excessButtons.Add(excess);

        //        if (item.Header is not FontIcon)
        //        {
        //            item = CreateExcessButton(excessButtons);
        //        }
        //        else
        //            item.ItemsSource = excessButtons;

        //        item.Items.Refresh();
        //    }
        //}

        private void AddPathButton(KeyValuePair<string, string> kv, bool addArrow = true) => AddPathButton(kv.Key, kv.Value, addArrow);
        private void AddPathButton(string path, string name, bool addArrow = true)
        {
            if (PathStackPanel.Children.Count > 0 && addArrow)
            {
                AddPathArrow();
            }

            MenuItem button = new() { Header = name, Tag = path, Height = 24 };
            Menu menu = new() { Height = 24 };
            menu.Items.Add(button);
            button.Click += PathButton_Click;
            PathStackPanel.Children.Add(menu);
        }

        private void AddPathArrow(bool append = true)
        {
            FontIcon arrow = new()
            {
                Glyph = " \uE970 ",
                FontSize = 7,
            };

            if (append)
                PathStackPanel.Children.Add(arrow);
            else
                PathStackPanel.Children.Insert(0, arrow);
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

            foreach (FileClass item in ExplorerGrid.SelectedItems)
            {
                fileOperationQueue.AddOperation(new FilePullOperation(Dispatcher, CurrentADBDevice, item.Path, path));
            }
        }

        private void FileOperationProgressUpdateHandler(object sender, EventArgs e)
        {
            if (fileOperationQueue.IsActive)
            {
                OperationCompletedTextBlock.Text = "";

                if (ProgressGrid.Visibility == Visibility.Collapsed)
                    ProgressGrid.Visibility = Visibility.Visible;

                ProgressCountTextBlock.Text = $"{fileOperationQueue.CurrentOperationIndex + 1}/{fileOperationQueue.Operations.Count}";

                if (fileOperationQueue.CurrentOperation.StatusInfo is ADBService.Device.AdbSyncProgressInfo progressInfo && progressInfo.TotalPercentage.HasValue)
                {
                    OverallProgressBar.IsIndeterminate = false;
                    OverallProgressBar.Value = progressInfo.TotalPercentage.Value;
                }
                else
                {
                    OverallProgressBar.IsIndeterminate = true;
                }
            }
            else
            {
                if ((int)ProgressCountTextBlock.Tag > 0)
                {
                    OperationCompletedTextBlock.Tag = ProgressCountTextBlock.Tag;
                }

                ProgressCountTextBlock.Tag = 0;

                ProgressGrid.Visibility = Visibility.Collapsed;
                OperationCompletedTextBlock.Text = $"{DateTime.Now:HH:mm:ss} - {fileOperationQueue.Operations.Count} file{(fileOperationQueue.Operations.Count > 1 ? "s" : "")} done";
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
            }

            if (e.ChangedButton == MouseButton.Right)
            {
                InitializeContextMenu(e.OriginalSource is Border ? MenuType.EmptySpace : MenuType.ExplorerItem);
            }

            if (e.OriginalSource is Border)
                return;

            row.IsSelected = true;

            MouseDownPoint = e.GetPosition(ExplorerGrid);
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
            NewDevicePanelVisibility(false);
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
            NewDevicePanelVisibility(!NewDevicePanelVisibility());
            Devices.UnselectAll();
            DevicesList.Items.Refresh();
        }

        private void NewDevicePanelVisibility(bool open)
        {
            if (NewDevicePanel is null)
                return;

            if (open)
            {
                if (NewDevicePanel.Visibility == Visibility.Collapsed)
                    NewDevicePanel.Visibility = Visibility.Visible;

                NewDevicePanel.Tag = "Open";
            }
            else
                NewDevicePanel.Tag = "Closed";
        }

        private bool NewDevicePanelVisibility()
        {
            return NewDevicePanel?.Tag?.ToString() == "Open";
        }

        private void RetrieveIp()
        {
            NewDeviceIpBox.Clear();
            NewDevicePortBox.Clear();

            if (RememberIpCheckBox.IsChecked == true
                && Storage.RetrieveValue(Settings.lastIp) is string lastIp
                && !Devices.List.Find(d => d.ID.Split(':')[0] == lastIp))
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
                device.SetOpen();
                CurrentADBDevice = new(device.ID);

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
                    device.SetOpen(false);
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
            NavHistory.Reset();
            PathBox.IsEnabled =
            NewMenuButton.IsEnabled =
            CopyMenuButton.IsEnabled =
            BackButton.IsEnabled =
            ForwardButton.IsEnabled =
            ParentButton.IsEnabled = false;
        }

        private void DevicesSplitView_PaneClosing(SplitView sender, SplitViewPaneClosingEventArgs args)
        {
            NewDevicePanelVisibility(false);
            Devices.UnselectAll();
            DevicesList.Items.Refresh();
        }

        private void NewDevicePanel_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (NewDevicePanelVisibility())
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
            ExplorerGrid.ContextMenu.Visibility = Visible(ExplorerGrid.ContextMenu.HasItems);
        }

        private void ListViewItem_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is ModernWpf.Controls.ListViewItem item && item.DataContext is DeviceClass device && !device.IsSelected)
            {
                device.SetSelected();
                DevicesList.Items.Refresh();
            }
        }

        private void AutoOpenCheckBox_Click(object sender, RoutedEventArgs e)
        {
            Storage.StoreValue(Settings.autoOpen, AutoOpenCheckBox.IsChecked);
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
                    InitializeContextMenu(MenuType.Header);
            }

            MouseDownPoint = point;
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
                InitializeContextMenu(MenuType.Header);
            }
            else if (e.OriginalSource is FrameworkElement element && element.DataContext is FileClass && ExplorerGrid.SelectedItems.Count > 0)
            {
                InitializeContextMenu(MenuType.ExplorerItem);
            }
            else
                InitializeContextMenu(MenuType.EmptySpace);
        }

        private void DevicesList_MouseDown(object sender, MouseButtonEventArgs e)
        {
            Devices.UnselectAll();
            DevicesList.Items.Refresh();
        }

        private void DevicesSplitView_PaneOpening(SplitView sender, object args)
        {
            NewDevicePanelVisibility(false);
        }

        private void CloseOperationButton_Click(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is Button button && button.DataContext is FileOperation operation)
            {
                fileOperationQueue.Operations.Remove(operation);
            }
        }

        private void DataGridRow_MouseEnter(object sender, MouseEventArgs e)
        {
            if (MouseDownPoint.Y > 0 && e.LeftButton == MouseButtonState.Pressed && e.OriginalSource is DataGridRow row)
            {
                ExplorerGrid.UnselectAll();

                var currentY = e.GetPosition(ExplorerGrid).Y;
                var above = currentY < MouseDownPoint.Y;
                var verticalDistance = Math.Abs(MouseDownPoint.Y - currentY);

                double currentRelativeDistance = 0;
                var i = ExplorerGrid.ItemContainerGenerator.IndexFromContainer(row);
                do
                {
                    if (i < 0)
                        return;

                    if (ExplorerGrid.ItemContainerGenerator.ContainerFromIndex(i) is not DataGridRow tempRow)
                    {
                        ExplorerGrid.UnselectAll();
                        return;
                    }

                    currentRelativeDistance = Math.Abs(e.GetPosition(tempRow).Y);
                    if (above)
                    {
                        if (currentRelativeDistance > verticalDistance && verticalDistance > tempRow.ActualHeight)
                            break;

                        currentRelativeDistance = tempRow.ActualHeight - currentRelativeDistance;
                    }

                    tempRow.IsSelected = true;
                    i += above ? 1 : -1;
                } while (currentRelativeDistance < verticalDistance && i < ExplorerGrid.Items.Count);
            }
        }

        private void ExplorerGrid_MouseMove(object sender, MouseEventArgs e)
        {
            var point = e.GetPosition(ExplorerCanvas);
            if (e.LeftButton == MouseButtonState.Released
                || (e.LeftButton == MouseButtonState.Pressed
                && (MouseDownPoint.Y < ColumnHeaderHeight || point.Y < ColumnHeaderHeight)))
            {
                SelectionRect.Visibility = Visibility.Collapsed;
                if (e.LeftButton == MouseButtonState.Pressed && point.Y < ColumnHeaderHeight)
                    ExplorerGrid.UnselectAll();

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
        }

        private void SelectionRect_MouseUp(object sender, MouseButtonEventArgs e)
        {
            SelectionRect.Visibility = Visibility.Collapsed;
            MouseDownPoint = new(0, 0);
        }

        private void ExplorerCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            MouseDownPoint = e.GetPosition(ExplorerGrid);
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (PathStackPanel.ActualWidth / PathBox.ActualWidth > 0.98)
                ConsolidateButtons(PathStackPanel.ActualWidth);
        }
    }
}
