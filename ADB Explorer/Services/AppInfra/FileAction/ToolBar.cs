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
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.NavRefresh),
            Data.FileActions.NavRefreshIcon,
            StyleHelper.ContentAnimation.RotateCW,
            mirrorInRTL: true),
        ];

}

file static class CompressToMenuHelper
{
    public static SubMenu[] CompressToFormatMenus() =>
    [
        new(AppActions.List.Find(a => a.Name is FileAction.FileActionType.CompressToTar)),
        new(AppActions.List.Find(a => a.Name is FileAction.FileActionType.CompressToTarGz)),
        new(AppActions.List.Find(a => a.Name is FileAction.FileActionType.CompressToTarBz2)),
        new(AppActions.List.Find(a => a.Name is FileAction.FileActionType.CompressToTarXz)),
        new(AppActions.List.Find(a => a.Name is FileAction.FileActionType.CompressToTarZst)),
    ];
}

internal static class MainToolBar
{
    public static ObservableList<IMenuItem> List { get; } = [
        new CompoundIconMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.Pull),
            new(new PullIcon()),
            isVisible: Data.FileActions.IsPullCopyVisible),
        new CompoundIconMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.Push),
            new(new PushIcon()),
            isChevronVisible: true,
            isVisible: Data.FileActions.IsPushMenuVisible,
            children: 
            [
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.PushFolders), AppActions.Icon(FileAction.FileActionType.PushFolders, 16)),
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.PushFiles), AppActions.Icon(FileAction.FileActionType.NewFile, 16)),
                new SubMenuSeparator(Data.FileActions.IsApkActionsVisible),
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.PushPackages),
                    AppActions.Icon(FileAction.FileActionType.Install, 16),
                    isVisible: Data.FileActions.IsApkActionsVisible),
            ]),
        new MenuSeparator(),
        new CompoundIconMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.New),
            new BaseIcon(new AddCircleIcon()),
            isNameDisplayed: true,
            isChevronVisible: true,
            children:
            [
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.NewFolder), AppActions.Icon(FileAction.FileActionType.PushFolders, 16)),
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.NewFile), AppActions.Icon(FileAction.FileActionType.NewFile, 16)),
                new SubMenuSeparator(),
                new (
                    AppActions.List.Find(a => a.Name is FileAction.FileActionType.NewCompressTo),
                    AppActions.Icon(FileAction.FileActionType.NewCompressTo, 16),
                    children: CompressToMenuHelper.CompressToFormatMenus()),
            ],
            isVisible: Data.FileActions.IsNewMenuVisible),
        new IconMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.Cut),
            AppActions.Icon(FileAction.FileActionType.Cut, 18),
            StyleHelper.ContentAnimation.UpMarquee,
            Data.FileActions.IsCutState,
            altAction: AppActions.List.Find(a => a.Name is FileAction.FileActionType.KeyboardCut),
            isVisible: Data.FileActions.IsCutPasteDeleteVisible),
        new IconMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.Copy),
            new BaseIcon(new CopyIcon()),
            StyleHelper.ContentAnimation.Bounce,
            Data.FileActions.IsCopyState,
            altAction: AppActions.List.Find(a => a.Name is FileAction.FileActionType.KeyboardCopy),
            isVisible: Data.FileActions.IsPullCopyVisible),
        new DynamicAltTextMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.Paste),
            Data.FileActions.CutItemsCount,
            new BaseIcon(new PasteIcon()),
            StyleHelper.ContentAnimation.Bounce,
            altAction: AppActions.List.Find(a => a.Name is FileAction.FileActionType.KeyboardPaste),
            isVisible: Data.FileActions.IsPasteVisible),
        new IconMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.Rename),
            new BaseIcon(new RenameAIcon()),
            StyleHelper.ContentAnimation.Bounce,
            isVisible: Data.FileActions.IsNewMenuVisible),
        new IconMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.Restore),
            AppActions.Icon(FileAction.FileActionType.Restore, 18),
            isVisible: Data.FileActions.IsRestoreMenuVisible),
        new IconMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.Delete),
            AppActions.Icon(FileAction.FileActionType.Delete, 18),
            isVisible: Data.FileActions.IsCutPasteDeleteVisible),
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
        new IconMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.SearchApkOnWeb),
            AppActions.Icon(FileAction.FileActionType.SearchApkOnWeb, 18),
            isVisible: Data.FileActions.IsUninstallVisible),
        new IconMenu(description: Strings.Resources.S_MENU_MORE,
            icon: AppActions.Icon(FileAction.FileActionType.More, 20),
            children:
            [
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.CopyItemPath), AppActions.Icon(FileAction.FileActionType.CopyItemPath, 16)),
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.MoreCompress),
                    AppActions.Icon(FileAction.FileActionType.NewCompressTo, 16),
                    children:
                    [
                        new SubMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.CopyContents), new BaseIcon(new CopyArrowRightIcon(), 16)),
                        new SubMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.ExtractHere), new BaseIcon(new FolderArrowRightIcon(), 16)),
                        new SubMenu(
                            AppActions.List.Find(a => a.Name is FileAction.FileActionType.CompressTo),
                            AppActions.Icon(FileAction.FileActionType.CompressTo, 16),
                            children: CompressToMenuHelper.CompressToFormatMenus()),
                    ]),
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.Package),
                    new(FluentPathGeometries.Box, 16),
                    isVisible: Data.FileActions.IsApkActionsVisible,
                    children:
                    [
                        new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.Install), AppActions.Icon(FileAction.FileActionType.Install, 16)),
                        new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.SubMenuUninstall), AppActions.Icon(FileAction.FileActionType.Uninstall, 16)),
                    ]),
            ]),
    ];
}

