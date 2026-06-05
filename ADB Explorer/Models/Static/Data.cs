using ADB_Explorer.Helpers;
using ADB_Explorer.Services;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Models;

public static class Data
{
    public static string CurrentPath 
    {
        get;
        set
        {
            field = value;
            CurrentPathO.Value = value;
        }
    } = "";
    public static string ParentPath => FileHelper.GetParentPath(CurrentPath);

    public static ObservableProperty<string> CurrentPathO { get; } = new();

    public static DriveViewModel CurrentDrive { get; set; } = null;

    public static FileOperationQueue FileOpQ { get; set; }

    public static Dictionary<string, string> CurrentDisplayNames { get; set; } = [];

    public static AppSettings Settings { get; set; } = new();

    public static AppRuntimeSettings RuntimeSettings { get; set; } = new();

    public static CopyPasteService CopyPaste { get; } = new();

    public static ObservableCollection<Log> CommandLog { get; set; } = [];

    public static ObservableList<TrashIndexer> RecycleIndex { get; set; } = [];

    public static ObservableList<Package> Packages { get; set; } = [];

    public static Version AppVersion => new(Properties.AppGlobal.AppVersion);

    public static FileActionsEnable FileActions { get; set; } = new();

    public static DirectoryLister DirList { get; set; }

    public static string AppDataPath { get; set; } = "";

    public static Devices DevicesObject { get; set; }

    public static MDNS MdnsService { get; } = new();

    public static IEnumerable<FileClass> SelectedFiles { get; set; } = [];

    public static IEnumerable<Package> SelectedPackages { get; set; } = [];

    public static ObservableProperty<Type> CurrentPage { get; set; } = new();

    public static event EventHandler? ClearLogs;

    public static void RaiseClearLogs() => ClearLogs?.Invoke(null, EventArgs.Empty);

    public static event EventHandler? ClearNavigationBox;
    public static void RaiseClearNavigationBox() => ClearNavigationBox?.Invoke(null, EventArgs.Empty);

    public static event EventHandler<bool>? UnfocusNavigationBox;
    public static void RaiseFocusNavigationBox(bool focus) => UnfocusNavigationBox?.Invoke(null, focus);

    public static event EventHandler? UnfocusSearchBox;
    public static void RaiseUnfocusSearchBox() => UnfocusSearchBox?.Invoke(null, EventArgs.Empty);

    public static ObservableProperty<bool> IsLogPaused { get; set; } = new();

    public static ObservableProperty<IBrowserItem?> ItemToSelect { get; set; } = new();

    public static CancellationTokenSource DeviceCts { get; set; } = new();
}
