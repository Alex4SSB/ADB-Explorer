using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
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
            () => Data.RuntimeSettings.Filter = true,
            "Filter",
            new(Key.F, ModifierKeys.Control)),
        new(FileAction.FileActionType.OpenDevices,
            () => Data.RuntimeSettings.IsDevicesViewEnabled,
            () => Data.RuntimeSettings.IsDevicesPaneOpen = true,
            "Devices"),
        new(FileAction.FileActionType.Pull,
            () => Data.FileActions.PullEnabled,
            () => Data.RuntimeSettings.BeginPull = true,
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
            () => Data.RuntimeSettings.PushFolders = true,
            "Folders"),
        new(FileAction.FileActionType.PushFiles,
            () => Data.FileActions.PushFilesFoldersEnabled,
            () => Data.RuntimeSettings.PushFiles = true,
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
            () => Data.RuntimeSettings.EditCurrentPath = true,
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
            () => Data.RuntimeSettings.Cut = true,
            "Cut",
            new(Key.X, ModifierKeys.Control)),
        new(FileAction.FileActionType.Copy,
            () => Data.FileActions.CopyEnabled,
            () => Data.RuntimeSettings.Copy = true,
            "Copy",
            new(Key.C, ModifierKeys.Control)),
        new(FileAction.FileActionType.KeyboardPaste,
            () => Data.FileActions.IsKeyboardPasteEnabled,
            () => Data.RuntimeSettings.Paste = true,
            Data.FileActions.PasteAction,
            new(Key.V, ModifierKeys.Control)),
        new(FileAction.FileActionType.Paste,
            () => Data.FileActions.PasteEnabled,
            () => Data.RuntimeSettings.Paste = true,
            Data.FileActions.PasteAction,
            new(Key.V, ModifierKeys.Control)),
        new(FileAction.FileActionType.Rename,
            () => Data.FileActions.RenameEnabled,
            () => Data.RuntimeSettings.Rename = true,
            "Rename",
            new(Key.F2)),
        new(FileAction.FileActionType.Restore,
            () => Data.FileActions.RestoreEnabled,
            () => Data.RuntimeSettings.Restore = true,
            Data.FileActions.RestoreAction,
            new(Key.R, ModifierKeys.Control)),
        new(FileAction.FileActionType.Delete,
            () => Data.FileActions.DeleteEnabled,
            () => Data.RuntimeSettings.Delete = true,
            Data.FileActions.DeleteAction,
            new(Key.Delete)),
        new(FileAction.FileActionType.CopyItemPath,
            () => Data.FileActions.IsCopyItemPathEnabled,
            () => Data.RuntimeSettings.CopyItemPath = true,
            Data.FileActions.CopyPathAction,
            new(Key.C, ModifierKeys.Control | ModifierKeys.Shift)),
        new(FileAction.FileActionType.Package,
            () => Data.FileActions.PackageActionsEnabled,
            () => { },
            "Package"),
        new(FileAction.FileActionType.UpdateModified,
            () => Data.FileActions.UpdateModifiedEnabled,
            () => Data.RuntimeSettings.UpdateModifiedTime = true,
            "Update Time Modified",
            new(Key.U, ModifierKeys.Control)),
        new(FileAction.FileActionType.Edit,
            () => Data.FileActions.EditFileEnabled,
            () => Data.RuntimeSettings.EditItem = true,
            "Open In Editor",
            new(Key.E, ModifierKeys.Control)),
        new(FileAction.FileActionType.Install,
            () => Data.FileActions.InstallPackageEnabled,
            () => Data.RuntimeSettings.InstallPackage = true,
            "Install",
            new(Key.F10, ModifierKeys.Shift)),
        new(FileAction.FileActionType.Uninstall,
            () => Data.FileActions.UninstallPackageEnabled,
            () => Data.RuntimeSettings.Uninstall = true,
            "Uninstall",
            new(Key.F11, ModifierKeys.Shift)),
        new(FileAction.FileActionType.SubMenuUninstall,
            () => Data.FileActions.SubmenuUninstallEnabled,
            () => Data.RuntimeSettings.Uninstall = true,
            "Uninstall",
            new(Key.F11, ModifierKeys.Shift)),
        new(FileAction.FileActionType.CopyToTemp,
            () => Data.FileActions.CopyToTempEnabled,
            () => Data.RuntimeSettings.CopyToTemp = true,
            "Copy To Temp",
            new(Key.F12, ModifierKeys.Shift)),
        new(FileAction.FileActionType.PushPackages,
            () => Data.FileActions.PushPackageEnabled,
            () => Data.RuntimeSettings.PushPackages = true,
            "Install Packages",
            new(Key.I, ModifierKeys.Alt)),
        new(FileAction.FileActionType.ContextPushPackages,
            () => Data.FileActions.ContextPushPackagesEnabled,
            () => Data.RuntimeSettings.PushPackages = true,
            "Install Packages",
            new(Key.I, ModifierKeys.Alt)),
        new(FileAction.FileActionType.EmptyTrash,
            () => Data.FileActions.EmptyTrash,
            () => { },
            "Recycle Bin Is Empty"),
        new(FileAction.FileActionType.None,
            new(),
            "",
            new(Key.F10)),
    };

    public static List<KeyBinding> Bindings =>
        List.Where(a => a.Name is not FileAction.FileActionType.Paste)
            .Select(action => action.KeyBinding)
            .Where(binding => binding is not null)
            .ToList();
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

            result += Gesture.Key;

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
