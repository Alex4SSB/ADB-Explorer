using ADB_Explorer.Core.Helpers;
using ADB_Explorer.Contracts.Views;
using ADB_Explorer.Core.Models;
using ADB_Explorer.Core.Services;
using ADB_Explorer.Models;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Input;
using static ADB_Explorer.Models.Data;
using System.Windows.Threading;
using System.Threading;
using System;
using System.Windows;

namespace ADB_Explorer.Views
{
    public partial class ExplorerPage : Page, INotifyPropertyChanged, INavigationAware
    {
        private const string INTERNAL_STORAGE = "sdcard";
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

            cancellationTokenSource = new CancellationTokenSource();
            waitingFileStats = new ConcurrentQueue<FileStat>();
            dirListUpdateTimer = new DispatcherTimer();
            dirListUpdateTimer.Interval = DIR_LIST_UPDATE_INTERVAL;
            dirListUpdateTimer.Tick += DirListUpdateTimer_Tick;

            // Get device name
            if (DeviceName is string name && string.IsNullOrEmpty(name))
            {
                TitleBlock.Text = "NO CONNECTED DEVICES";
                return;
            }
            else
            {
                TitleBlock.Text = DeviceName;
            }

            ExplorerGrid.ItemsSource = AndroidFileList;

            if (AndroidFileList.Any())
            {
                PathBox.Text = CurrentPath;
            }
            else
            {
                PathBox.Text = INTERNAL_STORAGE;
                StartDirectoryList(INTERNAL_STORAGE);
            }
        }

        ~ExplorerPage()
        {
            StopDirectoryList();
        }

        public void OnNavigatedTo(object parameter)
        {
            // Windows
            //WindowsFileList = DriveInfo.GetDrives().Select(f => FileClass.GenerateWindowsFile(f.Name, FileStat.FileType.Drive)).ToList();
        }

        public void OnNavigatedFrom()
        {
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
                CurrentPath =
                PathBox.Text = file.Path;

                StartDirectoryList(file.Path);
            }
        }

        private void DirListUpdateTimer_Tick(object sender, EventArgs e)
        {
            UpdateDirectoryList();
        }
    }
}
