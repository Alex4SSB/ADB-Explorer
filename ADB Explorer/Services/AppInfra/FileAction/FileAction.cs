using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Resources;
using ADB_Explorer.Services.AppInfra;
using ADB_Explorer.ViewModels;
using static ADB_Explorer.Services.FileAction;

namespace ADB_Explorer.Services;

internal static class AppActions
{
    private static readonly Dictionary<FileActionType, KeyGesture> Gestures = new()
    {
        { FileActionType.Home, new(Key.H, ModifierKeys.Alt) },
        { FileActionType.Filter, new(Key.F, ModifierKeys.Control) },
        { FileActionType.Cut, new(Key.X, ModifierKeys.Control) },
        { FileActionType.Copy, new(Key.C, ModifierKeys.Control) },
        { FileActionType.Restore, new(Key.R, ModifierKeys.Control) },
        { FileActionType.Delete, new(Key.Delete) },
        { FileActionType.Edit, new(Key.E, ModifierKeys.Control) },
        { FileActionType.Uninstall, new(Key.F11, ModifierKeys.Shift) },
        { FileActionType.PushPackages, new(Key.I, ModifierKeys.Alt) },
        { FileActionType.OpenSettings, new(Key.D0, ModifierKeys.Alt) },
        { FileActionType.Paste, new(Key.V, ModifierKeys.Control) },
    };

    public static readonly Dictionary<FileActionType, string> Icons = new()
    {
        { FileActionType.OpenFileOps, "\uF16A" },
        { FileActionType.FileOpPastView, "\uE9D5" },
        { FileActionType.PushFolders, "\uE8B7" },
        { FileActionType.NewFile, "\uE8A5" },
        { FileActionType.Package, "\uE7B8" },
        { FileActionType.New, "\uECC8" },
        { FileActionType.Cut, "\uE8C6" },
        { FileActionType.Copy, "\uE8C8" },
        { FileActionType.Paste, "\uE77F" },
        { FileActionType.Rename, "\uE8AC" },
        { FileActionType.Restore, "\uE845" },
        { FileActionType.Delete, "\uE74D" },
        { FileActionType.Uninstall, "\uE25B" },
        { FileActionType.More, "\uE712" },
        { FileActionType.UpdateModified, "\uE787" },
        { FileActionType.Edit, "\uE70F" },
        { FileActionType.Install, "\uE896" },
        { FileActionType.CopyToTemp, "\uF413" },
        { FileActionType.FileOpRemove, "\uE711" },
    };

    public static List<ToggleMenu> ToggleActions { get; } = new()
    {
        new(FileActionType.FileOpStop,
            () => Data.FileActions.IsFileOpStopEnabled,
            "Resume",
            "\uE768",
            FileActionLogic.ToggleFileOpQ,
            "Stop",
            "\uE71A"),
        new(FileActionType.FileOpPastView,
            () => Data.FileActions.IsExplorerVisible || Data.FileActions.IsDriveViewVisible,
            "Show Current Operations",
            Icons[FileActionType.OpenFileOps],
            FileActionLogic.TogglePastView,
            "Show Previous Operations",
            Icons[FileActionType.FileOpPastView],
            // Resources in nested dictionaries can't be found directly, only within that dictionary
            checkBackground: (SolidColorBrush)((ResourceDictionary)Application.Current.Resources["DynamicBrushes"])["HistoryDeviceBottomBorderBrush"]),
        new(FileActionType.PauseLogs,
            () => true,
            "Log Updates Are Paused",
            "\uE769",
            () => Data.RuntimeSettings.IsLogPaused ^= true,
            "Pause Log Updates"),
        new(FileActionType.SortSettings,
            () => true,
            "Exit Search",
            Icons[FileActionType.FileOpRemove],
            FileActionLogic.ToggleSettingsSort,
            "Search View",
            "\uE721"),
        new(FileActionType.ExpandSettings,
            () => true,
            "Collapse All",
            "\uE16A",
            FileActionLogic.ToggleSettingsExpand,
            "Expand All",
            "\uE169",
            isVisible: Data.FileActions.IsExpandSettingsVisible),
        new(FileActionType.LogToggle,
            () => true,
            "Command Log",
            "\uE756",
            () => Data.RuntimeSettings.IsLogOpen ^= true,
            isVisible: Data.FileActions.IsLogToggleVisible),
    };

