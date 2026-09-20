using ADB_Explorer.Controls;
using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services.AppInfra;
using ADB_Explorer.ViewModels;
using ADB_Explorer.ViewModels.Pages;
using static ADB_Explorer.Services.FileAction;

namespace ADB_Explorer.Services;

public static class AppActions
{
    private static FileActionsEnable Actions => Data.Active.Actions;
    private static FileActionsEnable Explorer => Data.FileActions;

    private static ExplorerTabsViewModel? Tabs => App.Services.GetService<ExplorerTabsViewModel>();

    public static string Tooltip(FileActionType type)
    {
        var action = List.Find(a => a.Name == type);

        return $"{action.Description} ({action.GestureTooltip})";
    }

    private static void RunOnExplorer(Action action)
    {
        using (Data.Use(Data.Files))
            action();
    }

    private static readonly Dictionary<FileActionType, KeyGesture> Gestures = new()
    {
        { FileActionType.Filter, new(Key.F, ModifierKeys.Control) },
        { FileActionType.Cut, new(Key.X, ModifierKeys.Control) },
        { FileActionType.Copy, new(Key.C, ModifierKeys.Control) },
        { FileActionType.Refresh, new(Key.R, ModifierKeys.Control) },
        { FileActionType.Delete, new(Key.Delete) },
        { FileActionType.Uninstall, new(Key.F11, ModifierKeys.Shift) },
        { FileActionType.PushPackages, new(Key.I, ModifierKeys.Alt) },
        { FileActionType.Paste, new(Key.V, ModifierKeys.Control) },
    };

    public static readonly Dictionary<FileActionType, string> Icons = new()
    {
        { FileActionType.PushFolders, "\uE8B7" },
        { FileActionType.NewFile, "\uE8A5" },
        { FileActionType.Copy, "\uE8C8" },
        { FileActionType.Restore, "\uE845" },
        { FileActionType.Delete, "\uE74D" },
        { FileActionType.More, "\uE712" },
        { FileActionType.UpdateModified, "\uE787" },
        { FileActionType.EditCurrentPath, "\uE70F" },
        { FileActionType.FileOpRemove, "\uE711" },
        { FileActionType.PauseLogs, "\uE769" },
        { FileActionType.FollowLink, "\uE838" },
        { FileActionType.Refresh, "\uE72C" },
        { FileActionType.FileOpStop, "\uE768" },
        { FileActionType.ContextRemoveSavedLocation, "\uE8D9" },
    };

    public static BaseIcon Icon(FileActionType type, double size = 18) => type switch
    {
        FileActionType.CopyItemPath => new(new ItemPathIcon(), size),
        FileActionType.Cut => new(new CutIcon(), size),
        FileActionType.Delete => new(new DeleteIcon(), size),
        FileActionType.Restore => new(new RestoreIcon(), size),
        FileActionType.Install => new(new InstallIcon(), size),
        FileActionType.Uninstall => new(new UninstallIcon(), size),
        FileActionType.CopyLink => new(new DocumentLinkIcon(), size),
        FileActionType.ContextCopyAsImage => new(new ClipboardImageIcon(), size),
        FileActionType.CopyContents => new(new CopyArrowRightIcon(), size),
        FileActionType.ExtractHere => new(new FolderArrowRightIcon(), size),
        FileActionType.CompressTo or FileActionType.NewCompressTo or FileActionType.BackupPackage => new(new ZipIcon(), size, RtlBehavior.ForceLtr),
        FileActionType.PasteLink => new(new DocumentLinkIcon(), size),
        FileActionType.SearchApkOnWeb => new(new GlobeSearchIcon(), size),
        _ => new(Icons[type], size),
    };

    public static List<ToggleMenu> ToggleActions { get; } =
    [
        new(FileActionType.PauseLogs,
            () => true,
            Strings.Resources.S_LOG_UPDATES_PAUSE,
            Icon(FileActionType.FileOpStop, 20),
            () => Data.IsLogPaused.Value ^= true,
            Strings.Resources.S_LOG_UPDATES_ALT,
            Icon(FileActionType.PauseLogs, 20)),
    ];

