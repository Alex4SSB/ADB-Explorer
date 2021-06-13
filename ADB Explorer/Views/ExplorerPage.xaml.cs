using ADB_Explorer.Contracts.Views;
using ADB_Explorer.Core.Models;
using ADB_Explorer.Core.Services;
using ADB_Explorer.Models;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Controls;
using System.Windows.Input;

namespace ADB_Explorer.Views
{
    public partial class ExplorerPage : Page, INotifyPropertyChanged, INavigationAware
    {
        private const string INTERNAL_STORAGE = "sdcard";

        public ExplorerPage()
        {
            InitializeComponent();
            DataContext = this;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public List<FileClass> WindowsFileList { get; set; }
        public List<FileClass> AndroidFileList { get; set; }

        public void OnNavigatedTo(object parameter)
        {
            // Windows
            WindowsFileList = DriveInfo.GetDrives().Select(f => FileClass.GenerateWindowsFile(f.Name, FileStat.FileType.Drive)).ToList();

            PathBox.Text = INTERNAL_STORAGE;
            // Android
            AndroidFileList = ADBService.ReadDirectory(INTERNAL_STORAGE).Select(f => FileClass.GenerateAndroidFile(f)).ToList();

            ExplorerGrid.ItemsSource = AndroidFileList;// WindowsFileList;
        }

        public void OnNavigatedFrom()
        {
        }

        private void Set<T>(ref T storage, T value, [CallerMemberName]string propertyName = null)
        {
            if (Equals(storage, value))
            {
                return;
            }

            storage = value;
            OnPropertyChanged(propertyName);
        }

        private void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private void WindowsExplorerView_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            //var itemName = WindowsExplorerGrid.SelectedItem?.ToString();
            //if (itemName == "..")
            //{
            //    itemName = Directory.GetParent(WindowsFileList[1]).FullName;
            //    var parent = Directory.GetParent(itemName);
            //    if (parent is null)
            //    {
            //        WindowsFileList.Clear();
            //        WindowsFileList.AddRange(DriveInfo.GetDrives().Select(f => f.Name).ToList());
            //        WindowsExplorerGrid.Items.Refresh();
            //        return;
            //    }
            //    itemName = parent.FullName;
            //}
            //else if (!Directory.Exists(itemName)) return;
            //WindowsFileList.Clear();
            //WindowsFileList.Add("..");
            //WindowsFileList.AddRange(Directory.GetDirectories(itemName));
            //WindowsFileList.AddRange(Directory.GetFiles(itemName));
            //WindowsExplorerGrid.Items.Refresh();
            //WindowsExplorerGrid.Items.Clear();
            //WindowsFileList.ForEach(i => WindowsExplorerGrid.Items.Add(i));
        }

        private void DataGridRow_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.Source is DataGridRow row && row.Item is FileClass file && file.Type != FileStat.FileType.File)
            {
                // WindowsFileList.Clear();
                // WindowsFileList.Add(new(Directory.GetParent(file.Path)?.FullName, FileStat.FileType.Parent));
                // WindowsFileList.AddRange(Directory.GetDirectories(file.Path).Select(d => new PhysicalFileClass(d, FileStat.FileType.Folder)));
                // WindowsFileList.AddRange(Directory.GetFiles(file.Path).Select(f => new PhysicalFileClass(f, FileStat.FileType.File)));

                PathBox.Text = file.Path;

                AndroidFileList.Clear();
                ExplorerGrid.ItemsSource = null;

                AndroidFileList.AddRange(ADBService.ReadDirectory(file.Path).Select(f => FileClass.GenerateAndroidFile(f)));

                ExplorerGrid.ItemsSource = AndroidFileList;
                ExplorerGrid.Items.Refresh();

                ExplorerGrid.ScrollIntoView(ExplorerGrid.Items[0]);
            }
        }
    }
}
