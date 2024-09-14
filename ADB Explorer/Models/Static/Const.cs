namespace ADB_Explorer.Models;

public static class AdbExplorerConst
{
    public const string ADB_PROCESS = "adb";
    public const string PROGRESS_REDIRECTION_PATH = "AdbProgressRedirection.exe";
    public const string APP_DATA_FOLDER = "AdbExplorer";
    public const string ADB_DRAG_FORMAT = "ADB Explorer Drag List";

    public const string DEFAULT_PATH = "/sdcard";
    public const string TEMP_PATH = "/data/local/tmp";
    public const string RECYCLE_FOLDER = ".Trash-AdbExplorer";
    public static readonly string RECYCLE_PATH = $"/sdcard/{RECYCLE_FOLDER}";
    public const string RECYCLE_INDEX_SUFFIX = ".index";

    public static readonly Dictionary<string, string> SPECIAL_FOLDERS_DISPLAY_NAMES = new()
    {
        { "/sdcard", "Internal Storage" },
        { "/storage/emulated/0", "Internal Storage" },
        { "/storage/self/primary", "Internal Storage" },
        { "/mnt/sdcard", "Internal Storage" },
        { "/", "Root" }
    };

    public static List<string> POSSIBLE_RECYCLE_PATHS =>
        SPECIAL_FOLDERS_DISPLAY_NAMES.Where(kv => kv.Value == "Internal Storage").Select(kv => $"{kv.Key}/{RECYCLE_FOLDER}").ToList();

    public static readonly Dictionary<string, AbstractDrive.DriveType> DRIVE_TYPES = new()
    {
        { "/sdcard", AbstractDrive.DriveType.Internal },
        { "/storage/emulated/0", AbstractDrive.DriveType.Internal },
        { "/storage/self/primary", AbstractDrive.DriveType.Internal },
        { "/mnt/sdcard", AbstractDrive.DriveType.Internal },
        { "/storage/emulated", AbstractDrive.DriveType.Internal },
        { RECYCLE_PATH, AbstractDrive.DriveType.Trash },
        { NavHistory.StringFromLocation(NavHistory.SpecialLocation.RecycleBin), AbstractDrive.DriveType.Trash },
        { TEMP_PATH, AbstractDrive.DriveType.Temp },
        { NavHistory.StringFromLocation(NavHistory.SpecialLocation.PackageDrive), AbstractDrive.DriveType.Package },
        { "/", AbstractDrive.DriveType.Root },
    };

    public static readonly Dictionary<AbstractDrive.DriveType, string> DRIVE_DISPLAY_NAMES = new()
    {
        { AbstractDrive.DriveType.Root, "Root" },
        { AbstractDrive.DriveType.Internal, "Internal Storage" },
        { AbstractDrive.DriveType.Expansion, "µSD Card" },
        { AbstractDrive.DriveType.External, "OTG Drive" },
        { AbstractDrive.DriveType.Unknown, "" }, // "Other Drive"
        { AbstractDrive.DriveType.Emulated, "Emulated Drive" },
        { AbstractDrive.DriveType.Trash, "Recycle Bin" },
        { AbstractDrive.DriveType.Temp, "Temp" },
        { AbstractDrive.DriveType.Package, "Installed Apps" },
    };

    public static readonly TimeSpan DIR_LIST_VISIBLE_PROGRESS_DELAY = TimeSpan.FromMilliseconds(500);
    public static readonly int DIR_LIST_START_COUNT = 100;
    public static readonly TimeSpan DIR_LIST_UPDATE_INTERVAL = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan DIR_LIST_UPDATE_START_INTERVAL = TimeSpan.FromMilliseconds(100);
    public static readonly TimeSpan SPLASH_DISPLAY_TIME = TimeSpan.FromMilliseconds(2000);
    public static readonly int DIR_LIST_UPDATE_THRESHOLD_MIN = 100;
    public static readonly int DIR_LIST_UPDATE_START_THRESHOLD_MIN = 10;
    public static readonly int DIR_LIST_UPDATE_THRESHOLD_MAX = 500;
    public static readonly TimeSpan MOUSE_DOWN_VALID = TimeSpan.FromMilliseconds(150);

    public static readonly TimeSpan CONNECT_TIMER_INTERVAL = TimeSpan.FromMilliseconds(2000);
    public static readonly TimeSpan CONNECT_TIMER_INIT = TimeSpan.FromMilliseconds(50);
    public static readonly TimeSpan DRIVE_UPDATE_INTERVAL = TimeSpan.FromMilliseconds(2000);
    public static readonly TimeSpan BATTERY_UPDATE_INTERVAL = TimeSpan.FromMilliseconds(8000);
    public static readonly TimeSpan MDNS_STATUS_UPDATE_INTERVAL = TimeSpan.FromMilliseconds(15);
    public static readonly TimeSpan RESPONSE_TIMER_INTERVAL = TimeSpan.FromMilliseconds(1000);
    public static readonly TimeSpan SERVER_RESPONSE_TIMEOUT = TimeSpan.FromMilliseconds(13000);
    public static readonly TimeSpan MDNS_DOWN_RESPONSE_TIME = TimeSpan.FromMilliseconds(12000);
    public static readonly TimeSpan SERVICE_DISPLAY_DELAY = TimeSpan.FromMilliseconds(2000);
    public static readonly TimeSpan EMPTY_FOLDER_NOTICE_DELAY = TimeSpan.FromMilliseconds(1500);
    public static readonly TimeSpan MDNS_FORCE_CONNECT_TIME = TimeSpan.FromMilliseconds(2500);
    public static readonly TimeSpan DISK_USAGE_INTERVAL_ACTIVE = TimeSpan.FromMilliseconds(200);
    public static readonly TimeSpan DISK_USAGE_INTERVAL_IDLE = TimeSpan.FromMilliseconds(1000);
    //public static readonly TimeSpan DRAG_EXIT_INTERVAL = TimeSpan.FromMilliseconds(200);