    public static List<FileAction> List { get; } = new()
    {
        new(FileActionType.Home,
            () => Data.FileActions.HomeEnabled,
            () => Data.RuntimeSettings.LocationToNavigate = NavHistory.SpecialLocation.DriveView,
            "Device Drives",
            Gestures[FileActionType.Home]),
        new(FileActionType.KeyboardHome,
            () => Data.FileActions.HomeEnabled && !Data.FileActions.IsExplorerEditing,
            () => Data.RuntimeSettings.LocationToNavigate = NavHistory.SpecialLocation.DriveView,
            "Device Drives",
            Gestures[FileActionType.Home],
            true),
        new(FileActionType.Back,
            () => NavHistory.BackAvailable,
            () => Data.RuntimeSettings.LocationToNavigate = NavHistory.SpecialLocation.Back,
            "Back",
            new(Key.Back),
            true),
        new(FileActionType.Forward,
            () => NavHistory.ForwardAvailable,
            () => Data.RuntimeSettings.LocationToNavigate = NavHistory.SpecialLocation.Forward,
            "Forward",
            new(Key.Right, ModifierKeys.Alt),
            true),
        new(FileActionType.Up,
            () => Data.FileActions.ParentEnabled,
            () => Data.RuntimeSettings.LocationToNavigate = NavHistory.SpecialLocation.Up,
            "Up",
            new(Key.Up, ModifierKeys.Alt),
            true),
        new(FileActionType.Filter,
            () => Data.FileActions.HomeEnabled,
            () => Data.RuntimeSettings.IsSearchBoxFocused ^= true,
            "Filter",
            Gestures[FileActionType.Filter]),
        new(FileActionType.KeyboardFilter,
            () => Data.FileActions.HomeEnabled && !Data.FileActions.IsExplorerEditing,
            () => Data.RuntimeSettings.IsSearchBoxFocused ^= true,
            "Filter",
            Gestures[FileActionType.Filter],
            true),
        new(FileActionType.OpenDevices,
            () => Data.RuntimeSettings.IsDevicesViewEnabled,
            ToggleDevicesPane,
            "Devices",
            new(Key.D1, ModifierKeys.Alt),
            true),
        new(FileActionType.Pull,
            () => Data.FileActions.PullEnabled,
            () => FileActionLogic.PullFiles(),
            "Pull",
            new(Key.C, ModifierKeys.Alt),
            true),
        new(FileActionType.Push,
            () => Data.FileActions.PushEnabled,
            () => { },
            "Push"),
        new(FileActionType.ContextPush,
            () => Data.FileActions.ContextPushEnabled,
            () => { },
            "Push"),
        new(FileActionType.PushFolders,
            () => Data.FileActions.PushFilesFoldersEnabled,
            () => FileActionLogic.PushItems(true, false),
            "Folders"),
        new(FileActionType.PushFiles,
            () => Data.FileActions.PushFilesFoldersEnabled,
            () => FileActionLogic.PushItems(false, false),
            "Files",
            new(Key.V, ModifierKeys.Alt),
            true),
        new(FileActionType.Refresh,
            () => Data.FileActions.IsRefreshEnabled,
            () => Data.RuntimeSettings.Refresh = true,
            "Refresh",
            new(Key.F5)),
        new(FileActionType.CopyCurrentPath,
            () => Data.FileActions.IsCopyCurrentPathEnabled,
            () => Clipboard.SetText(Data.CurrentPath),
            "Copy",
            new(Key.F6),
            true),
        new(FileActionType.More,
            () => Data.FileActions.MoreEnabled,
            () => { },
            "More"),
        new(FileActionType.EditCurrentPath,
            () => Data.FileActions.IsCopyCurrentPathEnabled,
            () => Data.RuntimeSettings.IsPathBoxFocused = null,
            "Edit",
            new(Key.F6, ModifierKeys.Alt),
            true),
        new(FileActionType.ContextNew,
            () => Data.FileActions.ContextNewEnabled,
            () => { },
            "New"),
        new(FileActionType.New,
            () => Data.FileActions.NewEnabled,
            () => { },
            "New"),
        new(FileActionType.NewFolder,
            () => Data.FileActions.NewEnabled,
            () => Data.RuntimeSettings.NewFolder = true,
            "Folder"),
        new(FileActionType.NewFile,
            () => Data.FileActions.NewEnabled,
            () => Data.RuntimeSettings.NewFile = true,
            "File"),
        new(FileActionType.SelectAll,
            () => Data.FileActions.IsExplorerVisible && !Data.FileActions.IsExplorerEditing,
            () => Data.RuntimeSettings.SelectAll = true,
            "Select All",
            new(Key.A, ModifierKeys.Control),
            true),
        new(FileActionType.KeyboardCut,
            () => Data.FileActions.CutEnabled && !Data.FileActions.IsExplorerEditing,
            () => FileActionLogic.CutFiles(Data.SelectedFiles),
            "Cut",
            Gestures[FileActionType.Cut],
            true),
        new(FileActionType.Cut,
            () => Data.FileActions.CutEnabled,
            () => FileActionLogic.CutFiles(Data.SelectedFiles),
            "Cut",
            Gestures[FileActionType.Cut]),
        new(FileActionType.KeyboardCopy,
            () => Data.FileActions.CopyEnabled && !Data.FileActions.IsExplorerEditing,
            () => FileActionLogic.CutFiles(Data.SelectedFiles, true),
            "Copy",
            Gestures[FileActionType.Copy],
            true),
        new(FileActionType.Copy,
            () => Data.FileActions.CopyEnabled,
            () => FileActionLogic.CutFiles(Data.SelectedFiles, true),
            "Copy",
            Gestures[FileActionType.Copy]),
        new(FileActionType.KeyboardPaste,
            () => Data.FileActions.IsKeyboardPasteEnabled && !Data.FileActions.IsExplorerEditing,
            FileActionLogic.PasteFiles,
            Data.FileActions.PasteAction,
            Gestures[FileActionType.Paste],
            true),
        new(FileActionType.Paste,
            () => Data.FileActions.PasteEnabled,
            FileActionLogic.PasteFiles,
            Data.FileActions.PasteAction,
            Gestures[FileActionType.Paste]),
        new(FileActionType.Rename,
            () => Data.FileActions.RenameEnabled,
            () => Data.RuntimeSettings.Rename = true,
            "Rename",
            new(Key.F2),
            true),
        new(FileActionType.KeyboardRestore,
            () => Data.FileActions.RestoreEnabled && !Data.FileActions.IsExplorerEditing,
            FileActionLogic.RestoreItems,
            Data.FileActions.RestoreAction,
            Gestures[FileActionType.Restore],
            true),
        new(FileActionType.Restore,
            () => Data.FileActions.RestoreEnabled,
            FileActionLogic.RestoreItems,
            Data.FileActions.RestoreAction,
            Gestures[FileActionType.Restore]),
        new(FileActionType.KeyboardDelete,
            () => Data.FileActions.DeleteEnabled && !Data.FileActions.IsExplorerEditing,
            FileActionLogic.DeleteFiles,
            Data.FileActions.DeleteAction,
            Gestures[FileActionType.Delete],
            true),
        new(FileActionType.Delete,
            () => Data.FileActions.DeleteEnabled,
            FileActionLogic.DeleteFiles,
            Data.FileActions.DeleteAction,
            Gestures[FileActionType.Delete]),
        new(FileActionType.CopyItemPath,
            () => Data.FileActions.IsCopyItemPathEnabled,
            FileActionLogic.CopyItemPath,
            Data.FileActions.CopyPathAction,
            new(Key.C, ModifierKeys.Control | ModifierKeys.Shift),
            true),
        new(FileActionType.Package,
            () => Data.FileActions.PackageActionsEnabled,
            () => { },
            "Package"),
        new(FileActionType.UpdateModified,
            () => Data.FileActions.UpdateModifiedEnabled,
            FileActionLogic.UpdateModifiedDates,
            "Update Time Modified",
            new(Key.U, ModifierKeys.Control),
            true),
        new(FileActionType.Edit,
            () => Data.FileActions.EditFileEnabled,
            FileActionLogic.OpenEditor,
            "Open In Editor",
            Gestures[FileActionType.Edit],
            true),
        new(FileActionType.CloseEditor,
            () => true,
            FileActionLogic.OpenEditor,
            "Close Editor",
            Gestures[FileActionType.Edit],
            true),
        new(FileActionType.SaveEditor,
            () => Data.FileActions.IsEditorTextChanged,
            FileActionLogic.SaveEditorText,
            "Save Changes",
            new(Key.S, ModifierKeys.Control),
            true),
        new(FileActionType.Install,
            () => Data.FileActions.InstallPackageEnabled,
            FileActionLogic.InstallPackages,
            "Install",
            new(Key.F10, ModifierKeys.Shift),
            true),
        new(FileActionType.Uninstall,
            () => Data.FileActions.UninstallPackageEnabled,
            FileActionLogic.UninstallPackages,
            "Uninstall",
            Gestures[FileActionType.Uninstall],
            true),
        new(FileActionType.SubMenuUninstall,
            () => Data.FileActions.SubmenuUninstallEnabled,
            FileActionLogic.UninstallPackages,
            "Uninstall",
            Gestures[FileActionType.Uninstall]),
        new(FileActionType.CopyToTemp,
            () => Data.FileActions.CopyToTempEnabled,
            FileActionLogic.CopyToTemp,
            "Copy To Temp",
            new(Key.F12, ModifierKeys.Shift),
            true),
        new(FileActionType.PushPackages,
            () => Data.FileActions.PushPackageEnabled,
            FileActionLogic.PushPackages,
            Strings.S_PUSH_PKG,
            Gestures[FileActionType.PushPackages],
            true),
        new(FileActionType.ContextPushPackages,
            () => Data.FileActions.ContextPushPackagesEnabled,
            FileActionLogic.PushPackages,
            Strings.S_PUSH_PKG,
            Gestures[FileActionType.PushPackages]),
        new(FileActionType.EmptyTrash,
            () => Data.FileActions.EmptyTrash,
            () => { },
            Strings.S_TRASH_IS_EMPTY),
        new(FileActionType.None,
            new(),
            "",
            new(Key.F10),
            true),
        new(FileActionType.OpenSettings,
            () => true,
            ToggleSettingsPane,
            "Settings",
            Gestures[FileActionType.OpenSettings],
            true),
        new(FileActionType.HideSettings,
            () => true,
            ToggleSettingsPane,
            "Hide",
            Gestures[FileActionType.OpenSettings]),
        new(FileActionType.OpenFileOps,
            () => true,
            () => Data.RuntimeSettings.IsOperationsViewOpen ^= true,
            Strings.S_FILE_OP_TOOLTIP,
            new(Key.D9, ModifierKeys.Alt),
            true),
        ToggleActions.Find(a => a.FileAction.Name is FileActionType.FileOpStop).FileAction,
        new(FileActionType.FileOpRemove,
            () => Data.FileActions.IsFileOpRemoveCompletedEnabled || Data.FileActions.IsFileOpRemovePendingEnabled || Data.FileActions.IsFileOpRemovePastEnabled,
            () => { },
            "Remove Operations"),
        ToggleActions.Find(a => a.FileAction.Name is FileActionType.LogToggle).FileAction,

# pragma warning disable IDE0200
        // Passing non-static methods as delegates here causes a runtime exception

        new(FileActionType.FileOpRemovePending,
            () => Data.FileActions.IsFileOpRemovePendingEnabled,
            () => Data.FileOpQ.ClearPending(),
            "Pending"),
        new(FileActionType.FileOpRemoveCompleted,
            () => Data.FileActions.IsFileOpRemoveCompletedEnabled,
            () => Data.FileOpQ.ClearCompleted(),
            "Completed"),
        new(FileActionType.FileOpRemoveAll,
            () => Data.FileActions.IsFileOpRemoveCompletedEnabled || Data.FileActions.IsFileOpRemovePendingEnabled,
            () => Data.FileOpQ.Clear(),
            "All"),
        new(FileActionType.FileOpRemovePast,
            () => Data.FileActions.IsFileOpRemovePastEnabled,
            () => Data.FileOpQ.ClearPast(),
            "Previous"),

#pragma warning restore IDE0200

        new(FileActionType.FileOpDefaultFolder,
            () => !string.IsNullOrEmpty(Data.Settings.DefaultFolder),
            () => Process.Start("explorer.exe", Data.Settings.DefaultFolder),
            "Open Default Folder"),
        new(FileActionType.FileOpTestNext,
            () => true,
            FileOpTest.TestCurrentOperation,
            "Next Test"),
        ToggleActions.Find(a => a.FileAction.Name is FileActionType.FileOpPastView).FileAction,
        ToggleActions.Find(a => a.FileAction.Name is FileActionType.PauseLogs).FileAction,
        new(FileActionType.ClearLogs,
            () => Data.CommandLog.Count > 0,
            () => Data.RuntimeSettings.ClearLogs = true,
            "Clear Command Log"),
        new(FileActionType.ResetSettings,
            () => true,
            FileActionLogic.ResetAppSettings,
            "Reset App Settings"),
        ToggleActions.Find(a => a.FileAction.Name is FileActionType.SortSettings).FileAction,
        ToggleActions.Find(a => a.FileAction.Name is FileActionType.ExpandSettings).FileAction,
        new(FileActionType.FileOpValidate,
            () => Data.FileActions.SelectedFileOps.Value.Any(op => op.ValidationAllowed),
            Security.ValidateOps,
            "Validate Operation(s)"),
    };

