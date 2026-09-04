using ADB_Explorer.Helpers;
using ADB_Explorer.Services;
using ADB_Explorer.ViewModels;
using System.Diagnostics.CodeAnalysis;

namespace ADB_Explorer.Models;

public static class Data
{
    /// <summary>
    /// Explorer (and later, the active tab). Location-dependent chrome binds here.
    /// </summary>
    public static FileList Files { get; } = new();

    private static FileList? actionTarget;

    /// <summary>
    /// Action target: a tree context list, an inactive tab, or <see cref="Files"/>.
    /// </summary>
    public static FileList Active => actionTarget ?? Files;

    public static FileListScope Use(FileList list) => new(list);

    public readonly struct FileListScope : IDisposable
    {
        private readonly FileList? previous;

        internal FileListScope(FileList list)
        {
            previous = actionTarget;
            actionTarget = list;
        }

        public void Dispose() => actionTarget = previous;
    }

    public static string CurrentPath
    {
        get;
        [param: AllowNull]
        set
        {
            field = value ?? "";
            Files.Path = field;
            CurrentPathO.Value = field;
        }
    } = "";
    public static string ParentPath => FileHelper.GetParentPath(CurrentPath);

    /// <summary>
    /// Device path active when the user entered explorer search mode.
    /// </summary>
    public static string? SearchOriginPath { get; set; }

    /// <summary>
    /// Whether the search root allowed modifications when search mode was entered.
    /// </summary>
    public static bool SearchOriginCanWrite { get; set; }

    /// <summary>
    /// Optimized common parent for the current search-mode transfer batch.
    /// </summary>
    public static string? SearchTransferParent { get; set; }

    public static ObservableProperty<string> CurrentPathO { get; } = new();

    public static DriveViewModel? CurrentDrive
    {
        get => Active.CurrentDrive;
        set => Files.CurrentDrive = value;
    }

    // Created in MainWindow.Initialize after CheckAdbVersion succeeds; not available before then.
    public static FileOperationQueue FileOpQ { get; set; } = null!;

    public static Dictionary<string, string> CurrentDisplayNames { get; set; } = [];

    public static AppSettings Settings { get; set; } = new();

    public static AppRuntimeSettings RuntimeSettings { get; set; } = new();

    public static CopyPasteService CopyPaste { get; } = new();

    public static ObservableCollection<Log> CommandLog { get; set; } = [];

    public static ObservableList<TrashIndexer> RecycleIndex { get; set; } = [];

    public static ObservableList<Package> Packages { get; set; } = [];

    public static Version AppVersion => new(Properties.AppGlobal.AppVersion);

    public static FileActionsEnable FileActions => Files.Actions;

    public static DirectoryLister DirList
    {
        get => Active.DirList;
        set => Files.DirList = value;
    }

    public static string AppDataPath { get; set; } = "";

    // Created in MainWindow.Initialize after CheckAdbVersion succeeds; not available before then.
    public static Devices DevicesObject { get; set; } = null!;

    public static event EventHandler? DevicesObjectCreated;

    internal static void RaiseDevicesObjectCreated() => DevicesObjectCreated?.Invoke(null, EventArgs.Empty);

    public static MDNS MdnsService { get; } = new();

    public static IEnumerable<FileClass> SelectedFiles
    {
        get => Active.SelectedFiles ?? [];
        set => Files.SelectedFiles = value ?? [];
    }

    public static IEnumerable<Package> SelectedPackages
    {
        get => Active.SelectedPackages ?? [];
        set => Files.SelectedPackages = value ?? [];
    }

    public static ObservableProperty<Type> CurrentPage { get; set; } = new();

    public static event EventHandler? ClearLogs;

    public static void RaiseClearLogs() => ClearLogs?.Invoke(null, EventArgs.Empty);

    public static event EventHandler? ClearNavigationBox;
    public static void RaiseClearNavigationBox() => ClearNavigationBox?.Invoke(null, EventArgs.Empty);

    public static event EventHandler<bool>? UnfocusNavigationBox;
    public static void RaiseFocusNavigationBox(bool focus) => UnfocusNavigationBox?.Invoke(null, focus);

    public static event EventHandler? UnfocusSearchBox;
    public static void RaiseUnfocusSearchBox() => UnfocusSearchBox?.Invoke(null, EventArgs.Empty);

    public static event EventHandler? RunExplorerSearch;
    public static void RaiseRunExplorerSearch() => RunExplorerSearch?.Invoke(null, EventArgs.Empty);

    public static event EventHandler? ExitSearchMode;
    public static void RaiseExitSearchMode() => ExitSearchMode?.Invoke(null, EventArgs.Empty);

    public static ObservableProperty<bool> IsLogPaused { get; set; } = new();

    public static ObservableProperty<IBrowserItem?> ItemToSelect { get; set; } = new();

    public static CancellationTokenSource DeviceCts { get; set; } = new();
}
