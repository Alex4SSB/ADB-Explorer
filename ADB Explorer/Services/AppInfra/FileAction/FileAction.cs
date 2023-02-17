using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Resources;
using ADB_Explorer.Services.AppInfra;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Services;

internal static class AppActions
{
    public static List<FileAction> List { get; } = new()
    {
        new(FileAction.FileActionType.Home,
            () => Data.FileActions.HomeEnabled,
            () => Data.RuntimeSettings.LocationToNavigate = NavHistory.SpecialLocation.DriveView,
            "Device Drives",
            new(Key.H, ModifierKeys.Alt)),
        new(FileAction.FileActionType.Back,
            () => NavHistory.BackAvailable,
            () => Data.RuntimeSettings.LocationToNavigate = NavHistory.SpecialLocation.Back,
            "Back",
            new(Key.Back)),
        new(FileAction.FileActionType.Forward,
            () => NavHistory.ForwardAvailable,
            () => Data.RuntimeSettings.LocationToNavigate = NavHistory.SpecialLocation.Forward,
            "Forward"),
        new(FileAction.FileActionType.Up,
            () => Data.FileActions.ParentEnabled,
            () => Data.RuntimeSettings.LocationToNavigate = NavHistory.SpecialLocation.Up,
            "Up"),
        new(FileAction.FileActionType.Filter,
            () => Data.FileActions.HomeEnabled,
            () => Data.RuntimeSettings.IsSearchBoxFocused ^= true,
            "Filter",
            new(Key.F, ModifierKeys.Control)),
        new(FileAction.FileActionType.OpenDevices,
            () => Data.RuntimeSettings.IsDevicesViewEnabled,
            ToggleDevicesPane,
            "Devices",
            new(Key.D1, ModifierKeys.Alt)),
        new(FileAction.FileActionType.Pull,
            () => Data.FileActions.PullEnabled,
            () => FileActionLogic.PullFiles(),
            "Pull",
            new(Key.C, ModifierKeys.Alt)),
        new(FileAction.FileActionType.Push,
            () => Data.FileActions.PushEnabled,
            () => { },
            "Push"),
        new(FileAction.FileActionType.ContextPush,
            () => Data.FileActions.ContextPushEnabled,
            () => { },
            "Push"),
        new(FileAction.FileActionType.PushFolders,
            () => Data.FileActions.PushFilesFoldersEnabled,
            () => FileActionLogic.PushItems(true, false),
            "Folders"),
        new(FileAction.FileActionType.PushFiles,
            () => Data.FileActions.PushFilesFoldersEnabled,
            () => FileActionLogic.PushItems(false, false),
            "Files",
            new(Key.V, ModifierKeys.Alt)),
        new(FileAction.FileActionType.Refresh,
            () => Data.FileActions.IsRefreshEnabled,
            () => Data.RuntimeSettings.Refresh = true,
            "Refresh",
            new(Key.F5)),
        new(FileAction.FileActionType.CopyCurrentPath,
            () => Data.FileActions.IsCopyCurrentPathEnabled,
            () => Clipboard.SetText(Data.CurrentPath),
            "Copy",
            new(Key.F6)),
        new(FileAction.FileActionType.More,
            () => Data.FileActions.MoreEnabled,
            () => { },
            "More"),
        new(FileAction.FileActionType.EditCurrentPath,
            () => Data.FileActions.IsCopyCurrentPathEnabled,
            () => Data.RuntimeSettings.IsPathBoxFocused = null,
            "Edit",
            new(Key.F6, ModifierKeys.Alt)),
        new(FileAction.FileActionType.New,
            () => Data.FileActions.NewEnabled,
            () => { },
            "New"),
        new(FileAction.FileActionType.NewFolder,
            () => Data.FileActions.NewEnabled,
            () => Data.RuntimeSettings.NewFolder = true,
            "Folder"),
        new(FileAction.FileActionType.NewFile,
            () => Data.FileActions.NewEnabled,
            () => Data.RuntimeSettings.NewFile = true,
            "File"),
        new(FileAction.FileActionType.SelectAll,
            () => Data.FileActions.IsExplorerVisible,
            () => Data.RuntimeSettings.SelectAll = true,
            "Select All",
            new(Key.A, ModifierKeys.Control)),
        new(FileAction.FileActionType.Cut,
            () => Data.FileActions.CutEnabled,
            () => FileActionLogic.CutFiles(Data.SelectedFiles),
            "Cut",
            new(Key.X, ModifierKeys.Control)),
        new(FileAction.FileActionType.Copy,
            () => Data.FileActions.CopyEnabled,
            () => FileActionLogic.CutFiles(Data.SelectedFiles, true),
            "Copy",
            new(Key.C, ModifierKeys.Control)),
        new(FileAction.FileActionType.KeyboardPaste,
            () => Data.FileActions.IsKeyboardPasteEnabled,
            () => FileActionLogic.PasteFiles(),
            Data.FileActions.PasteAction,
            new(Key.V, ModifierKeys.Control)),
        new(FileAction.FileActionType.Paste,
            () => Data.FileActions.PasteEnabled,
            () => FileActionLogic.PasteFiles(),
            Data.FileActions.PasteAction,
            new(Key.V, ModifierKeys.Control)),
        new(FileAction.FileActionType.Rename,
            () => Data.FileActions.RenameEnabled,
            () => Data.RuntimeSettings.Rename = true,
            "Rename",
            new(Key.F2)),
        new(FileAction.FileActionType.Restore,
            () => Data.FileActions.RestoreEnabled,
            () => FileActionLogic.RestoreItems(),
            Data.FileActions.RestoreAction,
            new(Key.R, ModifierKeys.Control)),
        new(FileAction.FileActionType.Delete,
            () => Data.FileActions.DeleteEnabled,
            () => FileActionLogic.DeleteFiles(),
            Data.FileActions.DeleteAction,
            new(Key.Delete)),
        new(FileAction.FileActionType.CopyItemPath,
            () => Data.FileActions.IsCopyItemPathEnabled,
            () => FileActionLogic.CopyItemPath(),
            Data.FileActions.CopyPathAction,
            new(Key.C, ModifierKeys.Control | ModifierKeys.Shift)),
        new(FileAction.FileActionType.Package,
            () => Data.FileActions.PackageActionsEnabled,
            () => { },
            "Package"),
        new(FileAction.FileActionType.UpdateModified,
            () => Data.FileActions.UpdateModifiedEnabled,
            () => FileActionLogic.UpdateModifiedDates(),
            "Update Time Modified",
            new(Key.U, ModifierKeys.Control)),
        new(FileAction.FileActionType.Edit,
            () => Data.FileActions.EditFileEnabled,
            () => FileActionLogic.OpenEditor(),
            "Open In Editor",
            new(Key.E, ModifierKeys.Control)),
        new(FileAction.FileActionType.Install,
            () => Data.FileActions.InstallPackageEnabled,
            () => FileActionLogic.InstallPackages(),
            "Install",
            new(Key.F10, ModifierKeys.Shift)),
        new(FileAction.FileActionType.Uninstall,
            () => Data.FileActions.UninstallPackageEnabled,
            () => FileActionLogic.UninstallPackages(),
            "Uninstall",
            new(Key.F11, ModifierKeys.Shift)),
        new(FileAction.FileActionType.SubMenuUninstall,
            () => Data.FileActions.SubmenuUninstallEnabled,
            () => FileActionLogic.UninstallPackages(),
            "Uninstall",
            new(Key.F11, ModifierKeys.Shift)),
        new(FileAction.FileActionType.CopyToTemp,
            () => Data.FileActions.CopyToTempEnabled,
            () => FileActionLogic.CopyToTemp(),
            "Copy To Temp",
            new(Key.F12, ModifierKeys.Shift)),
        new(FileAction.FileActionType.PushPackages,
            () => Data.FileActions.PushPackageEnabled,
            () => FileActionLogic.PushPackages(),
            Strings.S_PUSH_PKG,
            new(Key.I, ModifierKeys.Alt)),
        new(FileAction.FileActionType.ContextPushPackages,
            () => Data.FileActions.ContextPushPackagesEnabled,
            () => FileActionLogic.PushPackages(),
            Strings.S_PUSH_PKG,
            new(Key.I, ModifierKeys.Alt)),
        new(FileAction.FileActionType.EmptyTrash,
            () => Data.FileActions.EmptyTrash,
            () => { },
            Strings.S_TRASH_IS_EMPTY),
        new(FileAction.FileActionType.None,
            new(),
            "",
            new(Key.F10)),
        new(FileAction.FileActionType.OpenSettings,
            () => true,
            ToggleSettingsPane,
            "Settings",
            new(Key.D0, ModifierKeys.Alt)),
        new(FileAction.FileActionType.HideSettings,
            () => true,
            ToggleSettingsPane,
            "Hide",
            new(Key.D0, ModifierKeys.Alt)),
        new(FileAction.FileActionType.OpenFileOps,
            () => true,
            () => Data.RuntimeSettings.IsOperationsViewOpen ^= true,
            Strings.S_FILE_OP_TOOLTIP,
            new(Key.D9, ModifierKeys.Alt)),
    };

