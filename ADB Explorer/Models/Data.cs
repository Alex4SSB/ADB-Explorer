using ADB_Explorer.Services;
using System.Collections.Generic;
using System.Drawing;
using ADB_Explorer.Helpers;
using System;

namespace ADB_Explorer.Models
{
    public static class Data
    {
        private static string deviceName = "";
        public static string DeviceName
        {
            get
            {
                if (deviceName == "")
                    deviceName = ADBService.GetDeviceName();

                return deviceName;
            }
        }

        public static MyObservableCollection<FileClass> AndroidFileList = new();

        public static string CurrentPath { get; set; }
        public static string ParentPath { get; set; }

        public static Dictionary<Tuple<string, bool>, Icon> FileIcons = new();

        /// <summary>
        /// Tuple of target path and source path
        /// </summary>
        public static Queue<Tuple<string, string>> PullQ = new();
    }
}
