using ADB_Explorer.Contracts.Views;
using ADB_Explorer.Core.Models;
using ADB_Explorer.Core.Services;
using ADB_Explorer.Models;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Controls;
using System.Windows.Input;
using static ADB_Explorer.Models.Data;

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

        public void OnNavigatedTo(object parameter)
        {
            // Get device name
            TitleBlock.Text = DeviceName;

            // Windows
            //WindowsFileList = DriveInfo.GetDrives().Select(f => FileClass.GenerateWindowsFile(f.Name, FileStat.FileType.Drive)).ToList();

            PathBox.Text = INTERNAL_STORAGE;
            // Android
            if (AndroidFileList is null)
                AndroidFileList = ADBService.ReadDirectory(INTERNAL_STORAGE).Select(f => FileClass.GenerateAndroidFile(f)).ToList();

            ExplorerGrid.ItemsSource = AndroidFileList;// WindowsFileList;
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
