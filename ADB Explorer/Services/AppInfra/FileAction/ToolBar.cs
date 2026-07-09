using ADB_Explorer.Controls;
using ADB_Explorer.Helpers;
using ADB_Explorer.Models;

namespace ADB_Explorer.Services;

internal static class NavigationToolBar
{
    public static ObservableList<IMenuItem> List { get; } = [
        new IconMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.Home),
            AppActions.Icon(FileAction.FileActionType.Home, 16),
            StyleHelper.ContentAnimation.Bounce,
            altAction: AppActions.List.Find(a => a.Name is FileAction.FileActionType.KeyboardHome)),
        new IconMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.Back),
            new BaseIcon("\uE72B", 16),
            StyleHelper.ContentAnimation.LeftMarquee,
            mirrorInRTL: true),
        new IconMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.NavHistory),
            new BaseIcon("\uE70D", 12),
            children:
            NavHistory.MenuHistory),
        new IconMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.Forward),
            new BaseIcon("\uE72A", 16),
            StyleHelper.ContentAnimation.RightMarquee,
            mirrorInRTL: true),
        new IconMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.Up),
            new BaseIcon("\uE197", 16),
            StyleHelper.ContentAnimation.Bounce),
        new IconMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.Refresh),
            AppActions.Icon(FileAction.FileActionType.Refresh, 16),
            StyleHelper.ContentAnimation.RotateCW,
            mirrorInRTL: true),
        ];

}

internal static class MainToolBar
{
    public static ObservableList<IMenuItem> List { get; } = [
        new CompoundIconMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.Pull),
            new(new PullIcon())),
        new CompoundIconMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.Push),
            new(new PushIcon()),
            isChevronVisible: true,
            children: 
            [
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.PushFolders), AppActions.Icon(FileAction.FileActionType.PushFolders, 16)),
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.PushFiles), AppActions.Icon(FileAction.FileActionType.NewFile, 16)),
                new SubMenuSeparator(Data.FileActions.IsApkActionsVisible),
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.PushPackages),
                    new(FluentPathGeometries.BoxArrowUp, 16),
                    isVisible: Data.FileActions.IsApkActionsVisible),
            ]),
        new MenuSeparator(),
        new CompoundIconMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.New),
            BaseIcon.NewItem(),
            isNameDisplayed: true,
            isChevronVisible: true,
            children:
            [
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.NewFolder), new BaseIcon("\uE8F4", 16)),
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.NewFile), AppActions.Icon(FileAction.FileActionType.NewFile, 16)),
            ],
            isVisible: Data.FileActions.IsNewMenuVisible),
        new IconMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.Cut),
            AppActions.Icon(FileAction.FileActionType.Cut, 18),
            StyleHelper.ContentAnimation.UpMarquee,
            Data.FileActions.IsCutState,
            altAction: AppActions.List.Find(a => a.Name is FileAction.FileActionType.KeyboardCut)),
        new IconMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.Copy),
            AppActions.Icon(FileAction.FileActionType.Copy, 18),
            StyleHelper.ContentAnimation.Bounce,
            Data.FileActions.IsCopyState,
            altAction: AppActions.List.Find(a => a.Name is FileAction.FileActionType.KeyboardCopy)),
        new DynamicAltTextMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.Paste),
            Data.FileActions.CutItemsCount,
            AppActions.Icon(FileAction.FileActionType.Paste, 18),
            StyleHelper.ContentAnimation.Bounce,
            altAction: AppActions.List.Find(a => a.Name is FileAction.FileActionType.KeyboardPaste)),
        new IconMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.Rename),
            AppActions.Icon(FileAction.FileActionType.Rename, 18),
            StyleHelper.ContentAnimation.Bounce,
            isVisible: Data.FileActions.IsNewMenuVisible),
        new IconMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.Restore),
            AppActions.Icon(FileAction.FileActionType.Restore, 18),
            isVisible: Data.FileActions.IsRestoreMenuVisible),
        new IconMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.Delete),
            AppActions.Icon(FileAction.FileActionType.Delete, 18)),
        new IconMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.OpenPackageLocation),
            AppActions.Icon(FileAction.FileActionType.FollowLink, 18),
            StyleHelper.ContentAnimation.RightMarquee,
            isVisible: Data.FileActions.IsUninstallVisible),
        new IconMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.Uninstall),
            AppActions.Icon(FileAction.FileActionType.Uninstall, 18),
            StyleHelper.ContentAnimation.DownMarquee,
            isVisible: Data.FileActions.IsUninstallVisible),
        new IconMenu(description: Strings.Resources.S_MENU_MORE,
            icon: AppActions.Icon(FileAction.FileActionType.More, 20),
            children:
            [
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.CopyItemPath), AppActions.Icon(FileAction.FileActionType.CopyItemPath, 16)),
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.SearchApkOnWeb),
                    AppActions.Icon(FileAction.FileActionType.SearchApkOnWeb, 16),
                    isVisible: Data.FileActions.IsApkActionsVisible),
                new SubMenuSeparator(),
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.FollowLink), AppActions.Icon(FileAction.FileActionType.FollowLink, 16)),
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.PasteLink), AppActions.Icon(FileAction.FileActionType.PasteLink, 16)),
                new SubMenuSeparator(),
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.UpdateModified), AppActions.Icon(FileAction.FileActionType.UpdateModified, 16)),
                new SubMenuSeparator(Data.FileActions.IsApkActionsVisible),
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.Package),
                    AppActions.Icon(FileAction.FileActionType.Package, 16),
                    isVisible: Data.FileActions.IsApkActionsVisible,
                    children:
                    [
                        new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.Install), AppActions.Icon(FileAction.FileActionType.Install, 16)),
                        new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.SubMenuUninstall), AppActions.Icon(FileAction.FileActionType.Uninstall, 16)),
                        new SubMenuSeparator(),
                        new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.CopyToTemp), AppActions.Icon(FileAction.FileActionType.CopyToTemp, 16)),
                    ]),
            ]),
    ];
}

