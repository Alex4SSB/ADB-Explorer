﻿using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
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
        { FileActionType.PauseLogs, "\uE769" },
        { FileActionType.FollowLink, "\uE838" },
        { FileActionType.PasteLink, "\uE1A5" },
        { FileActionType.HideSettings, "\uE761" },
        { FileActionType.SearchApkOnWeb, "\uF6FA" },
    };

    public static List<ToggleMenu> ToggleActions { get; } =
    [
        new(FileActionType.FileOpFilter,
            () => !Data.FileOpQ.IsActive,
            "Filter File Operations",
            "\uF16C",
            () => { },
            toggleOnClick: false,
            children: FileOpFilters.List.Select(f => new GeneralSubMenu(f.CheckBox))),
        new(FileActionType.FileOpStop,
            () => true,
            "Enable Auto Play",
            "\uE768",
            FileActionLogic.ToggleFileOpQ,
            "Disable Auto Play\nCancel All Running",
            Icons[FileActionType.PauseLogs]),
        new(FileActionType.PauseLogs,
            () => true,
            "Log Updates Are Paused",
            Icons[FileActionType.PauseLogs],
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
    ];

    public static List<FileAction> List { get; } =
    [
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
            () => !Data.RuntimeSettings.IsSplashScreenVisible,
            ToggleDevicesPane,
            "Devices",
            new(Key.D1, ModifierKeys.Alt),
            true),
        new(FileActionType.Pull,
            () => Data.FileActions.PullEnabled,
            () => FileActionLogic.PullFiles(),
            Data.FileActions.PullDescription,
            new(Key.C, ModifierKeys.Alt),
            true,
            clearClipboard: true),
        new(FileActionType.Push,
            () => Data.FileActions.PushEnabled,
            () => { },
            "Push",
            clearClipboard: true),
        new(FileActionType.ContextPush,
            () => Data.FileActions.ContextPushEnabled,
            () => { },
            "Push",
            clearClipboard: true),
        new(FileActionType.PushFolders,
            () => Data.FileActions.PushFilesFoldersEnabled,
            () => FileActionLogic.PushItems(true, false),
            "Folders",
            clearClipboard: true),
        new(FileActionType.PushFiles,
            () => Data.FileActions.PushFilesFoldersEnabled,
            () => FileActionLogic.PushItems(false, false),
            "Files",
            new(Key.V, ModifierKeys.Alt),
            true,
            clearClipboard: true),
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
            "Folder",
            clearClipboard: true),
        new(FileActionType.NewFile,
            () => Data.FileActions.NewEnabled,
            () => Data.RuntimeSettings.NewFile = true,
            "File",
            clearClipboard: true),
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
            () => FileActionLogic.PasteFiles(Data.SelectedFiles),
            Data.FileActions.PasteDescription,
            Gestures[FileActionType.Paste],
            true),
        new(FileActionType.Paste,
            () => Data.FileActions.PasteEnabled,
            () => FileActionLogic.PasteFiles(Data.SelectedFiles),
            Data.FileActions.PasteDescription,
            Gestures[FileActionType.Paste]),
        new(FileActionType.PasteLink,
            () => Data.FileActions.IsPasteLinkEnabled,
            () => FileActionLogic.PasteFiles(Data.SelectedFiles, isLink: true),
            "Paste Link",
            new(Key.L, ModifierKeys.Control),
            true),
        new(FileActionType.Rename,
            () => Data.FileActions.RenameEnabled,
            () => Data.RuntimeSettings.Rename = true,
            "Rename",
            new(Key.F2),
            true,
            clearClipboard: true),
        new(FileActionType.KeyboardRestore,
            () => Data.FileActions.RestoreEnabled && !Data.FileActions.IsExplorerEditing,
            FileActionLogic.RestoreItems,
            Data.FileActions.RestoreDescription,
            Gestures[FileActionType.Restore],
            true,
            clearClipboard: true),
        new(FileActionType.Restore,
            () => Data.FileActions.RestoreEnabled,
            FileActionLogic.RestoreItems,
            Data.FileActions.RestoreDescription,
            Gestures[FileActionType.Restore],
            clearClipboard: true),
        new(FileActionType.KeyboardDelete,
            () => Data.FileActions.DeleteEnabled && !Data.FileActions.IsExplorerEditing,
            FileActionLogic.DeleteFiles,
            Data.FileActions.DeleteDescription,
            Gestures[FileActionType.Delete],
            true,
            clearClipboard: true),
        new(FileActionType.Delete,
            () => Data.FileActions.DeleteEnabled,
            FileActionLogic.DeleteFiles,
            Data.FileActions.DeleteDescription,
            Gestures[FileActionType.Delete],
            clearClipboard: true),
        new(FileActionType.CopyItemPath,
            () => Data.FileActions.IsCopyItemPathEnabled,
            FileActionLogic.CopyItemPath,
            Data.FileActions.CopyPathDescription,
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
            true,
            clearClipboard: true),
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
            true,
            clearClipboard: true),
        new(FileActionType.PushPackages,
            () => Data.FileActions.PushPackageEnabled,
            FileActionLogic.PushPackages,
            Strings.Resources.S_PUSH_PKG,
            Gestures[FileActionType.PushPackages],
            true),
        new(FileActionType.ContextPushPackages,
            () => Data.FileActions.ContextPushPackagesEnabled,
            FileActionLogic.PushPackages,
            Strings.Resources.S_PUSH_PKG,
            Gestures[FileActionType.PushPackages]),
        new(FileActionType.None,
            new(),
            "",
            new(Key.F10),
            true),
        new(FileActionType.OpenSettings,
            () => !Data.RuntimeSettings.IsSplashScreenVisible,
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
            () => !Data.RuntimeSettings.IsSplashScreenVisible,
            () => Data.RuntimeSettings.IsOperationsViewOpen ^= true,
            Strings.Resources.S_FILE_OP_TOOLTIP,
            new(Key.D9, ModifierKeys.Alt),
            true),
        ToggleActions.Find(a => a.FileAction.Name is FileActionType.FileOpStop).FileAction,
        new(FileActionType.FileOpRemove,
            () => !Data.FileOpQ.IsActive && Data.FileActions.SelectedFileOps.Value.Any(),
            () => Data.FileOpQ.Operations.RemoveAll(Data.FileActions.SelectedFileOps.Value),
            Data.FileActions.RemoveFileOpDescription),
        ToggleActions.Find(a => a.FileAction.Name is FileActionType.LogToggle).FileAction,
        new(FileActionType.FileOpTestNext,
            () => true,
            FileOpTest.TestCurrentOperation,
            "Next Test"),
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
            () => !Data.FileOpQ.IsActive && Data.FileActions.SelectedFileOps.Value.AnyAll(op => op.ValidationAllowed),
            Security.ValidateOps,
            Data.FileActions.ValidateDescription),
        new(FileActionType.FollowLink,
            () => Data.FileActions.IsFollowLinkEnabled,
            FileActionLogic.FollowLink,
            "Open Item Location",
            new(Key.Enter, ModifierKeys.Shift),
            true),
        new(FileActionType.SearchApkOnWeb,
            () => Data.FileActions.IsApkWebSearchEnabled,
            FileActionLogic.ApkWebSearch,
            "Search In Browser",
            new(Key.O, ModifierKeys.Control),
            true),
        new(FileActionType.CopyMessageToClipboard,
            () => !string.IsNullOrEmpty(Data.FileActions.MessageToCopy),
            () =>
            {
                Clipboard.SetText(Data.FileActions.MessageToCopy);
                Data.FileActions.MessageToCopy = "";
            },
            "Copy to clipboard")
    ];

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
        OpenSettings,
        HideSettings,
        OpenFileOps,
        CloseEditor,
        SaveEditor,
        FileOpStop,
        FileOpRemove,
        FileOpTestNext,
        PauseLogs,
        ClearLogs,
        ResetSettings,
        SortSettings,
        ExpandSettings,
        LogToggle,
        FileOpValidate,
        FileOpFilter,
        FollowLink,
        PasteLink,
        SearchApkOnWeb,
        CopyMessageToClipboard,
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
            result = result.Replace("Return", "Enter");

            return result;
        }
    }

    public FileAction(FileActionType name,
                      BaseAction command,
                      string description,
                      KeyGesture gesture = null,
                      bool useForGesture = false,
                      bool clearClipboard = false)
    {
        Name = name;
        Command = command;
        Gesture = gesture;
        Description = description;

        if (gesture is not null)
            KeyBinding = new(Command.Command, gesture);

        UseForGesture = useForGesture;

        ((CommandHandler)Command.Command).OnExecute.PropertyChanged += (object sender, PropertyChangedEventArgs<bool> e) =>
        {
            if (clearClipboard && Data.CopyPaste.IsSelf)
                Data.CopyPaste.Clear();
        };
    }

    public FileAction(FileActionType name,
                      Func<bool> canExecute,
                      Action action,
                      string description = "",
                      KeyGesture gesture = null,
                      bool useForGesture = false,
                      bool clearClipboard = false)
        : this(name, new(canExecute, action), description, gesture, useForGesture, clearClipboard)
    { }

    public FileAction(FileActionType name,
                      Func<bool> canExecute,
                      Action action,
                      ObservableProperty<string> description,
                      KeyGesture gesture = null,
                      bool useForGesture = false,
                      bool clearClipboard = false)
        : this(name, new(canExecute, action), description.Value, gesture, useForGesture, clearClipboard)
    {
        description.PropertyChanged += (object sender, PropertyChangedEventArgs<string> e) => Description = e.NewValue;
    }

    public override string ToString()
    {
        return Name.ToString();
    }
}