    public static List<KeyBinding> Bindings =>
        List.Where(a => a.UseForGesture)
            .Select(action => action.KeyBinding)
            .Where(binding => binding is not null)
            .ToList();

    private static void ToggleSettingsPane()
    {
        Data.RuntimeSettings.IsSettingsPaneOpen ^= true;

        if (Data.RuntimeSettings.IsSettingsPaneOpen)
            Data.RuntimeSettings.IsDevicesPaneOpen = false;
    }

    private static void ToggleDevicesPane()
    {
        Data.RuntimeSettings.IsDevicesPaneOpen ^= true;

        if (Data.RuntimeSettings.IsDevicesPaneOpen)
            Data.RuntimeSettings.IsSettingsPaneOpen = false;
    }
}

internal class FileAction : ViewModelBase
{
    internal enum FileActionType
    {
        None,
        Home,
        KeyboardHome,
        Back,
        Forward,
        Up,
        Refresh,
        CopyCurrentPath,
        EditCurrentPath,
        Filter,
        KeyboardFilter,
        OpenDevices,
        Pull,
        Push,
        ContextPush,
        PushFolders,
        PushFiles,
        ContextNew,
        New,
        NewFolder,
        NewFile,
        SelectAll,
        KeyboardCut,
        Cut,
        KeyboardCopy,
        Copy,
        KeyboardPaste,
        Paste,
        Rename,
        KeyboardRestore,
        Restore,
        KeyboardDelete,
        Delete,
        CopyItemPath,
        More,
        UpdateModified,
        Edit,
        Package,
        Install,
        Uninstall,
        SubMenuUninstall,
        CopyToTemp,
        PushPackages,
        ContextPushPackages,
        EmptyTrash,
        OpenSettings,
        HideSettings,
        OpenFileOps,
        CloseEditor,
        SaveEditor,
        FileOpStop,
        FileOpRemove,
        FileOpRemovePending,
        FileOpRemoveCompleted,
        FileOpRemovePast,
        FileOpRemoveAll,
        FileOpDefaultFolder,
        FileOpTestNext,
        FileOpPastView,
        PauseLogs,
        ClearLogs,
        ResetSettings,
        SortSettings,
        ExpandSettings,
        LogToggle,
        FileOpValidate,
    }

