using ADB_Explorer.Contracts.Views;
using ADB_Explorer.Core.Helpers;
using ADB_Explorer.Core.Models;
using ADB_Explorer.Core.Services;
using ADB_Explorer.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using static ADB_Explorer.Models.Data;

namespace ADB_Explorer.Views
{
    public partial class ExplorerPage : Page, INotifyPropertyChanged, INavigationAware
    {
        private const string INTERNAL_STORAGE = "/sdcard";
        private static readonly Dictionary<string, string> SPECIAL_FOLDERS_PRETTY_NAMES = new Dictionary<string, string>
        {
            {"/sdcard", "Internal Storage"},
            {"/storage/emulated/0", "Internal Storage"},
            {"/storage/self/primary", "Internal Storage"},
            {"/", "Root"}
        };
        private static readonly Key[] KEYS_TO_NAVIGATE = { Key.Enter, Key.Return };

        private readonly DispatcherTimer ConnectTimer = new();
        private static readonly TimeSpan DIR_LIST_SYNC_TIMEOUT = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan DIR_LIST_UPDATE_INTERVAL = TimeSpan.FromMilliseconds(1000);

        public event PropertyChangedEventHandler PropertyChanged;

        private Task listDirTask;
        private DispatcherTimer dirListUpdateTimer;
        private CancellationTokenSource cancellationTokenSource;
        private ConcurrentQueue<FileStat> waitingFileStats;

        private void StartDirectoryList(string path)
        {
            StopDirectoryList();

            AndroidFileList.Clear();

            cancellationTokenSource = new CancellationTokenSource();
            waitingFileStats = new ConcurrentQueue<FileStat>();

            listDirTask = Task.Run(() => ADBService.ListDirectory(path, ref waitingFileStats, cancellationTokenSource.Token));

            if (listDirTask.Wait(DIR_LIST_SYNC_TIMEOUT))
            {
                StopDirectoryList();
            }
            else
            {
                UpdateDirectoryList();
                dirListUpdateTimer.Start();
                listDirTask.ContinueWith((t) => Application.Current.Dispatcher.BeginInvoke(() => StopDirectoryList()));
            }
        }

        private void UpdateDirectoryList()
        {
            if (listDirTask != null)
            {
                bool wasEmpty = (AndroidFileList.Count == 0);

                AndroidFileList.AddRange(waitingFileStats.DequeueAllExisting().Select(f => FileClass.GenerateAndroidFile(f)));

                if (wasEmpty && (AndroidFileList.Count > 0))
                {
                    ExplorerGrid.ScrollIntoView(ExplorerGrid.Items[0]);
                }
            }
        }

        private void StopDirectoryList()
        {
            if (listDirTask != null)
            {
                dirListUpdateTimer.Stop();
                cancellationTokenSource.Cancel();
                listDirTask.Wait();
                UpdateDirectoryList();
                listDirTask = null;
            }
        }

        public ExplorerPage()
        {
            InitializeComponent();
            DataContext = this;

            ConnectTimer.Interval = TimeSpan.FromSeconds(2);
            ConnectTimer.Tick += ConnectTimer_Tick;
        }

        private void ConnectTimer_Tick(object sender, EventArgs e)
        {
            if (ADBService.GetDeviceName() != "")
            {
                ConnectTimer.Stop();
                LaunchSequence();
            }
        }

        public List<FileClass> WindowsFileList { get; set; }

        public void OnNavigatedTo(object parameter)
        {
            LaunchSequence();
        }

        private void LaunchSequence()
        {
            // Get device name
            if (ADBService.GetDeviceName() is string name && !string.IsNullOrEmpty(name))
            {
                TitleBlock.Text = name;
            }
            else
            {
                TitleBlock.Text = "NO CONNECTED DEVICES";
                AndroidFileList?.Clear();
                ConnectTimer.Start();
                return;
            }

            cancellationTokenSource = new CancellationTokenSource();
            waitingFileStats = new ConcurrentQueue<FileStat>();
            dirListUpdateTimer = new DispatcherTimer();
            dirListUpdateTimer.Interval = DIR_LIST_UPDATE_INTERVAL;
            dirListUpdateTimer.Tick += DirListUpdateTimer_Tick;

            ExplorerGrid.ItemsSource = AndroidFileList;

            if (AndroidFileList.Any())
            {
                PathBox.Tag = CurrentPath;
            }
            else
            {
                NavigateToPath(INTERNAL_STORAGE);
            }
        }

        public void OnNavigatedFrom()
        {
            StopDirectoryList();
            ConnectTimer.Stop();
        }
        public bool NavigateToPath(string path)
        {
            string realPath = path;
            try
            {
                realPath = ADBService.TranslateDevicePath(path);
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message, "Navigation Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            PathBox.Tag =
            CurrentPath = realPath;
            PopulateButtons(realPath);
            StartDirectoryList(realPath);
            return true;
        }

        private void Set<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(storage, value))
            {
                return;
            }

            storage = value;
            OnPropertyChanged(propertyName);
        }

        private void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private void DataGridRow_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.Source is DataGridRow row && row.Item is FileClass file && file.Type != FileStat.FileType.File)
            {
                NavigateToPath(file.Path);
            }
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
            var specialPair = SPECIAL_FOLDERS_PRETTY_NAMES.Where((kv) => path.StartsWith(kv.Key)).FirstOrDefault();
            if (specialPair.Key != null)
            {
                AddPathButton(specialPair.Key, specialPair.Value);
                pathItems.Add(specialPair.Key);
                path = path.Substring(specialPair.Key.Length).TrimStart('/');
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

            Button button = new() { Content = name, Tag = path };
            button.Click += PathButton_Click;
            PathStackPanel.Children.Add(button);
        }

        private void PathButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            string path = (sender as Button).Tag.ToString();
            PopulateButtons(path);
            StartDirectoryList(path);
        }

        private void PathBox_GotFocus(object sender, RoutedEventArgs e)
        {
            PathStackPanel.Visibility = Visibility.Collapsed;
            PathBox.Text = PathBox.Tag.ToString();
            PathBox.IsReadOnly = false;
        }

        private void PathBox_LostFocus(object sender, RoutedEventArgs e)
        {
            PathStackPanel.Visibility = Visibility.Visible;
            PathBox.Text = "";
            PathBox.IsReadOnly = true;
        }

        private void Page_MouseDown(object sender, MouseButtonEventArgs e)
        {
            ExplorerGrid.Focus();
        }

        private void PathBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (KEYS_TO_NAVIGATE.Contains(e.Key) && NavigateToPath(PathBox.Text))
            {
                ExplorerGrid.Focus();
            }
        }
    }
}
