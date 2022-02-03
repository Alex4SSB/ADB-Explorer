using ADB_Explorer.Services;
using System.Collections.Generic;
using System.Drawing;
using ADB_Explorer.Helpers;
using System;
using System.Collections.ObjectModel;

namespace ADB_Explorer.Models
{
    public static class Data
    {
        public static ADBService.Device CurrentADBDevice = null;

        public static ObservableList<FileClass> AndroidFileList = new();

        public static string CurrentPath { get; set; }
        public static string ParentPath { get; set; }

        public static Dictionary<Tuple<string, bool>, Icon> FileIcons = new();

        public static FileOperationQueue fileOperationQueue;

        public static Dictionary<string, string> CurrentPrettyNames = new();

        public static ObservableCollection<ColumnConfig> ColumnConfigs = new();
    }
}