    public FileActionType Name { get; }

    public BaseAction Command { get; }

    public KeyGesture Gesture { get; }

    public KeyBinding KeyBinding { get; }

    public string Description { get; private set; }

    public bool UseForGesture { get; }

    public string GestureString
    {
        get
        {
            if (Gesture is null)
                return null;

            string result = "";
            if (Gesture.Modifiers is not ModifierKeys.None)
            {
                result = Gesture.Modifiers.ToString();

                result = result.Replace("Control", "Ctrl");
                result = result.Replace(",", "+");
                result = result.Replace(" ", "");

                result += "+";
            }

            string key = Gesture.Key.ToString();
            if (key.Length > 1 && key[0] == 'D' && char.IsDigit(key[1]))
                key = key[1..];

            result += key;

            result = result.Replace("Delete", "Del");

            return result;
        }
    }

    public FileAction(FileActionType name, BaseAction command, string description, KeyGesture gesture, bool useForGesture = false)
    {
        Name = name;
        Command = command;
        Gesture = gesture;
        Description = description;

        if (gesture is not null)
            KeyBinding = new(Command.Command, gesture);

        UseForGesture = useForGesture;
    }

    public FileAction(FileActionType name, Func<bool> canExecute, Action action, string description = "", KeyGesture gesture = null, bool useForGesture = false)
        : this(name, new(canExecute, action), description, gesture, useForGesture)
    { }

    public FileAction(FileActionType name, Func<bool> canExecute, Action action, ObservableProperty<string> description, KeyGesture gesture = null, bool useForGesture = false)
        : this(name, new(canExecute, action), description.Value, gesture, useForGesture)
    {
        description.PropertyChanged += (object sender, PropertyChangedEventArgs<string> e) => Description = e.NewValue;
    }

    public override string ToString()
    {
        return Name.ToString();
    }
}
