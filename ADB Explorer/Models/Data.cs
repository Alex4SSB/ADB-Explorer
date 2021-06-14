using ADB_Explorer.Core.Services;
using System.Collections.Generic;

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

        public static List<FileClass> AndroidFileList { get; set; }
    }
}
