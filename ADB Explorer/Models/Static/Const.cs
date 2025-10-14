namespace ADB_Explorer.Models;

public static class AdbExplorerConst
{
    public const string ADB_PROCESS = "adb";
    public const string APP_DATA_FOLDER = "AdbExplorer";
    public const string ADB_DRAG_FORMAT = "ADB Explorer Drag List";

    public const string DEFAULT_PATH = "/sdcard";
    public const string TEMP_PATH = "/data/local/tmp";
    public const string RECYCLE_FOLDER = ".Trash-AdbExplorer";
    public const string RECYCLE_PATH = $"/sdcard/{RECYCLE_FOLDER}";
    public const string RECYCLE_INDEX_SUFFIX = ".index";

    public static List<string> POSSIBLE_RECYCLE_PATHS =>
        [.. DRIVE_TYPES.Where(kv => kv.Value is AbstractDrive.DriveType.Internal).Select(kv => $"{kv.Key}/{RECYCLE_FOLDER}")];

    public static readonly Dictionary<string, AbstractDrive.DriveType> DRIVE_TYPES = new()
    {
        { "/sdcard", AbstractDrive.DriveType.Internal },
        { "/storage/emulated/0", AbstractDrive.DriveType.Internal },
        { "/storage/self/primary", AbstractDrive.DriveType.Internal },
        { "/mnt/sdcard", AbstractDrive.DriveType.Internal },
        { "/storage/emulated", AbstractDrive.DriveType.Internal },
        { RECYCLE_PATH, AbstractDrive.DriveType.Trash },
        { AdbLocation.StringFromLocation(Navigation.SpecialLocation.RecycleBin), AbstractDrive.DriveType.Trash },
        { TEMP_PATH, AbstractDrive.DriveType.Temp },
        { AdbLocation.StringFromLocation(Navigation.SpecialLocation.PackageDrive), AbstractDrive.DriveType.Package },
        { "/", AbstractDrive.DriveType.Root },
    };

    public const int DIR_LIST_START_COUNT = 100;
    public const int DIR_LIST_UPDATE_THRESHOLD_MIN = 100;
    public const int DIR_LIST_UPDATE_START_THRESHOLD_MIN = 10;
    public const int DIR_LIST_UPDATE_THRESHOLD_MAX = 500;

    public static readonly TimeSpan DIR_LIST_VISIBLE_PROGRESS_DELAY = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan DIR_LIST_UPDATE_INTERVAL = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan DIR_LIST_UPDATE_START_INTERVAL = TimeSpan.FromMilliseconds(100);
    public static readonly TimeSpan SPLASH_DISPLAY_TIME = TimeSpan.FromMilliseconds(2000);
    public static readonly TimeSpan MOUSE_DOWN_VALID = TimeSpan.FromMilliseconds(150);
    public static readonly TimeSpan LINK_CLICK_DELAY = TimeSpan.FromMilliseconds(300);
    public static readonly TimeSpan RENAME_CLICK_DELAY = TimeSpan.FromMilliseconds(500);
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

    public const sbyte MIN_SUPPORTED_ANDROID_VER = 6;
    public const sbyte MIN_PKG_UID_ANDROID_VER = 9;
    public const sbyte MIN_MEDIA_SCAN_ANDROID_VER = 10;

    public const double MAX_PANE_HEIGHT_RATIO = 0.4;
    public const int MIN_PANE_HEIGHT = 150;
    public const double MIN_PANE_HEIGHT_RATIO = 0.15;

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

    public const double WINDOW_HEIGHT_RATIO = 0.58;
    public const double WINDOW_WIDTH_RATIO = 0.56;

    public const double MAX_SEARCH_WIDTH_RATIO = 0.4;
    public const double DEFAULT_SEARCH_WIDTH = 200;
    public const double MAX_WINDOW_WIDTH_FOR_SEARCH_AUTO_COLLAPSE = 800;
    public const double MIN_SEARCH_WIDTH = 100;

    public const string APP_SETTINGS_FILE = "App.txt";

    public const int DRIVE_WARNING = 90;

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

    public const ulong DISK_READ_THRESHOLD = 500000;
    public const ulong DISK_WRITE_THRESHOLD = 500000;
    public const ulong MAX_DISK_DISPLAY_RATE = 1000000000;

    public static readonly Point DRAG_OFFSET_DEFAULT = new(48, 89);

    public static readonly string TEMP_DRAG_FOLDER = $"TempDrag_{Helpers.RandomString.GetUniqueKey(3, [.. WIFI_PAIRING_ALPHABET.Except(INVALID_NTFS_CHARS)])}";

    public static readonly string[] INCOMPATIBLE_APPS = [ "Files", "TOTALCMD", "TOTALCMD64" ];
}
