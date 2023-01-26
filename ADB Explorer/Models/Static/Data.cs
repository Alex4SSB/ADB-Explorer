using ADB_Explorer.Helpers;
using ADB_Explorer.Services;
using ADB_Explorer.ViewModels;
using System.Drawing;

namespace ADB_Explorer.Models;

internal static class Data
{
    public static ADBService.AdbDevice CurrentADBDevice { get; set; } = null;

    public static string CurrentPath { get; set; }
    public static string ParentPath { get; set; }

    public static Dictionary<Tuple<string, bool>, Icon> FileIcons { get; set; } = new();

    public static FileOperationQueue FileOpQ { get; set; }

    public static Dictionary<string, string> CurrentDisplayNames { get; set; } = new();

    public static AppSettings Settings { get; set; } = new();

    public static AppRuntimeSettings RuntimeSettings { get; set; } = new();

    public static ObservableList<FileClass> CutItems { get; private set; } = new();

    public static ObservableCollection<Log> CommandLog { get; set; } = new();

    public static ObservableList<TrashIndexer> RecycleIndex { get; set; } = new();

    public static ObservableList<Package> Packages { get; set; } = new();

    public static Version AppVersion => new(Properties.Resources.AppVersion);

    public static FileActionsEnable FileActions { get; set; } = new();

    public static DirectoryLister DirList { get; set; }

    public static string IsolatedStorageLocation { get; set; } = "";

    public static string ProgressRedirectionPath { get; set; } = "AdbProgressRedirection.exe";

    public static Devices DevicesObject { get; set; } = new();

    public static MDNS MdnsService { get; set; } = new();

    public static PairingQrClass QrClass { get; set; }
}