    public static List<KeyBinding> Bindings =>
        List.Where(a => a.Name is not FileAction.FileActionType.Paste)
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
        Back,
        Forward,
        Up,
        Refresh,
        CopyCurrentPath,
        EditCurrentPath,
        Filter,
        OpenDevices,
        Pull,
        Push,
        ContextPush,
        PushFolders,
        PushFiles,
        New,
        NewFolder,
        NewFile,
        SelectAll,
        Cut,
        Copy,
        KeyboardPaste,
        Paste,
        Rename,
        Restore,
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
    }

    public FileActionType Name { get; }

    public BaseAction Command { get; }

    public KeyGesture Gesture { get; }

    public KeyBinding KeyBinding { get; }

    public string Description { get; private set; }

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
            if (key.Length > 1 && key[0] == 'D')
                key = key[1..];

            result += key;

            result = result.Replace("Delete", "Del");

            return result;
        }
    }

    public FileAction(FileActionType name, BaseAction command, string description, KeyGesture gesture)
    {
        Name = name;
        Command = command;
        Gesture = gesture;
        Description = description;

        if (gesture is not null)
            KeyBinding = new(Command.Command, gesture);
    }

    public FileAction(FileActionType name, Func<bool> canExecute, Action action, string description = "", KeyGesture gesture = null)
        : this(name, new(canExecute, action), description, gesture)
    { }

    public FileAction(FileActionType name, Func<bool> canExecute, Action action, ObservableProperty<string> description, KeyGesture gesture = null)
        : this(name, new(canExecute, action), description.Value, gesture)
    {
        description.PropertyChanged += (object sender, PropertyChangedEventArgs<string> e) => Description = e.NewValue;
    }

    public override string ToString()
    {
        return Name.ToString();
    }
}
