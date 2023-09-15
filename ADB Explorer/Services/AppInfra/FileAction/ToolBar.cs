using ADB_Explorer.Controls;
using ADB_Explorer.Helpers;
using ADB_Explorer.Models;

namespace ADB_Explorer.Services;

internal static class NavigationToolBar
{
    public static ObservableList<IconMenu> List { get; } = new() {
        new IconMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.Home),
            "\uE80F",
            StyleHelper.ContentAnimation.Bounce,
            16,
            altAction: AppActions.List.Find(a => a.Name is FileAction.FileActionType.KeyboardHome)),
        new IconMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.Back),
            "\uE72B",
            StyleHelper.ContentAnimation.LeftMarquee),
        new IconMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.Forward),
            "\uE72A",
            StyleHelper.ContentAnimation.RightMarquee),
        new IconMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.Up),
            "\uE197",
            StyleHelper.ContentAnimation.Bounce),
        };

}

internal static class MainToolBar
{
    public static ObservableList<ActionMenu> List { get; } = new() {
        new AnimatedNotifyMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.OpenDevices),
            Data.DevicesObject.ObservableCount,
            "\uE8CC"),
        new MenuSeparator(),
        new CompoundIconMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.Pull),
            new PullIcon()),
         new CompoundIconMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.Push),
            new PushIcon(),
            new SubMenu[]
            {
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.PushFolders), AppActions.Icons[FileAction.FileActionType.PushFolders]),
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.PushFiles), AppActions.Icons[FileAction.FileActionType.NewFile]),
                new SubMenuSeparator(() => Data.FileActions.PushPackageEnabled),
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.PushPackages), AppActions.Icons[FileAction.FileActionType.Package]),
            }),
        new MenuSeparator(),
        new AltTextMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.New),
            AppActions.Icons[FileAction.FileActionType.New],
            iconSize: 20,
            isTooltipVisible: false,
            children: new SubMenu[]
            {
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.NewFolder), "\uE8F4"),
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.NewFile), AppActions.Icons[FileAction.FileActionType.NewFile]),
            },
            isVisible: Data.FileActions.IsNewMenuVisible),
        new IconMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.Cut),
            "\uE8C6",
            StyleHelper.ContentAnimation.UpMarquee,
            18,
            Data.FileActions.IsCutState,
            altAction: AppActions.List.Find(a => a.Name is FileAction.FileActionType.KeyboardCut)),
        new IconMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.Copy),
            "\uE8C8",
            StyleHelper.ContentAnimation.Bounce,
            18,
            Data.FileActions.IsCopyState,
            altAction: AppActions.List.Find(a => a.Name is FileAction.FileActionType.KeyboardCopy)),
        new DynamicAltTextMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.Paste),
            Data.FileActions.CutItemsCount,
            "\uE77F",
            StyleHelper.ContentAnimation.Bounce,
            iconSize: 18,
            altAction: AppActions.List.Find(a => a.Name is FileAction.FileActionType.KeyboardPaste)),
        new IconMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.Rename),
            "\uE8AC",
            StyleHelper.ContentAnimation.Bounce,
            18),
        new IconMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.Restore),
            "\uE845",
            iconSize: 18,
            isVisible: Data.FileActions.IsRestoreMenuVisible),
        new IconMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.Delete),
            "\uE74D",
            iconSize: 18),
        new IconMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.Uninstall),
            "\uE25B",
            StyleHelper.ContentAnimation.DownMarquee,
            18,
            isVisible: Data.FileActions.IsUninstallVisible),
        new IconMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.More),
            "\uE712",
            iconSize: 20,
            children: new SubMenu[]
            {
                new CompoundIconSubMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.CopyItemPath), new Controls.PathIcon()),
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.UpdateModified), "\uE787"),
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.Edit), "\uE70F"),
                new SubMenuSeparator(() => Data.FileActions.PackageActionsEnabled),
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.Package),
                    AppActions.Icons[FileAction.FileActionType.Package],
                    children: new SubMenu[]
                    {
                        new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.Install), "\uE896"),
                        new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.SubMenuUninstall), "\uE25B"),
                        new SubMenuSeparator(() => true),
                        new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.CopyToTemp), "\uF413"),
                    }),
            }),
    };
}

