namespace ADB_Explorer.Models;

public static partial class Data
{
    /// <summary>
    /// The active tab's full state. Only one instance exists for now; a future extra tab
    /// would add more and repoint this at whichever is focused.
    /// </summary>
    public static ExplorerInstance ActiveExplorerInstance { get; set; } = new();

    /// <summary>Lets XAML bind window-wide state without searching up to a Window that a cached page content control may not have.</summary>
    public static MainWindowViewModel MainWindowVM => App.Services.GetRequiredService<MainWindowViewModel>();

    /// <summary>
    /// Explorer (and later, the active tab). Location-dependent chrome binds here.
    /// </summary>
    public static FileList Files => ActiveExplorerInstance.FileList;

    private static FileList? _actionTarget;

    /// <summary>
    /// Action target: a tree context list, an inactive tab, or <see cref="Files"/>.
    /// </summary>
    public static FileList Active => _actionTarget ?? Files;

    public static FileListScope Use(FileList list) => new(list);

    /// <summary>Makes <paramref name="instance"/> the active one until disposed, so the static forwarders above read its state.</summary>
    public static InstanceScope UseInstance(ExplorerInstance instance) => new(instance);

    /// <summary>
    /// Runs <paramref name="action"/> for every open pane (any tab, split panes included) that
    /// satisfies <paramref name="match"/>, with that pane made active for the duration. The second
    /// argument tells whether it is the pane that was active beforehand. Returns whether any matched.
    /// </summary>
    public static bool ForEachInstance(Func<ExplorerInstance, bool> match, Action<ExplorerInstance, bool> action)
    {
        var any = false;

        // On the UI thread, since the active instance is swapped while each pane runs.
        App.SafeInvoke(() =>
        {
            var focused = ActiveExplorerInstance;
            var instances = new HashSet<ExplorerInstance> { focused };

            if (App.Services.GetService<ExplorerTabsViewModel>() is { } tabs)
                instances.UnionWith(tabs.AllInstances);

            var matching = instances.Where(match).ToList();
            foreach (var instance in matching)
            {
                using (UseInstance(instance))
                    action(instance, ReferenceEquals(instance, focused));
            }

            any = matching.Count > 0;
        });

        return any;
    }

    /// <summary>Runs <paramref name="action"/> for every pane listing <paramref name="path"/> on the device <paramref name="deviceId"/>.</summary>
    public static bool ForEachListingAt(string path, string? deviceId, Action<ExplorerInstance, bool> action)
        => ForEachInstance(instance => instance.EffectiveDevice?.ID == deviceId && instance.FileList.Path == path, action);

    /// <summary>
    /// Like <see cref="ForEachListingAt"/>, plus panes showing search results, which list items from any folder.
    /// For changes to an existing item only - a search doesn't gain items it never matched.
    /// </summary>
    public static bool ForEachListingHolding(string path, string? deviceId, Action<ExplorerInstance, bool> action)
        => ForEachInstance(
            instance => instance.EffectiveDevice?.ID == deviceId
                && (instance.FileList.Path == path || instance.FileList.Actions.IsSearchMode),
            action);

    public readonly struct InstanceScope : IDisposable
    {
        private readonly ExplorerInstance _previous;

        internal InstanceScope(ExplorerInstance instance)
        {
            _previous = ActiveExplorerInstance;
            ActiveExplorerInstance = instance;
        }

        public void Dispose() => ActiveExplorerInstance = _previous;
    }

    public readonly struct FileListScope : IDisposable
    {
        private readonly FileList? _previous;

        internal FileListScope(FileList list)
        {
            _previous = _actionTarget;
            _actionTarget = list;
        }

        public void Dispose() => _actionTarget = _previous;
    }

    public static string CurrentPath
    {
        get => Files.Path;
        [param: AllowNull]
        set
        {
            Files.Path = value ?? "";
            CurrentPathO.Value = Files.Path;
        }
    }
    public static string ParentPath => FileHelper.GetParentPath(CurrentPath);

    /// <summary>
    /// Device path active when the focused pane entered explorer search mode.
    /// </summary>
    public static string? SearchOriginPath
    {
        get => Files.SearchOriginPath;
        set => Files.SearchOriginPath = value;
    }

    /// <summary>
    /// Whether the focused pane's search root allowed modifications when search mode was entered.
    /// </summary>
    public static bool SearchOriginCanWrite
    {
        get => Files.SearchOriginCanWrite;
        set => Files.SearchOriginCanWrite = value;
    }

    /// <summary>
    /// Optimized common parent for the focused pane's current search-mode transfer batch.
    /// </summary>
    public static string? SearchTransferParent
    {
        get => Files.SearchTransferParent;
        set => Files.SearchTransferParent = value;
    }

    /// <summary>Full paths matched by the "File contents" search option in Current Folder scope;
    /// <see langword="null"/> when not applicable/stale.</summary>
    public static HashSet<string>? ContentSearchMatches { get; set; }

    public static ObservableProperty<string> CurrentPathO { get; } = new();

    public static DriveViewModel? CurrentDrive
    {
        get => Active.CurrentDrive;
        set => Files.CurrentDrive = value;
    }

    // Created in MainWindow.Initialize after CheckAdbVersion succeeds; not available before then.
    public static FileOperationQueue FileOpQ { get; set; } = null!;

    /// <summary>Friendly display names for special/drive paths, keyed by owning device so two
    /// devices open in different tabs don't clobber each other's entries for the same path.</summary>
    public static Dictionary<(string? DeviceId, string Path), string> CurrentDisplayNames { get; set; } = [];

    public static AppSettings Settings { get; set; } = new();

    public static AppRuntimeSettings RuntimeSettings { get; set; } = new();

    public static CopyPasteService CopyPaste { get; } = new();

    public static ObservableCollection<Log> CommandLog { get; set; } = [];

    public static ObservableList<TrashIndexer> RecycleIndex { get; set; } = [];

    public static ObservableList<Package> Packages { get; set; } = [];

    public static Version AppVersion => new(Properties.AppGlobal.AppVersion);

    public static FileActionsEnable FileActions => Files.Actions;

    /// <summary>The active tab's device: its own once pinned to one, else the app-wide current.
    /// Use this, not <see cref="Devices.Current"/>, for anything acting on the visible tab.</summary>
    public static LogicalDeviceViewModel? ActiveDevice => ActiveExplorerInstance.EffectiveDevice;

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

    public static MdnsService MdnsService { get; } = new();

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

    public static ObservableProperty<bool> IsLogPaused { get; set; } = new();

    public static ObservableProperty<IBrowserItem?> ItemToSelect { get; set; } = new();

    public static CancellationTokenSource DeviceCts { get; set; } = new();
}