    public static List<FileAction> List { get; } =
    [
        new(FileActionType.Back,
            () => NavHistory.BackAvailable && !Data.FileActions.ListingInProgress,
            () => Data.RuntimeSettings.LocationToNavigate = new(Navigation.SpecialLocation.Back),
            Strings.Resources.S_BUTTON_BACK,
            new(Key.Back),
            true),
        new(FileActionType.Forward,
            () => NavHistory.ForwardAvailable && !Data.FileActions.ListingInProgress,
            () => Data.RuntimeSettings.LocationToNavigate = new(Navigation.SpecialLocation.Forward),
            Strings.Resources.S_BUTTON_FORWARD,
            new(Key.Right, ModifierKeys.Alt),
            true),
        new(FileActionType.Up,
            () => Data.FileActions.ParentEnabled && !Data.FileActions.ListingInProgress && !Data.ActiveExplorerInstance.IsShowingPage,
            () => Data.RuntimeSettings.LocationToNavigate = new(Navigation.SpecialLocation.Up),
            Strings.Resources.S_BUTTON_UP,
            new(Key.Up, ModifierKeys.Alt),
            true),
        new(FileActionType.Filter,
            () => Data.FileActions.HomeEnabled,
            () => Data.RuntimeSettings.IsSearchBoxFocused ^= true,
            Strings.Resources.S_BUTTON_FILTER,
            Gestures[FileActionType.Filter]),
        new(FileActionType.KeyboardFilter,
            () => Data.FileActions.HomeEnabled && !Data.FileActions.IsExplorerEditing,
            () => Data.RuntimeSettings.IsSearchBoxFocused ^= true,
            Strings.Resources.S_BUTTON_FILTER,
            Gestures[FileActionType.Filter],
            true),
        new(FileActionType.Pull,
            () => Explorer.PullEnabled,
            () => RunOnExplorer(() => FileActionLogic.PullFiles()),
            Data.FileActions.PullDescription,
            new(Key.C, ModifierKeys.Alt),
            true,
            clearClipboard: true),
        new(FileActionType.ContextPull,
            () => Actions.PullEnabled,
            () => FileActionLogic.PullFiles(),
            Data.FileActions.PullDescription,
            new(Key.C, ModifierKeys.Alt),
            clearClipboard: true),
        new(FileActionType.Push,
            () => Explorer.PushEnabled,
            () => { },
            Strings.Resources.S_BUTTON_PUSH,
            clearClipboard: true),
        new(FileActionType.ContextPush,
            () => Actions.ContextPushEnabled,
            () => { },
            Strings.Resources.S_BUTTON_PUSH,
            clearClipboard: true),
        new(FileActionType.PushFolders,
            () => Explorer.PushFilesFoldersEnabled,
            () => RunOnExplorer(() => FileActionLogic.PushItems(true, false)),
            Strings.Resources.S_MENU_FOLDERS,
            clearClipboard: true),
        new(FileActionType.ContextPushFolders,
            () => Actions.PushFilesFoldersEnabled,
            () => FileActionLogic.PushItems(true, false),
            Strings.Resources.S_MENU_FOLDERS,
            clearClipboard: true),
        new(FileActionType.PushFiles,
            () => Explorer.PushFilesFoldersEnabled,
            () => RunOnExplorer(() => FileActionLogic.PushItems(false, false)),
            Strings.Resources.S_MENU_FILES,
            new(Key.V, ModifierKeys.Alt),
            true,
            clearClipboard: true),
        new(FileActionType.ContextPushFiles,
            () => Actions.PushFilesFoldersEnabled,
            () => FileActionLogic.PushItems(false, false),
            Strings.Resources.S_MENU_FILES,
            clearClipboard: true),
        new(FileActionType.Refresh,
            () => Data.FileActions.IsRefreshEnabled && !Data.FileActions.ListingInProgress,
            FileActionLogic.Refresh,
            Strings.Resources.S_MENU_REFRESH,
            new(Key.F5)),
        new(FileActionType.NavRefresh,
            () => !Data.ActiveExplorerInstance.IsShowingPage
                  && ((Data.FileActions.IsSearchMode && Data.FileActions.ListingInProgress)
                      || (Data.FileActions.IsRefreshEnabled && !Data.FileActions.ListingInProgress)),
            FileActionLogic.NavRefresh,
            Data.FileActions.NavRefreshDescription,
            new(Key.F5),
            true)
        {
            ExtraGestures = [Gestures[FileActionType.Refresh]],
        },
        new(FileActionType.CopyCurrentPath,
            () => Data.FileActions.IsCopyCurrentPathEnabled,
            () => Clipboard.SetText(Data.CurrentPath),
            Strings.Resources.S_MENU_COPY,
            new(Key.F6, ModifierKeys.Alt),
            true),
        new(FileActionType.EditCurrentPath,
            () => Data.FileActions.IsCopyCurrentPathEnabled,
            () => Data.RaiseFocusNavigationBox(true),
            Strings.Resources.S_MENU_EDIT,
            new(Key.F6),
            true),
        new(FileActionType.ContextNew,
            () => Actions.ContextNewEnabled,
            () => { },
            Strings.Resources.S_MENU_NEW),
        new(FileActionType.New,
            () => Explorer.NewEnabled,
            () => { },
            Strings.Resources.S_MENU_NEW),
        new(FileActionType.NewFolder,
            () => Explorer.NewEnabled,
            () => Data.RuntimeSettings.NewFolder = true,
            Strings.Resources.S_MENU_FOLDER,
            clearClipboard: true),
        new(FileActionType.ContextNewFolder,
            () => Actions.ContextNewEnabled,
            FileActionLogic.NewFolder,
            Strings.Resources.S_MENU_FOLDER,
            clearClipboard: true),
        new(FileActionType.NewFile,
            () => Explorer.NewEnabled && ReferenceEquals(Data.Active, Data.Files),
            () => Data.RuntimeSettings.NewFile = true,
            Strings.Resources.S_MENU_FILE,
            clearClipboard: true),
        new(FileActionType.NewCompressTo,
            () => Explorer.IsCompressToEnabled && ReferenceEquals(Data.Active, Data.Files),
            () => { },
            Strings.Resources.S_MENU_ARCHIVE),
        new(FileActionType.MoreCompress,
            () => Actions.IsCompressToContextEnabled,
            () => { },
            Strings.Resources.S_MENU_ARCHIVE),
        new(FileActionType.CompressTo,
            () => Actions.IsCompressToContextEnabled,
            () => { },
            Strings.Resources.S_MENU_COMPRESS_TO),
        new(FileActionType.CompressToTar,
            () => Actions.IsCompressToEnabled,
            () => FileActionLogic.BeginCompressTo(".tar"),
            "tar",
            clearClipboard: true),
        new(FileActionType.CompressToTarGz,
            () => Actions.IsCompressToEnabled,
            () => FileActionLogic.BeginCompressTo(".tar.gz"),
            "tar.gz",
            clearClipboard: true),
        new(FileActionType.CompressToTarBz2,
            () => Actions.IsCompressToEnabled,
            () => FileActionLogic.BeginCompressTo(".tar.bz2"),
            "tar.bz2",
            clearClipboard: true),
        new(FileActionType.CompressToTarXz,
            () => Actions.IsCompressToEnabled,
            () => FileActionLogic.BeginCompressTo(".tar.xz"),
            "tar.xz",
            clearClipboard: true),
        new(FileActionType.CompressToTarZst,
            () => Actions.IsCompressToEnabled,
            () => FileActionLogic.BeginCompressTo(".tar.zst"),
            "tar.zst",
            clearClipboard: true),
        new(FileActionType.SelectAll,
            () => Data.FileActions.IsExplorerVisible && !Data.FileActions.IsExplorerEditing,
            () => Data.RuntimeSettings.SelectAll = true,
            Strings.Resources.S_MENU_SELECT_ALL,
            new(Key.A, ModifierKeys.Control)),
        new(FileActionType.KeyboardCut,
            () => Explorer.CutEnabled && !Data.FileActions.IsExplorerEditing,
            () => RunOnExplorer(() => FileActionLogic.CutItems()),
            Strings.Resources.S_MENU_CUT,
            Gestures[FileActionType.Cut],
            true),
        new(FileActionType.Cut,
            () => Explorer.CutEnabled,
            () => RunOnExplorer(() => FileActionLogic.CutItems()),
            Strings.Resources.S_MENU_CUT,
            Gestures[FileActionType.Cut]),
        new(FileActionType.ContextCut,
            () => Actions.CutEnabled,
            () => FileActionLogic.CutItems(),
            Strings.Resources.S_MENU_CUT,
            Gestures[FileActionType.Cut]),
        new(FileActionType.KeyboardCopy,
            () => Explorer.CopyEnabled && !Data.FileActions.IsExplorerEditing,
            () => RunOnExplorer(() => FileActionLogic.CutItems(true)),
            Strings.Resources.S_MENU_COPY,
            Gestures[FileActionType.Copy],
            true),
        new(FileActionType.Copy,
            () => Explorer.CopyEnabled,
            () => RunOnExplorer(() => FileActionLogic.CutItems(true)),
            Strings.Resources.S_MENU_COPY,
            Gestures[FileActionType.Copy]),
        new(FileActionType.ContextCopy,
            () => Actions.CopyEnabled,
            () => FileActionLogic.CutItems(true),
            Strings.Resources.S_MENU_COPY,
            Gestures[FileActionType.Copy]),
        new(FileActionType.CopyLink,
            () => Explorer.IsCopyLinkEnabled,
            () => RunOnExplorer(() => FileActionLogic.CopyLinkFiles(Data.SelectedFiles)),
            Strings.Resources.S_MENU_COPY_LINK,
            new(Key.L, ModifierKeys.Control | ModifierKeys.Shift),
            true),
        new(FileActionType.ContextCopyLink,
            () => Actions.IsCopyLinkEnabled,
            () => FileActionLogic.CopyLinkFiles(Data.SelectedFiles),
            Strings.Resources.S_MENU_COPY_LINK,
            new(Key.L, ModifierKeys.Control | ModifierKeys.Shift)),
        new(FileActionType.ContextCopyAsImage,
            () => Actions.IsCopyAsImageEnabled,
            FileActionLogic.CopyAsImage,
            Strings.Resources.S_MENU_COPY_AS_IMAGE),
        new(FileActionType.CopyContents,
            () => Actions.IsCopyContentsEnabled,
            FileActionLogic.CopyArchiveContents,
            Strings.Resources.S_MENU_COPY_CONTENTS),
        new(FileActionType.ExtractHere,
            () => Actions.IsExtractHereEnabled,
            FileActionLogic.ExtractArchiveHere,
            Strings.Resources.S_MENU_EXTRACT_HERE),
        new(FileActionType.KeyboardPaste,
            () => ((Explorer.IsKeyboardPasteEnabled && Data.CopyPaste.HasFiles) || FileActionLogic.CanPasteClipboardImageAtSelection(isKeyboard: true)) && !Data.FileActions.IsExplorerEditing,
            () => RunOnExplorer(() => FileActionLogic.PasteFiles(Data.SelectedFiles)),
            Data.FileActions.PasteDescription,
            Gestures[FileActionType.Paste],
            true),
        new(FileActionType.Paste,
            () => (Explorer.PasteEnabled && Data.CopyPaste.HasFiles) || FileActionLogic.CanPasteClipboardImageAtSelection(),
            () => RunOnExplorer(() => FileActionLogic.PasteFiles(Data.SelectedFiles)),
            Data.FileActions.PasteDescription,
            Gestures[FileActionType.Paste]),
        new(FileActionType.ContextPaste,
            () => (Actions.PasteEnabled && Data.CopyPaste.HasFiles) || FileActionLogic.CanPasteClipboardImageAtSelection(),
            () => FileActionLogic.PasteFiles(Data.SelectedFiles),
            Data.FileActions.PasteDescription,
            Gestures[FileActionType.Paste]),
        new(FileActionType.PasteLink,
            () => Explorer.IsPasteLinkEnabled,
            () => RunOnExplorer(() => FileActionLogic.PasteFiles(Data.SelectedFiles, isLink: true)),
            Strings.Resources.S_MENU_PASTE_LINK,
            new(Key.L, ModifierKeys.Control),
            true),
        new(FileActionType.ContextPasteLink,
            () => Actions.IsPasteLinkEnabled,
            () => FileActionLogic.PasteFiles(Data.SelectedFiles, isLink: true),
            Strings.Resources.S_MENU_PASTE_LINK,
            new(Key.L, ModifierKeys.Control)),
        new(FileActionType.Rename,
            () => Explorer.RenameEnabled,
            () => Data.RuntimeSettings.Rename = true,
            Strings.Resources.S_MENU_RENAME,
            new(Key.F2),
            true,
            clearClipboard: true),
        new(FileActionType.ContextRename,
            () => Actions.RenameEnabled,
            FileActionLogic.ContextRename,
            Strings.Resources.S_MENU_RENAME,
            new(Key.F2),
            clearClipboard: true),
        new(FileActionType.Restore,
            () => Explorer.RestoreEnabled,
            () => RunOnExplorer(FileActionLogic.RestoreItems),
            Data.FileActions.RestoreDescription,
            clearClipboard: true),
        new(FileActionType.ContextRestore,
            () => Actions.RestoreEnabled,
            FileActionLogic.RestoreItems,
            Data.FileActions.RestoreDescription,
            clearClipboard: true),
        new(FileActionType.KeyboardDelete,
            () => Explorer.DeleteEnabled && !Data.FileActions.IsExplorerEditing,
            () => RunOnExplorer(() => FileActionLogic.DeleteFiles()),
            Data.FileActions.DeleteDescription,
            Gestures[FileActionType.Delete],
            true,
            clearClipboard: true),
        new(FileActionType.ContextDelete,
            () => Actions.DeleteEnabled,
            () => FileActionLogic.DeleteFiles(Data.FileActions.ContextDeleteDescription.Value == Strings.Resources.S_PERM_DEL),
            Data.FileActions.ContextDeleteDescription,
            Gestures[FileActionType.Delete],
            clearClipboard: true),
        new(FileActionType.Delete,
            () => Explorer.DeleteEnabled,
            () => RunOnExplorer(() => FileActionLogic.DeleteFiles()),
            Data.FileActions.DeleteDescription,
            Gestures[FileActionType.Delete],
            clearClipboard: true),
        new(FileActionType.CopyItemPath,
            () => Explorer.IsCopyItemPathEnabled,
            () => RunOnExplorer(FileActionLogic.CopyItemPath),
            Data.FileActions.CopyPathDescription,
            new(Key.C, ModifierKeys.Control | ModifierKeys.Shift),
            true),
        new(FileActionType.ContextCopyItemPath,
            () => Actions.IsCopyItemPathEnabled,
            FileActionLogic.CopyItemPath,
            Data.FileActions.CopyPathDescription,
            new(Key.C, ModifierKeys.Control | ModifierKeys.Shift)),
        new(FileActionType.Package,
            () => Actions.PackageActionsEnabled,
            () => { },
            Strings.Resources.S_MENU_PACKAGE),
        new(FileActionType.UpdateModified,
            () => Actions.UpdateModifiedEnabled,
            FileActionLogic.UpdateModifiedDates,
            Strings.Resources.S_MENU_UPDATE_MODIFIED,
            info: Strings.Resources.S_UPDATE_MODIFIED_INFO),
        new(FileActionType.Install,
            () => Explorer.InstallUninstallEnabled,
            () => RunOnExplorer(FileActionLogic.InstallPackages),
            Strings.Resources.S_MENU_INSTALL,
            new(Key.F10, ModifierKeys.Shift),
            true),
        new(FileActionType.ContextInstall,
            () => Actions.InstallUninstallEnabled,
            FileActionLogic.InstallPackages,
            Strings.Resources.S_MENU_INSTALL,
            new(Key.F10, ModifierKeys.Shift)),
        new(FileActionType.Uninstall,
            () => Explorer.UninstallPackageEnabled,
            () => RunOnExplorer(FileActionLogic.UninstallPackages),
            Strings.Resources.S_UNINSTALL,
            Gestures[FileActionType.Uninstall],
            true),
        new(FileActionType.ContextUninstall,
            () => Actions.UninstallPackageEnabled,
            FileActionLogic.UninstallPackages,
            Strings.Resources.S_UNINSTALL,
            Gestures[FileActionType.Uninstall]),
        new(FileActionType.BackupPackage,
            () => Explorer.BackupPackageEnabled,
            () => RunOnExplorer(FileActionLogic.BackupPackages),
            Strings.Resources.S_MENU_BACKUP_PACKAGE,
            info: Strings.Resources.S_BACKUP_PACKAGE_INFO),
        new(FileActionType.ContextBackupPackage,
            () => Actions.BackupPackageEnabled,
            FileActionLogic.BackupPackages,
            Strings.Resources.S_MENU_BACKUP_PACKAGE,
            info: Strings.Resources.S_BACKUP_PACKAGE_INFO),
        new(FileActionType.SubMenuUninstall,
            () => Actions.SubmenuUninstallEnabled,
            FileActionLogic.UninstallPackages,
            Strings.Resources.S_UNINSTALL,
            Gestures[FileActionType.Uninstall]),
        new(FileActionType.PushPackages,
            () => Data.FileActions.PushPackageEnabled,
            FileActionLogic.PushPackages,
            Strings.Resources.S_PUSH_PKG,
            Gestures[FileActionType.PushPackages],
            true,
            info: Strings.Resources.S_PUSH_PACKAGES_INFO),
        new(FileActionType.ContextPushPackages,
            () => Actions.ContextPushPackagesEnabled,
            FileActionLogic.PushPackages,
            Strings.Resources.S_PUSH_PKG,
            Gestures[FileActionType.PushPackages],
            info: Strings.Resources.S_PUSH_PACKAGES_INFO),
        new(FileActionType.None,
            new(),
            "",
            new(Key.F10),
            true),
        ToggleActions.Find(a => a.FileAction.Name is FileActionType.PauseLogs).FileAction,
        new(FileActionType.ClearLogs,
            () => Data.CommandLog.Count > 0,
            Data.RaiseClearLogs,
            Strings.Resources.S_MENU_CLEAR_LOG),
        new(FileActionType.FollowLink,
            () => Explorer.IsFollowLinkEnabled,
            () => RunOnExplorer(FileActionLogic.FollowLink),
            Strings.Resources.S_MENU_OPEN_LOCATION,
            new(Key.Enter, ModifierKeys.Shift),
            true),
        new(FileActionType.ContextFollowLink,
            () => Actions.IsFollowLinkEnabled,
            FileActionLogic.FollowLink,
            Strings.Resources.S_MENU_OPEN_LOCATION,
            new(Key.Enter, ModifierKeys.Shift)),
        new(FileActionType.ContextOpenItemLocation,
            () => Actions.IsOpenItemLocationEnabled,
            FileActionLogic.OpenItemLocation,
            Strings.Resources.S_MENU_OPEN_LOCATION),
        new(FileActionType.SearchApkOnWeb,
            () => Explorer.IsApkWebSearchEnabled,
            () => RunOnExplorer(FileActionLogic.ApkWebSearch),
            Strings.Resources.S_MENU_SEARCH_WEB,
            new(Key.O, ModifierKeys.Control),
            true),
        new(FileActionType.ContextSearchApkOnWeb,
            () => Actions.IsApkWebSearchEnabled,
            FileActionLogic.ApkWebSearch,
            Strings.Resources.S_MENU_SEARCH_WEB,
            new(Key.O, ModifierKeys.Control)),
        new(FileActionType.NavHistory,
            () => NavHistory.MenuHistory.Value.Any() && !Data.FileActions.ListingInProgress,
            () => { },
            Strings.Resources.S_NAV_HISTORY),
        new(FileActionType.OpenPackageLocation,
            () => Explorer.IsOpenApkLocationEnabled,
            () => RunOnExplorer(() => FileActionLogic.OpenApkLocation()),
            Strings.Resources.S_MENU_OPEN_LOCATION,
            new(Key.Enter),
            true),
        new(FileActionType.ContextOpenPackageLocation,
            () => Actions.IsOpenApkLocationEnabled,
            () => FileActionLogic.OpenApkLocation(),
            Strings.Resources.S_MENU_OPEN_LOCATION,
            new(Key.Enter)),
        new(FileActionType.Enter,
            () => Actions.IsSingleFolder,
            FileActionLogic.EnterFolder,
            Strings.Resources.S_OPEN_FOLDER,
            new(Key.Enter)),
        new(FileActionType.ContextOpenInNewTab,
            () => Actions.IsOpenInNewTabEnabled,
            FileActionLogic.OpenInNewTab,
            Strings.Resources.S_OPEN_IN_NEW_TAB),
        new(FileActionType.NewTab,
            () => Tabs is not null && AdbHelper.CurrentAdbState.Status is AdbHelper.AdbStatus.Valid,
            () => Tabs?.AddTab(),
            Strings.Resources.S_NEW_TAB,
            new(Key.T, ModifierKeys.Control),
            true),
        new(FileActionType.CloseTab,
            () => Tabs is { ActiveTab: not null, Tabs.Count: > 1 },
            () => Tabs?.CloseTab(Tabs.ActiveTab!),
            Strings.Resources.S_CLOSE_TAB,
            new(Key.W, ModifierKeys.Control),
            true),
        new(FileActionType.ContextRemoveSavedLocation,
            () => Actions.RemoveSavedLocationEnabled,
            FileActionLogic.RemoveSavedLocation,
            Strings.Resources.S_MENU_REMOVE_SAVED_LOCATION),
    ];