internal static class ExplorerContextMenu
{
    public static ObservableList<SubMenu> List { get; } = new() {
        new CompoundIconSubMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.Pull),
            new PullIcon(-5)),
        new CompoundIconSubMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.ContextPush),
            new PushIcon(-5),
            children: new SubMenu[]
            {
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.PushFolders), AppActions.Icons[FileAction.FileActionType.PushFolders]),
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.PushFiles), AppActions.Icons[FileAction.FileActionType.NewFile]),
            }),
        new SubMenuSeparator(() => Data.FileActions.PullEnabled || Data.FileActions.ContextPushEnabled),
        new SubMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.ContextNew),
            AppActions.Icons[FileAction.FileActionType.New],
            children: new SubMenu[]
            {
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.NewFolder), "\uE8F4"),
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.NewFile), AppActions.Icons[FileAction.FileActionType.NewFile]),
            }),
        new SubMenuSeparator(
            () => Data.FileActions.ContextNewEnabled
            && (Data.FileActions.CutEnabled
            || Data.FileActions.CopyEnabled
            || Data.FileActions.PasteEnabled
            || Data.FileActions.RenameEnabled
            || Data.FileActions.IsCopyItemPathEnabled
            || Data.FileActions.UpdateModifiedEnabled
            || Data.FileActions.EditFileEnabled
            || Data.FileActions.UninstallPackageEnabled
            || Data.FileActions.RestoreEnabled)),
        new SubMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.Cut), "\uE8C6"),
        new SubMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.Copy), "\uE8C8"),
        new SubMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.Paste), "\uE77F"),
        new SubMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.Rename), "\uE8AC"),
        new CompoundIconSubMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.CopyItemPath), new Controls.PathIcon()),
        new SubMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.More),
            "\uE712",
            children: new SubMenu[]
            {
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.UpdateModified), "\uE787"),
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.Edit), "\uE70F"),
            }),
        new SubMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.Uninstall), "\uE25B"),
        new SubMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.Restore), "\uE845"),
        new SubMenuSeparator(
            () => (Data.FileActions.CutEnabled
            || Data.FileActions.CopyEnabled
            || Data.FileActions.PasteEnabled
            || Data.FileActions.RenameEnabled
            || Data.FileActions.IsCopyItemPathEnabled
            || Data.FileActions.UpdateModifiedEnabled
            || Data.FileActions.EditFileEnabled
            || Data.FileActions.UninstallPackageEnabled
            || Data.FileActions.RestoreEnabled)
            && Data.FileActions.PackageActionsEnabled),
        new SubMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.Package),
            AppActions.Icons[FileAction.FileActionType.Package],
            children: new SubMenu[]
            {
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.Install), "\uE896"),
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.SubMenuUninstall), "\uE25B"),
                new SubMenuSeparator(() => true),
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.CopyToTemp), "\uF413"),
            }),
        new SubMenuSeparator(
            () => (Data.FileActions.CutEnabled
            || Data.FileActions.CopyEnabled
            || Data.FileActions.PasteEnabled
            || Data.FileActions.RenameEnabled
            || Data.FileActions.IsCopyItemPathEnabled
            || Data.FileActions.UpdateModifiedEnabled
            || Data.FileActions.EditFileEnabled
            || Data.FileActions.UninstallPackageEnabled
            || Data.FileActions.RestoreEnabled
            || Data.FileActions.PackageActionsEnabled)
            && (Data.FileActions.DeleteEnabled
            || Data.FileActions.ContextPushPackagesEnabled)),
        new SubMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.Delete), "\uE74D"),
        new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.ContextPushPackages), AppActions.Icons[FileAction.FileActionType.Package]),
        new SubMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.EmptyTrash), "\uF141"),
    };
}

internal static class PathContextMenu
{
    public static ObservableList<SubMenu> List { get; } = new()
    {
        new SubMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.EditCurrentPath), "\uE70F"),
        new SubMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.CopyCurrentPath), "\uE8C8"),
        new SubMenuSeparator(() => true),
        new SubMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.Refresh), "\uE72C"),
    };
}

internal static class SettingsMenu
{
    public static ObservableList<ActionMenu> List { get; } = new()
    {
        new AltObjectMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.OpenFileOps), AppActions.Icons[FileAction.FileActionType.OpenFileOps]),
        new IconMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.OpenSettings),
            "\uE713",
            iconSize: 18),
    };
}

internal static class SettingsPaneMenu
{
    public static ObservableList<ActionMenu> List { get; } = new()
    {
        new IconMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.HideSettings),
            "\uE761",
            iconSize: 20),
    };
}

internal static class EditorControls
{
    public static ObservableList<ActionBase> List { get; } = new()
    {
        new DualActionButton(AppActions.List.Find(a => a.Name is FileAction.FileActionType.CloseEditor),
            "\uE711",
            iconSize: 16),
        new DualActionButton(AppActions.List.Find(a => a.Name is FileAction.FileActionType.SaveEditor),
            "\uE74E",
            animation: StyleHelper.ContentAnimation.Bounce,
            iconSize: 16),
    };
}

internal static class FileOpControls
{
    public static ObservableList<ActionBase> List { get; } = new()
    {
        AppActions.ToggleActions.Find(a => a.FileAction.Name is FileAction.FileActionType.FileOpPastView).Button,
        AppActions.ToggleActions.Find(a => a.FileAction.Name is FileAction.FileActionType.FileOpStop).Button,
        new IconMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.FileOpRemove),
            "\uE711",
            iconSize: 20,
            children: new SubMenu[]
            {
                new(AppActions.List.Find(a => a.Name is FileAction.FileActionType.FileOpRemovePending),
                    "\uECC5"),
                new(AppActions.List.Find(a => a.Name is FileAction.FileActionType.FileOpRemoveCompleted),
                    "\uE930"),
                new(AppActions.List.Find(a => a.Name is FileAction.FileActionType.FileOpRemoveAll),
                    "\uECC9"),
                new SubMenuSeparator(() => true),
                new(AppActions.List.Find(a => a.Name is FileAction.FileActionType.FileOpRemovePast),
                    AppActions.Icons[FileAction.FileActionType.FileOpPastView]),
            }),
        new IconMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.FileOpDefaultFolder),
            "\uED25",
            iconSize: 20),
#if DEBUG
        new IconMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.FileOpTestNext),
            "\uE14A",
            iconSize: 20),
#endif
    };
}

internal static class LogControls
{
    public static ObservableList<ActionBase> List { get; } = new()
    {
        AppActions.ToggleActions.Find(a => a.FileAction.Name is FileAction.FileActionType.PauseLogs).Button,
        new IconMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.ClearLogs),
            "\uE894",
            iconSize: 20)
    };
}

internal static class ResetSettings
{
    public static ObservableList<ActionBase> List { get; } = new()
    {
        new CompoundDualAction(AppActions.List.Find(a => a.Name is FileAction.FileActionType.ResetSettings),
            new ResetSettingsIcon()),
    };
}
