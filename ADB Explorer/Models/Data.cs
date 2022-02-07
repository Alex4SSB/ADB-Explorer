using ADB_Explorer.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using static ADB_Explorer.Services.ADBService;

namespace ADB_Explorer.Models
{
    public static class Data
    {
        public static AdbDevice CurrentADBDevice = null;

        public static ObservableList<FileClass> AndroidFileList = new();

        public static string CurrentPath { get; set; }
        public static string ParentPath { get; set; }

        public static Dictionary<Tuple<string, bool>, Icon> FileIcons = new();

        public static FileOperationQueue fileOperationQueue;

        public static Dictionary<string, string> CurrentPrettyNames = new();

        public static ObservableCollection<ColumnConfig> ColumnConfigs = new();
    }
}
