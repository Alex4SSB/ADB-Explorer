using ADB_Explorer.Contracts.Views;
using ADB_Explorer.Core.Models;
using ADB_Explorer.Core.Services;
using ADB_Explorer.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
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
                AndroidFileList?.Clear();
                ConnectTimer.Start();
                return;
            }

            // Windows
            // WindowsFileList = DriveInfo.GetDrives().Select(f => FileClass.GenerateWindowsFile(f.Name, FileStat.FileType.Drive)).ToList();

            // Android
            if (AndroidFileList is null || !AndroidFileList.Any())
            {
                PathBox.Tag =
                CurrentPath = INTERNAL_STORAGE;
                AndroidFileList = ADBService.ListDirectory(INTERNAL_STORAGE).Select(f => FileClass.GenerateAndroidFile(f)).ToList();
            }
            else
                PathBox.Tag = CurrentPath;

            PopulateButtons(PathBox.Tag.ToString());
            ExplorerGrid.ItemsSource = AndroidFileList; // WindowsFileList;
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
                PathBox.Tag =
                CurrentPath = file.Path;
                PopulateButtons(file.Path);

                EnterFolder(file.Path);
            }
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

        private void EnterFolder(string path)
        {
            AndroidFileList.Clear();
            ExplorerGrid.ItemsSource = null;

            AndroidFileList.AddRange(ADBService.ListDirectory(path).Select(f => FileClass.GenerateAndroidFile(f)));

            ExplorerGrid.ItemsSource = AndroidFileList;
            ExplorerGrid.Items.Refresh();

            ExplorerGrid.ScrollIntoView(ExplorerGrid.Items[0]);
        }

        private void PathButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            string path = (sender as Button).Tag.ToString();
            PopulateButtons(path);
            EnterFolder(path);
        }
    }
}
