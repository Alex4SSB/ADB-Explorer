using System;
using System.Collections.Generic;

namespace ADB_Explorer.Models
{
    public static class AdbExplorerConst
    {
        public static readonly string DEFAULT_PATH = "/sdcard";
        public static readonly Dictionary<string, string> SPECIAL_FOLDERS_PRETTY_NAMES = new()
        {
            { "/sdcard", "Internal Storage" },
            { "/storage/emulated/0", "Internal Storage" },
            { "/storage/self/primary", "Internal Storage" },
            { "/", "Root" }
        };

        public static readonly TimeSpan DIR_LIST_SYNC_TIMEOUT = TimeSpan.FromMilliseconds(500);
        public static readonly TimeSpan DIR_LIST_UPDATE_INTERVAL = TimeSpan.FromMilliseconds(1000);
        public static readonly TimeSpan SYNC_PROG_UPDATE_INTERVAL = TimeSpan.FromMilliseconds(100);
        public static readonly TimeSpan CONNECT_TIMER_INTERVAL = TimeSpan.FromMilliseconds(2000);
    }
}
