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
            { "/mnt/sdcard", "Internal Storage" },
            { "/", "Root" }
        };

        public static readonly Dictionary<string, DriveType> DRIVE_TYPES = new()
        {
            { "/storage/emulated", DriveType.Internal },
            { "/sdcard", DriveType.Internal },
            { "/", DriveType.Root }
        };
        public static readonly Dictionary<DriveType, string> DRIVES_PRETTY_NAMES = new()
        {
            { DriveType.Root, "Root" },
            { DriveType.Internal, "Internal Storage" },
            { DriveType.Expansion, "µSD Card" },
            { DriveType.External, "OTG Drive" },
            { DriveType.Unknown, "" }, // "Other Drive"
            { DriveType.Emulated, "Emulated Drive" }
        };

        public static readonly TimeSpan DIR_LIST_SYNC_TIMEOUT = TimeSpan.FromMilliseconds(500);
        public static readonly TimeSpan DIR_LIST_UPDATE_INTERVAL = TimeSpan.FromMilliseconds(1000);
        public static readonly TimeSpan SYNC_PROG_UPDATE_INTERVAL = TimeSpan.FromMilliseconds(100);
        public static readonly TimeSpan CONNECT_TIMER_INTERVAL = TimeSpan.FromMilliseconds(2000);
        public static readonly TimeSpan DRIVE_UPDATE_INTERVAL = TimeSpan.FromMilliseconds(2000);

        public static readonly sbyte MIN_SUPPORTED_ANDROID_VER = 6;
        public static readonly double MAX_PANE_WIDTH_RATIO = 0.9;
        public static readonly int MIN_PANE_WIDTH = 200;
        public static readonly int MIN_PANE_WIDTH_FOR_COLLAPSE = 260;
    }
}
