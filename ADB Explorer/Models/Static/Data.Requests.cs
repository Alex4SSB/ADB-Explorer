namespace ADB_Explorer.Models;

public enum ExplorerRequest
{
    NewFolder,
    NewFile,
    PasteClipboardImage,
    Rename,
    SelectAll,
    ToggleSearch,
    FilterActions,
    FilterDrives,
    InitLister,
    DriveViewNav,
}

/// <summary>
/// App-wide requests for the explorer, raised by actions and handled by the active tab's explorer content.
/// </summary>
public static partial class Data
{
    public static event EventHandler<ExplorerRequest>? ExplorerRequested;
    public static void RequestExplorer(ExplorerRequest request) => ExplorerRequested?.Invoke(null, request);

    public static event EventHandler<AdbLocation>? NavigationRequested;
    public static void RequestNavigation(AdbLocation location) => NavigationRequested?.Invoke(null, location);

    /// <summary>
    /// Navigates to a path typed or restored for a tab; <c>"-"</c> goes back.
    /// </summary>
    public static event EventHandler<string>? PathNavigationRequested;
    public static void RequestPathNavigation(string path) => PathNavigationRequested?.Invoke(null, path);

    public static event EventHandler<DriveViewModel>? BrowseDriveRequested;
    public static void RequestBrowseDrive(DriveViewModel drive) => BrowseDriveRequested?.Invoke(null, drive);

    /// <summary>
    /// Starts the Compress-to rename flow for a tar extension (e.g. <c>.tar.gz</c>).
    /// </summary>
    public static event EventHandler<string>? CompressToRequested;
    public static void RequestCompressTo(string extension) => CompressToRequested?.Invoke(null, extension);

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
}