internal static class ExplorerContextMenu
{
    public static void UpdateSeparators()
    {
        var list = List.ToArray();
        var separators = list.OfType<SubMenuSeparator>().Select(separator => (separator, List.IndexOf(separator))).ToList();

        App.SafeInvoke(() =>
        {
            for (int i = 0; i < separators.Count; i++)
            {
                var sep = separators[i];

                Index startIndexBefore = i < 1 ? 0 : separators[i - 1].Item2 + 1;
                Index endIndexBefore = separators[i].Item2;
                Index startIndexAfter = separators[i].Item2 + 1;

                sep.separator.IsEnabled = list[startIndexBefore..endIndexBefore].Any(a => a.Action.Command.IsEnabled)
                    && list[startIndexAfter..].Any(a => a is not SubMenuSeparator and not DummySubMenu && a.Action.Command.IsEnabled);
            }

            List.OfType<DummySubMenu>().First().IsEnabled = List.Where(a => a is not SubMenuSeparator and not DummySubMenu).All(a => !a.Action.Command.IsEnabled);
        });
    }

    private static FileClass enterFolder = new("/EnterFolder", "EnterFolder", AbstractFile.FileType.EnterFolder);

    public static ObservableList<SubMenu> List { get; } = [
        new SubMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.Enter),
            new(enterFolder.Icon, 16)),
        new SubMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.Pull),
            new(new PullIcon())),
        new SubMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.ContextPush),
            new(new PushIcon()),
            children:
            [
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.PushFolders), AppActions.Icon(FileAction.FileActionType.PushFolders, 16)),
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.PushFiles), AppActions.Icon(FileAction.FileActionType.NewFile, 16)),
            ]),
        new SubMenuSeparator(),
        new SubMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.ContextNew),
            BaseIcon.NewItem(),
            children:
            [
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.NewFolder), new BaseIcon("\uE8F4", 16)),
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.NewFile), AppActions.Icon(FileAction.FileActionType.NewFile, 16)),
            ]),
        new SubMenuSeparator(),
        new SubMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.Cut), AppActions.Icon(FileAction.FileActionType.Cut, 16)),
        new SubMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.Copy), AppActions.Icon(FileAction.FileActionType.Copy, 16)),
        new SubMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.Paste), AppActions.Icon(FileAction.FileActionType.Paste, 16)),
        new SubMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.PasteLink), AppActions.Icon(FileAction.FileActionType.PasteLink, 16)),
        new SubMenuSeparator(),
        new SubMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.Rename), AppActions.Icon(FileAction.FileActionType.Rename, 16)),
        new SubMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.FollowLink), AppActions.Icon(FileAction.FileActionType.FollowLink, 16)),
        new SubMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.OpenPackageLocation), AppActions.Icon(FileAction.FileActionType.FollowLink, 16)),
        new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.CopyItemPath), AppActions.Icon(FileAction.FileActionType.CopyItemPath, 16)),
        new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.UpdateModified), AppActions.Icon(FileAction.FileActionType.UpdateModified, 16)),
        new SubMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.Uninstall), AppActions.Icon(FileAction.FileActionType.Uninstall, 16)),
        new SubMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.Restore), AppActions.Icon(FileAction.FileActionType.Restore, 16)),
        new SubMenuSeparator(),
        new SubMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.Package),
            new(FluentPathGeometries.Box, 16),
            children:
            [
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.Install), AppActions.Icon(FileAction.FileActionType.Install, 16)),
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.SubMenuUninstall), AppActions.Icon(FileAction.FileActionType.Uninstall, 16)),
                new SubMenuSeparator(),
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.CopyToTemp), AppActions.Icon(FileAction.FileActionType.CopyToTemp, 16)),
            ]),
        new SubMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.SearchApkOnWeb), AppActions.Icon(FileAction.FileActionType.SearchApkOnWeb, 16)),
        new SubMenuSeparator(),
        new SubMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.ContextDelete), AppActions.Icon(FileAction.FileActionType.Delete, 16)),
        new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.ContextPushPackages), new(FluentPathGeometries.BoxArrowUp, 16)),
        new DummySubMenu(),
    ];
}

internal static class PathContextMenu
{
    public static ObservableList<SubMenu> List { get; } =
    [
        new SubMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.EditCurrentPath), AppActions.Icon(FileAction.FileActionType.EditCurrentPath, 16)),
        new SubMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.CopyCurrentPath), AppActions.Icon(FileAction.FileActionType.Copy, 16)),
        new SubMenuSeparator(),
        new SubMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.Refresh), AppActions.Icon(FileAction.FileActionType.Refresh, 16)),
    ];
}

internal static class LogControls
{
    public static ObservableList<IMenuItem> List { get; } =
    [
        AppActions.ToggleActions.Find(a => a.FileAction.Name is FileAction.FileActionType.PauseLogs).Button,
        new IconMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.ClearLogs),
            AppActions.Icon(FileAction.FileActionType.FileOpRemove, 20))
    ];
}