    public static List<KeyBinding> Bindings =>
        [.. List.Where(a => a.UseForGesture)
            .SelectMany(action => action.ExtraKeyBindings.Prepend(action.KeyBinding))
            .OfType<KeyBinding>()];

}

public class FileAction : ViewModelBase
{
    public enum FileActionType
    {
        None,
        Back,
        Forward,
        Up,
        Refresh,
        NavRefresh,
        CopyCurrentPath,
        EditCurrentPath,
        Filter,
        KeyboardFilter,
        Pull,
        ContextPull,
        Push,
        ContextPush,
        PushFolders,
        ContextPushFolders,
        PushFiles,
        ContextPushFiles,
        ContextNew,
        New,
        NewFolder,
        ContextNewFolder,
        NewFile,
        NewCompressTo,
        MoreCompress,
        CompressTo,
        CompressToTar,
        CompressToTarGz,
        CompressToTarBz2,
        CompressToTarXz,
        CompressToTarZst,
        SelectAll,
        KeyboardCut,
        Cut,
        ContextCut,
        KeyboardCopy,
        Copy,
        ContextCopy,
        KeyboardPaste,
        Paste,
        ContextPaste,
        PasteLink,
        ContextPasteLink,
        CopyLink,
        ContextCopyLink,
        ContextCopyAsImage,
        Rename,
        ContextRename,
        Restore,
        ContextRestore,
        KeyboardDelete,
        Delete,
        ContextDelete,
        CopyItemPath,
        ContextCopyItemPath,
        More,
        UpdateModified,
        Package,
        Install,
        ContextInstall,
        Uninstall,
        ContextUninstall,
        SubMenuUninstall,
        PushPackages,
        ContextPushPackages,
        BackupPackage,
        ContextBackupPackage,
        FileOpStop,
        FileOpRemove,
        PauseLogs,
        ClearLogs,
        FollowLink,
        ContextFollowLink,
        ContextOpenItemLocation,
        CopyContents,
        ExtractHere,
        SearchApkOnWeb,
        ContextSearchApkOnWeb,
        NavHistory,
        OpenPackageLocation,
        ContextOpenPackageLocation,
        Enter,
        ContextRemoveSavedLocation,
        ContextOpenInNewTab,
        NewTab,
        CloseTab,
    }

