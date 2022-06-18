using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Media;

namespace ADB_Explorer.Models
{
    public static class AdbExplorerConst
    {
        
        public static readonly Uri REPO_RELEASES_URL = new("https://api.github.com/repos/Alex4SSB/ADB-Explorer/releases");

        public static readonly string DEFAULT_PATH = "/sdcard";
        public static readonly string RECYCLE_PATH = "/sdcard/.Trash-AdbExplorer";
        public static readonly string RECYCLE_INDEX_FILE = ".RecycleIndex";
        public static readonly string RECYCLE_INDEX_BACKUP_FILE = ".RecycleIndex.bak";
        public static readonly string RECYCLE_INDEX_PATH = $"{RECYCLE_PATH}/{RECYCLE_INDEX_FILE}";
        public static readonly string RECYCLE_INDEX_BACKUP_PATH = $"{RECYCLE_PATH}/{RECYCLE_INDEX_BACKUP_FILE}";

        public static readonly string[] RECYCLE_INDEXES = { RECYCLE_INDEX_FILE, RECYCLE_INDEX_BACKUP_FILE };
        public static readonly string[] RECYCLE_INDEX_PATHS = { RECYCLE_INDEX_PATH, RECYCLE_INDEX_BACKUP_PATH };
        public static readonly string[] RECYCLE_PATHS = { RECYCLE_INDEX_PATH, RECYCLE_INDEX_BACKUP_PATH, RECYCLE_PATH };

        public static readonly Dictionary<string, string> SPECIAL_FOLDERS_PRETTY_NAMES = new()
        {
            { RECYCLE_PATH, "Recycle Bin" },
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
            { "/", DriveType.Root },
            { RECYCLE_PATH, DriveType.Trash }
        };
        public static readonly Dictionary<DriveType, string> DRIVES_PRETTY_NAMES = new()
        {
            { DriveType.Root, "Root" },
            { DriveType.Internal, "Internal Storage" },
            { DriveType.Expansion, "µSD Card" },
            { DriveType.External, "OTG Drive" },
            { DriveType.Unknown, "" }, // "Other Drive"
            { DriveType.Emulated, "Emulated Drive" },
            { DriveType.Trash, "Recycle Bin" }
        };

        public static readonly TimeSpan DIR_LIST_VISIBLE_PROGRESS_DELAY = TimeSpan.FromMilliseconds(500);
        public static readonly int DIR_LIST_START_COUNT = 100;
        public static readonly TimeSpan DIR_LIST_UPDATE_INTERVAL = TimeSpan.FromMilliseconds(500);
        public static readonly TimeSpan DIR_LIST_UPDATE_START_INTERVAL = TimeSpan.FromMilliseconds(100);
        public static readonly TimeSpan SPLASH_DISPLAY_TIME = TimeSpan.FromMilliseconds(2000);
        public static readonly int DIR_LIST_UPDATE_THRESHOLD_MIN = 100;
        public static readonly int DIR_LIST_UPDATE_START_THRESHOLD_MIN = 10;
        public static readonly int DIR_LIST_UPDATE_THRESHOLD_MAX = 500;

        public static readonly TimeSpan SYNC_PROG_UPDATE_INTERVAL = TimeSpan.FromMilliseconds(100);
        public static readonly TimeSpan CONNECT_TIMER_INTERVAL = TimeSpan.FromMilliseconds(2000);
        public static readonly TimeSpan CONNECT_TIMER_INIT = TimeSpan.FromMilliseconds(50);
        public static readonly TimeSpan DRIVE_UPDATE_INTERVAL = TimeSpan.FromMilliseconds(2000);
        public static readonly TimeSpan BATTERY_UPDATE_INTERVAL = TimeSpan.FromMilliseconds(8000);
        public static readonly TimeSpan RESPONSE_TIMER_INTERVAL = TimeSpan.FromMilliseconds(1000);
        public static readonly TimeSpan SERVER_RESPONSE_TIMEOUT = TimeSpan.FromMilliseconds(13000);

        public static readonly sbyte MIN_SUPPORTED_ANDROID_VER = 6;
        public static readonly double MAX_PANE_HEIGHT_RATIO = 0.4;
        public static readonly int MIN_PANE_HEIGHT = 150;
        public static readonly double MIN_PANE_HEIGHT_RATIO = 0.15;

        public static readonly string[] APK_NAMES = { ".APK", ".XAPK", ".APKS", ".APKM" };

        public static readonly UnicodeCategory[] UNICODE_ICONS = { UnicodeCategory.Surrogate, UnicodeCategory.PrivateUse, UnicodeCategory.OtherSymbol, UnicodeCategory.OtherNotAssigned };

        public static readonly SolidColorBrush QR_BACKGROUND = new(Colors.Transparent);
        public static readonly SolidColorBrush QR_FOREGROUND = new(Color.FromRgb(40, 40, 40));

        public static readonly char[] ALPHABET = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!@#$%^&*()-+*/<>{}".ToCharArray();
        public static readonly string PAIRING_SERVICE_PREFIX = "adbexplorer-";
        public static readonly string LOOPBACK_IP = "0.0.0.0";

        public static readonly char[] ESCAPE_ADB_SHELL_CHARS = { '(', ')', '<', '>', '|', ';', '&', '*', '\\', '~', '"', '\'', ' ', '$', '`' };
        public static readonly char[] ESCAPE_ADB_CHARS = { '$', '`', '"', '\\' };
        public static readonly char[] INVALID_ANDROID_CHARS = { '"', '*', '/', ':', '<', '>', '?', '\\', '|' };

        public static readonly Version MIN_ADB_VERSION = new(31, 0, 2);
        public static readonly Version WIN11_VERSION = new(10, 0, 22000);

        public static readonly bool DISPLAY_OFFLINE_SERVICES = false;

        public static readonly TimeSpan DOUBLE_CLICK_TIMEOUT = TimeSpan.FromMilliseconds(300);

        public static readonly string ADB_EXPLORER_DATE_FORMAT = "yyyy.MM.dd-HH:mm:ss";
    }
}
