using ADB_Explorer.Contracts.Views;
using ADB_Explorer.Core.Models;
using ADB_Explorer.Core.Services;
using ADB_Explorer.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
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

        public event PropertyChangedEventHandler PropertyChanged;

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
                AndroidFileList.Clear();
                ConnectTimer.Start();
                return;
            }

            // Windows
            //WindowsFileList = DriveInfo.GetDrives().Select(f => FileClass.GenerateWindowsFile(f.Name, FileStat.FileType.Drive)).ToList();


            // Android
            if (AndroidFileList is null || !AndroidFileList.Any())
            {
                PathBox.Text = INTERNAL_STORAGE;
                AndroidFileList = ADBService.ListDirectory(INTERNAL_STORAGE).Select(f => FileClass.GenerateAndroidFile(f)).ToList();
            }
            else
                PathBox.Text = CurrentPath;

            ExplorerGrid.ItemsSource = AndroidFileList;// WindowsFileList;
        }

        public void OnNavigatedFrom()
        {
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
                CurrentPath =
                PathBox.Text = file.Path;

                AndroidFileList.Clear();
                ExplorerGrid.ItemsSource = null;

                AndroidFileList.AddRange(ADBService.ListDirectory(file.Path).Select(f => FileClass.GenerateAndroidFile(f)));

                ExplorerGrid.ItemsSource = AndroidFileList;
                ExplorerGrid.Items.Refresh();

                ExplorerGrid.ScrollIntoView(ExplorerGrid.Items[0]);
            }
        }
    }
}
