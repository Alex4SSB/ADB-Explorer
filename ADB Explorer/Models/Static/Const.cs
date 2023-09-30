namespace ADB_Explorer.Models;

public static class AdbExplorerConst
{
    public static readonly string PROGRESS_REDIRECTION_PATH = "AdbProgressRedirection.exe";
    public static readonly string TEMP_FILES_FOLDER = "TEMP";

    public static readonly string DEFAULT_PATH = "/sdcard";
    public static readonly string TEMP_PATH = "/data/local/tmp";
    public static readonly string RECYCLE_PATH = "/sdcard/.Trash-AdbExplorer";
    public static readonly string RECYCLE_INDEX_SUFFIX = ".index";

    public static readonly Dictionary<string, string> SPECIAL_FOLDERS_DISPLAY_NAMES = new()
    {
        { "/sdcard", "Internal Storage" },
        { "/storage/emulated/0", "Internal Storage" },
        { "/storage/self/primary", "Internal Storage" },
        { "/mnt/sdcard", "Internal Storage" },
        { "/", "Root" }
    };

    public static readonly Dictionary<string, AbstractDrive.DriveType> DRIVE_TYPES = new()
    {
        { "/storage/emulated", AbstractDrive.DriveType.Internal },
        { "/sdcard", AbstractDrive.DriveType.Internal },
        { "/", AbstractDrive.DriveType.Root },
        { RECYCLE_PATH, AbstractDrive.DriveType.Trash },
        { NavHistory.StringFromLocation(NavHistory.SpecialLocation.RecycleBin), AbstractDrive.DriveType.Trash },
        { TEMP_PATH, AbstractDrive.DriveType.Temp },
        { NavHistory.StringFromLocation(NavHistory.SpecialLocation.PackageDrive), AbstractDrive.DriveType.Package },
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

    public static readonly sbyte MIN_SUPPORTED_ANDROID_VER = 6;
    public static readonly sbyte MIN_PKG_UID_ANDROID_VER = 9;
    public static readonly double MAX_PANE_HEIGHT_RATIO = 0.4;
    public static readonly int MIN_PANE_HEIGHT = 150;
    public static readonly double MIN_PANE_HEIGHT_RATIO = 0.15;

    public static readonly string[] APK_NAMES = { ".APK", ".XAPK", ".APKS", ".APKM", ".APEX" };
    public static readonly string[] INSTALL_APK = { ".APK", ".APEX" };

    public static readonly UnicodeCategory[] UNICODE_ICONS = { UnicodeCategory.Surrogate, UnicodeCategory.PrivateUse, UnicodeCategory.OtherSymbol, UnicodeCategory.OtherNotAssigned };

    public static readonly char[] ALPHABET = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!@#$%^&*()-+*/<>{}".ToCharArray();
    public static readonly string PAIRING_SERVICE_PREFIX = "adbexplorer-";

    public static readonly char[] ESCAPE_ADB_SHELL_CHARS = { '(', ')', '<', '>', '|', ';', '&', '*', '\\', '~', '"', '\'', ' ', '$', '`' };
    public static readonly char[] INVALID_ANDROID_CHARS = { '"', '*', '/', ':', '<', '>', '?', '\\', '|' };

    public static readonly Version MIN_ADB_VERSION = new(31, 0, 2);
    public static readonly Version WIN11_VERSION = new(10, 0, 22000);
    public static readonly Version WIN11_22H2 = new(10, 0, 22621);

    public static readonly TimeSpan DOUBLE_CLICK_TIMEOUT = TimeSpan.FromMilliseconds(300);
    public static readonly TimeSpan SELECTION_CHANGED_DELAY = TimeSpan.FromMilliseconds(150);

    public static readonly string ADB_EXPLORER_DATE_FORMAT = "yyyy.MM.dd-HH:mm:ss";

    public static readonly double WINDOW_HEIGHT_RATIO = 0.58;
    public static readonly double WINDOW_WIDTH_RATIO = 0.56;

    public static readonly double MAX_SEARCH_WIDTH_RATIO = 0.4;
    public static readonly double DEFAULT_SEARCH_WIDTH = 200;
    public static readonly double MAX_WINDOW_WIDTH_FOR_SEARCH_AUTO_COLLAPSE = 800;
    public static readonly double MIN_SEARCH_WIDTH = 100;

    public static readonly string APP_SETTINGS_FILE = "App.txt";

    public static readonly int DRIVE_WARNING = 90;

    public static readonly string WSA_INTERFACE_NAME = "WSLCore";
    public static readonly string WSA_PROCESS_NAME = "WsaClient";
    public static readonly string LOOPBACK_IP = "0.0.0.0";
    public static readonly string WIN_LOOPBACK_ADDRESS = "127.0.0.1";
    public static readonly string[] LOOPBACK_ADDRESSES = { WIN_LOOPBACK_ADDRESS, LOOPBACK_IP };
    public static readonly string WSA_PACKAGE_NAME = "Windows Subsystem for Android™";
    public static readonly TimeSpan WSA_LAUNCH_DELAY = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan WSA_CONNECT_TIMEOUT = TimeSpan.FromSeconds(8);

    public static readonly TimeSpan EXPLORER_NAV_DELAY = TimeSpan.FromMilliseconds(800);
}
