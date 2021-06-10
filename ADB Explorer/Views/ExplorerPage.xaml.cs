using ADB_Explorer.Contracts.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Controls;
using ADB_Explorer.Models;
using System.Windows.Input;

namespace ADB_Explorer.Views
{
    public partial class ExplorerPage : Page, INotifyPropertyChanged, INavigationAware
    {
        public ExplorerPage()
        {
            InitializeComponent();
            DataContext = this;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public List<FileClass> WindowsFileList { get; set; }

        public void OnNavigatedTo(object parameter)
        {
            var files = DriveInfo.GetDrives();
            WindowsFileList = files.Select(f => new FileClass(f.Name, FileType.Drive)).ToList();
            WindowsExplorerGrid.ItemsSource = WindowsFileList;
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
            if (e.Source is DataGridRow row && row.Item is FileClass file)
            {
                WindowsFileList.Clear();
                WindowsFileList.Add(new(Directory.GetParent(file.FilePath)?.FullName, FileType.Parent));
                WindowsFileList.AddRange(Directory.GetDirectories(file.FilePath).Select(d => new FileClass(d, FileType.Folder)));
                WindowsFileList.AddRange(Directory.GetFiles(file.FilePath).Select(f => new FileClass(f, FileType.File)));
                WindowsExplorerGrid.Items.Refresh();
            }
        }
    }
}