    public FileActionType Name { get; }

    public BaseAction Command { get; }

    public KeyGesture? Gesture { get; }

    public KeyBinding? KeyBinding { get; }

    public string Description { get; private set; }

    public bool UseForGesture { get; }

    /// <summary>Additional shortcuts for the same command; listed in the tooltip.</summary>
    public KeyGesture[] ExtraGestures { get; init; } = [];

    public IEnumerable<KeyBinding> ExtraKeyBindings => ExtraGestures.Select(gesture => new KeyBinding(Command.Command, gesture));

    public string GestureString => Gesture is null ? null : FormatGesture(Gesture);

    public string GestureTooltip => string.Join(", ", ExtraGestures.Prepend(Gesture).OfType<KeyGesture>().Select(FormatGesture));

    private static string FormatGesture(KeyGesture gesture)
    {
        string result = "";
        if (gesture.Modifiers is not ModifierKeys.None)
        {
            result = gesture.Modifiers.ToString();

            result = result.Replace("Control", "Ctrl");
            result = result.Replace(",", "+");
            result = result.Replace(" ", "");

            result += "+";
        }

        string key = gesture.Key.ToString();
        if (key.Length > 1 && key[0] == 'D' && char.IsDigit(key[1]))
            key = key[1..];

        result += key;

        result = result.Replace("Delete", "Del");
        result = result.Replace("Return", "Enter");

        return result;
    }