    public static readonly sbyte MIN_SUPPORTED_ANDROID_VER = 6;
    public static readonly sbyte MIN_PKG_UID_ANDROID_VER = 9;
    public static readonly double MAX_PANE_HEIGHT_RATIO = 0.4;
    public static readonly int MIN_PANE_HEIGHT = 150;
    public static readonly double MIN_PANE_HEIGHT_RATIO = 0.15;

    public static readonly string[] APK_NAMES = [".APK", ".XAPK", ".APKS", ".APKM", ".APEX"];
    public static readonly string[] INSTALL_APK = [".APK", ".APEX"];

    public static readonly UnicodeCategory[] UNICODE_ICONS = [UnicodeCategory.Surrogate, UnicodeCategory.PrivateUse, UnicodeCategory.OtherSymbol, UnicodeCategory.OtherNotAssigned];

    public static readonly char[] WIFI_PAIRING_ALPHABET = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!@#$%^&*()-+*/<>{}".ToCharArray();
    public const string PAIRING_SERVICE_PREFIX = "adbexplorer-";

    public static readonly char[] ESCAPE_ADB_SHELL_CHARS = ['(', ')', '<', '>', '|', ';', '&', '*', '\\', '~', '"', '\'', ' ', '$', '`'];
    public static readonly char[] INVALID_NTFS_CHARS = ['"', '*', '/', ':', '<', '>', '?', '\\', '|'];
    public static readonly char[] INVALID_UNIX_CHARS = ['/', '\\'];
    public static readonly string[] INVALID_WINDOWS_FILENAMES = ["CON", "PRN", "AUX", "NUL", "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM¹", "COM²", "COM³", "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT¹", "LPT²", "LPT³"];
    public static readonly string[] INVALID_WINDOWS_ROOT_PATHS = ["$AttrDef", "$BadClus", "$Bitmap", "$Boot", "$LogFile", "$MFT", "$MFTMirr", "$Secure", "$UpCase", "$Volume", "$Extend", @"$Extend\$ObjId", @"$Extend\$Quota", @"$Extend\$Reparse"];

    public static readonly Version MIN_ADB_VERSION = new(31, 0, 2);
    public static readonly Version WIN11_VERSION = new(10, 0, 22000);
    public static readonly Version WIN11_22H2 = new(10, 0, 22621);

    public static readonly TimeSpan SELECTION_CHANGED_DELAY = TimeSpan.FromMilliseconds(150);

    public const string ADB_EXPLORER_DATE_FORMAT = "yyyy.MM.dd-HH:mm:ss";

    public static readonly double WINDOW_HEIGHT_RATIO = 0.58;
    public static readonly double WINDOW_WIDTH_RATIO = 0.56;

    public static readonly double MAX_SEARCH_WIDTH_RATIO = 0.4;
    public static readonly double DEFAULT_SEARCH_WIDTH = 200;
    public static readonly double MAX_WINDOW_WIDTH_FOR_SEARCH_AUTO_COLLAPSE = 800;
    public static readonly double MIN_SEARCH_WIDTH = 100;

    public const string APP_SETTINGS_FILE = "App.txt";

    public static readonly int DRIVE_WARNING = 90;

    public const string WSA_INTERFACE_NAME = "WSLCore";
    public const string WSA_PROCESS_NAME = "WsaClient";
    public const string LOOPBACK_IP = "0.0.0.0";
    public const string WIN_LOOPBACK_ADDRESS = "127.0.0.1";
    public static readonly string[] LOOPBACK_ADDRESSES = [WIN_LOOPBACK_ADDRESS, LOOPBACK_IP];
    public const string WSA_PACKAGE_NAME = "Windows Subsystem for Android";
    public static readonly TimeSpan WSA_LAUNCH_DELAY = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan WSA_CONNECT_TIMEOUT = TimeSpan.FromSeconds(8);

    public static readonly TimeSpan EXPLORER_NAV_DELAY = TimeSpan.FromMilliseconds(800);
    public static readonly TimeSpan INIT_NAV_HIDE_FILTER_DELAY = TimeSpan.FromMilliseconds(1000);

    public static readonly ulong DISK_READ_THRESHOLD = 500000;
    public static readonly ulong DISK_WRITE_THRESHOLD = 500000;
    public static readonly ulong MAX_DISK_DISPLAY_RATE = 1000000000;

    public static readonly Dictionary<FileOpFilter.FilterType, string> FILE_OP_NAMES = new()
    {
        { FileOpFilter.FilterType.Running, "Running" },
        { FileOpFilter.FilterType.Pending, "Queued" },
        { FileOpFilter.FilterType.Completed, "Completed" },
        { FileOpFilter.FilterType.Validated, "Validated" },
        { FileOpFilter.FilterType.Failed, "Failed" },
        { FileOpFilter.FilterType.Canceled, "Canceled" },
        { FileOpFilter.FilterType.Previous, "Previous" },
    };

    public static readonly Point DRAG_OFFSET_DEFAULT = new(48, 89);

    public static readonly string TEMP_DRAG_FOLDER = $"TempDrag_{Helpers.RandomString.GetUniqueKey(3)}";
}
