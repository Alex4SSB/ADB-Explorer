using ADB_Explorer.Core.Services;
using System.Collections.Generic;
using System.Drawing;

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

        public static List<FileClass> AndroidFileList = new();

        public static string CurrentPath { get; set; }

        public static Dictionary<string, Icon> FileIcons = new();

    }
}