    public string? Info { get; }

    public FileAction(FileActionType name,
                      BaseAction command,
                      string description,
                      KeyGesture? gesture = null,
                      bool useForGesture = false,
                      bool clearClipboard = false,
                      string? info = null)
    {
        Name = name;
        Command = command;
        Gesture = gesture;
        Description = description;
        Info = info;

        if (gesture is not null)
            KeyBinding = new(Command.Command, gesture);

        UseForGesture = useForGesture;

        ((CommandHandler)Command.Command).OnExecute.PropertyChanged += (object? sender, PropertyChangedEventArgs<bool> e) =>
        {
            if (clearClipboard && Data.CopyPaste.IsSelf)
                Data.CopyPaste.Clear();
        };
    }

    public FileAction(FileActionType name,
                      Func<bool> canExecute,
                      Action action,
                      string description = "",
                      KeyGesture? gesture = null,
                      bool useForGesture = false,
                      bool clearClipboard = false,
                      string? info = null)
        : this(name, new(canExecute, action), description, gesture, useForGesture, clearClipboard, info)
    { }

    public FileAction(FileActionType name,
                      Func<bool> canExecute,
                      Action action,
                      ObservableProperty<string> description,
                      KeyGesture? gesture = null,
                      bool useForGesture = false,
                      bool clearClipboard = false,
                      string? info = null)
        : this(name, new(canExecute, action), description.Value, gesture, useForGesture, clearClipboard, info)
    {
        description.PropertyChanged += (object? sender, PropertyChangedEventArgs<string> e) =>
        {
            Description = e.NewValue;
            OnPropertyChanged(nameof(Description));
        };
    }

    public override string ToString()
    {
        return Name.ToString();
    }
}