internal static class ExplorerContextMenu
{
    public static bool IsVisibleInContextMenu(SubMenu menu)
    {
        if (menu is SubMenuSeparator or DummySubMenu)
            return false;

        if (menu.Children is null)
            return menu.Action.Command.IsEnabled;

        return menu.Action.Command.IsEnabled && menu.Children.Any(child => child.Action.Command.IsEnabled);
    }

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

                sep.separator.IsEnabled = list[startIndexBefore..endIndexBefore].Any(IsVisibleInContextMenu)
                    && list[startIndexAfter..].Any(IsVisibleInContextMenu);
            }

            List.OfType<DummySubMenu>().First().IsEnabled = !List.OfType<SubMenu>().Any(IsVisibleInContextMenu);

            RefreshVisibleList(list);
        });
    }

    /// <summary>
    /// Menu items whose command becomes disabled must be structurally removed from the bound
    /// ItemsSource (rather than merely collapsed) so no blank rows are left behind for slots
    /// that were previously visible but no longer apply to the current selection/location.
    /// </summary>
    private static void RefreshVisibleList(SubMenu[] list)
    {
        var visible = new List<SubMenu>(list.Length);

        foreach (var item in list)
        {
            var show = item switch
            {
                SubMenuSeparator sep => !sep.HideSeparator,
                DummySubMenu dummy => dummy.IsEnabled is true,
                _ => IsVisibleInContextMenu(item),
            };

            if (show)
                visible.Add(item);
        }

        if (!visible.SequenceEqual(VisibleList))
        {
            VisibleList.RemoveAll();
            VisibleList.AddRange(visible);
        }
    }

    /// <summary>
    /// The actual context menu ItemsSource. Only currently-applicable items are present here -
    /// see <see cref="RefreshVisibleList"/>.
    /// </summary>
    public static ObservableList<SubMenu> VisibleList { get; } = [];

    public static ObservableList<SubMenu> List { get; } = [
        new SubMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.Enter),
            new("\uE838", 16)),
        new SubMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.Pull),
            new(new PullIcon(), 16)),
        new SubMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.ContextPush),
            new(new PushIcon(), 16),
            children:
            [
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.PushFolders), AppActions.Icon(FileAction.FileActionType.PushFolders, 16)),
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.PushFiles), AppActions.Icon(FileAction.FileActionType.NewFile, 16)),
            ]),
        new SubMenuSeparator(),
        new SubMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.ContextNew),
            new BaseIcon(new AddCircleIcon(), 16),
            children:
            [
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.NewFolder), AppActions.Icon(FileAction.FileActionType.PushFolders, 16)),
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.NewFile), AppActions.Icon(FileAction.FileActionType.NewFile, 16)),
                new SubMenuSeparator(),
                new (
                    AppActions.List.Find(a => a.Name is FileAction.FileActionType.NewCompressTo),
                    AppActions.Icon(FileAction.FileActionType.NewCompressTo, 16),
                    children: CompressToMenuHelper.CompressToFormatMenus()),
            ]),
        new SubMenuSeparator(),
        new SubMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.Cut), AppActions.Icon(FileAction.FileActionType.Cut, 16)),
        new SubMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.Copy), new BaseIcon(new CopyIcon(), 16)),
        new SubMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.CopyLink), AppActions.Icon(FileAction.FileActionType.CopyLink, 16)),
        new SubMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.Paste), new BaseIcon(new PasteIcon(), 16)),
        new SubMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.PasteLink), AppActions.Icon(FileAction.FileActionType.PasteLink, 16)),
        new SubMenuSeparator(),
        new SubMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.CopyContents), new BaseIcon(new CopyArrowRightIcon(), 16)),
        new SubMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.ExtractHere), new BaseIcon(new FolderArrowRightIcon(), 16)),
        new SubMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.CompressTo),
            AppActions.Icon(FileAction.FileActionType.CompressTo, 16),
            children: CompressToMenuHelper.CompressToFormatMenus()),
        new SubMenuSeparator(),
        new SubMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.Rename), new BaseIcon(new RenameAIcon(), 16)),
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
            ]),
        new SubMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.SearchApkOnWeb), AppActions.Icon(FileAction.FileActionType.SearchApkOnWeb, 16)),
        new SubMenuSeparator(),
        new SubMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.ContextDelete), AppActions.Icon(FileAction.FileActionType.Delete, 16)),
        new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.ContextPushPackages), AppActions.Icon(FileAction.FileActionType.Install, 16)),
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
