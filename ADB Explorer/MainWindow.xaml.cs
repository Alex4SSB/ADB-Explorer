using ADB_Explorer.Converters;
using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;
using ModernWpf;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using static ADB_Explorer.Models.Data;

namespace ADB_Explorer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly SolidColorBrush DarkBG = new(Color.FromRgb(32, 32, 32));
        private readonly SolidColorBrush LightBG = new(Color.FromRgb(243, 243, 243));

        private static readonly string DEFAULT_PATH = "/sdcard";
        private static readonly Dictionary<string, string> SPECIAL_FOLDERS_PRETTY_NAMES = new()
        {
            { "/sdcard", "Internal Storage" },
            { "/storage/emulated/0", "Internal Storage" },
            { "/storage/self/primary", "Internal Storage" },
            { "/", "Root" }
        };
        private static readonly Key[] KEYS_TO_NAVIGATE = { Key.Enter, Key.Return };

        private readonly DispatcherTimer ConnectTimer = new();
        private static readonly TimeSpan DIR_LIST_SYNC_TIMEOUT = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan DIR_LIST_UPDATE_INTERVAL = TimeSpan.FromMilliseconds(1000);

        private Task listDirTask;
        private Task unknownFoldersTask;
        private DispatcherTimer dirListUpdateTimer;
        private CancellationTokenSource dirListCancelTokenSource;
        private CancellationTokenSource determineFoldersCancelTokenSource;
        private ConcurrentQueue<FileStat> waitingFileStats;

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

            SetTheme(RetrieveValue<ApplicationTheme>("theme"));

            LaunchSequence();

            ConnectTimer.Interval = TimeSpan.FromSeconds(2);
            ConnectTimer.Tick += ConnectTimer_Tick;
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

        private void ToggleThemeButton_Click(object sender, RoutedEventArgs e)
        {
            SetTheme(ThemeManager.Current.ApplicationTheme == ApplicationTheme.Light
                ? ApplicationTheme.Dark
                : ApplicationTheme.Light
                );
        }

        private void SetTheme(object theme) => SetTheme((ApplicationTheme)theme);

        private void SetTheme(ApplicationTheme theme)
        {
            ThemeManager.Current.ApplicationTheme = theme;

            Background = theme == ApplicationTheme.Light ? LightBG : DarkBG;
            Application.Current.Properties["theme"] = ThemeManager.Current.ApplicationTheme;
        }

        private static T RetrieveValue<T>(string key)
        {
            return Application.Current.Properties[key] is string value ? (T)Enum.Parse(typeof(T), value) : default;
        }

        private void PathBox_GotFocus(object sender, RoutedEventArgs e)
        {
            PathStackPanel.Visibility = Visibility.Collapsed;
            PathBox.Text = PathBox.Tag?.ToString();
            PathBox.IsReadOnly = false;
        }

        private void PathBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (KEYS_TO_NAVIGATE.Contains(e.Key) && NavigateToPath(PathBox.Text))
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
            if ((e.Source is DataGridRow row) &&
                (row.Item is FileClass file) &&
                (file.Type == FileStat.FileType.Folder))
            {
                NavigateToPath(file.Path);
            }
        }

        private void ExplorerGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            TotalSizeBlock.Text = SelectedFilesTotalSize;
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            ExplorerGrid.Focus();
        }

        private void LaunchSequence()
        {
            Title = Properties.Resources.AppDisplayName;
            // Get device name
            if (ADBService.GetDeviceName() is string name && !string.IsNullOrEmpty(name))
            {
                Title = $"{Title} - {name}";
            }
            else
            {
                Title = $"{Title} - NO CONNECTED DEVICES";
                AndroidFileList?.Clear();
                ConnectTimer.Start();
                return;
            }

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
        }

        private void ConnectTimer_Tick(object sender, EventArgs e)
        {
            if (ADBService.GetDeviceName() == "") return;

            ConnectTimer.Stop();
            LaunchSequence();
        }

        private void StartDirectoryList(string path)
        {
            StopDirectoryList();
            StopDetermineFolders();

            AndroidFileList.RemoveAll();

            determineFoldersCancelTokenSource = new CancellationTokenSource();
            dirListCancelTokenSource = new CancellationTokenSource();
            waitingFileStats = new ConcurrentQueue<FileStat>();

            listDirTask = Task.Run(() => ADBService.ListDirectory(path, ref waitingFileStats, dirListCancelTokenSource.Token));

            if (listDirTask.Wait(DIR_LIST_SYNC_TIMEOUT))
            {
                StopDirectoryList();
            }
            else
            {
                Cursor = Cursors.AppStarting;
                UnfinishedBlock.Visibility = Visibility.Visible;

                UpdateDirectoryList();
                dirListUpdateTimer.Start();
                listDirTask.ContinueWith((t) => Application.Current.Dispatcher.BeginInvoke(() => StopDirectoryList()));
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
            unknownFoldersTask.ContinueWith((t) => DetermineFolders(files, determineFoldersCancelTokenSource.Token));
        }

        private void DetermineFolders(IEnumerable<FileClass> files, CancellationToken cancellationToken)
        {
            foreach (var file in files)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                else if (ADBService.IsDirectory(file.Path))
                {
                    Application.Current.Dispatcher.BeginInvoke(() => { file.Type = FileStat.FileType.Folder; });
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
            string realPath;
            try
            {
                realPath = ADBService.TranslateDevicePath(path);
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
            ParentPath = ADBService.TranslateDeviceParentPath(CurrentPath);

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

    }
}
