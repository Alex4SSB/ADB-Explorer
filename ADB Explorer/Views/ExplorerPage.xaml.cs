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
        private const string INTERNAL_STORAGE = "sdcard";
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
                PathBox.Tag = INTERNAL_STORAGE;
                StartDirectoryList(INTERNAL_STORAGE);
            }
            PopulateButtons(PathBox.Tag.ToString());
        }

        public void OnNavigatedFrom()
        {
            StopDirectoryList();
            ConnectTimer.Stop();
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
                PathBox.Tag =
                CurrentPath = file.Path;
                PopulateButtons(file.Path);

                StartDirectoryList(file.Path);
            }
        }

        private void DirListUpdateTimer_Tick(object sender, EventArgs e)
        {
            UpdateDirectoryList();
        }

        private void PopulateButtons(string path)
        {
            PathStackPanel.Children.Clear();
            var dirs = path.Split('/');

            for (int i = 0; i < dirs.Length; i++)
            {
                var dirPath = string.Join('/', dirs[..(i + 1)]);
                var dirName = dirs[i] == INTERNAL_STORAGE ? "Internal Storage" : dirs[i];
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
                    Margin = new(0, 1, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
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
    }
}
