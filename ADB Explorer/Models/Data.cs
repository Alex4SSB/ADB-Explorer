using ADB_Explorer.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using ADB_Explorer.Services;

namespace ADB_Explorer.Models
{
    public static class Data
    {
        public static ADBService.AdbDevice CurrentADBDevice { get; set; } = null;

        public static ObservableList<FileClass> AndroidFileList { get; set; } = new();

        public static string CurrentPath { get; set; }
        public static string ParentPath { get; set; }

        public static Dictionary<Tuple<string, bool>, Icon> FileIcons { get; set; } = new();

        public static FileOperationQueue fileOperationQueue { get; set; }

        public static Dictionary<string, string> CurrentPrettyNames { get; set; } = new();

        public static ObservableCollection<ColumnConfig> ColumnConfigs { get; set; } = new();

        public static Dictionary<string, AbstractDevice.RootStatus> DevicesRoot { get; set; } = new();

        public static bool UseFluentStyles => Environment.OSVersion.Version > AdbExplorerConst.WIN11_VERSION
            || Storage.RetrieveBool(UserPrefs.forceFluentStyles) == true;
    }
}
