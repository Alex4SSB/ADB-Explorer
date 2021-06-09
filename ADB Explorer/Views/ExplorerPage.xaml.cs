using ADB_Explorer.Contracts.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Controls;

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

        public List<string> WindowsFileList { get; set; }

        public void OnNavigatedTo(object parameter)
        {
            //var files = Directory.GetFiles(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            var files = DriveInfo.GetDrives();
            WindowsFileList = files.Select(f => f.Name).ToList();
            //WindowsFileList = files.Select(f => Path.GetFileName(f));

            //WindowsExplorerView.items
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
            var itemName = WindowsExplorerView.SelectedItem?.ToString();
            if (itemName == "..")
            {
                itemName = Directory.GetParent(WindowsFileList[1]).FullName;
                var parent = Directory.GetParent(itemName);
                if (parent is null)
                {
                    WindowsFileList.Clear();
                    WindowsFileList.AddRange(DriveInfo.GetDrives().Select(f => f.Name).ToList());
                    WindowsExplorerView.Items.Refresh();
                    return;
                }
                itemName = parent.FullName;
            }
            else if (!Directory.Exists(itemName)) return;
            WindowsFileList.Clear();
            WindowsFileList.Add("..");
            WindowsFileList.AddRange(Directory.GetDirectories(itemName));
            WindowsFileList.AddRange(Directory.GetFiles(itemName));
            WindowsExplorerView.Items.Refresh();
            //WindowsExplorerView.Items.Clear();
            //WindowsFileList.ForEach(i => WindowsExplorerView.Items.Add(i));
        }
    }
}
