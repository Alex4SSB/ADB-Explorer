using ADB_Explorer.Controls;
using ADB_Explorer.Helpers;
using ADB_Explorer.Models;

namespace ADB_Explorer.Services;

internal static class NavigationToolBar
{
    public static ObservableList<IMenuItem> List { get; } = [
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
        ];

}

internal static class MainToolBar
{
    public static ObservableList<IMenuItem> List { get; } = [
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
            [
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.PushFolders), AppActions.Icons[FileAction.FileActionType.PushFolders]),
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.PushFiles), AppActions.Icons[FileAction.FileActionType.NewFile]),
                new SubMenuSeparator(Data.FileActions.IsApkActionsVisible),
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.PushPackages),
                    AppActions.Icons[FileAction.FileActionType.Package],
                    isVisible: Data.FileActions.IsApkActionsVisible),
            ]),
        new MenuSeparator(),
        new AltTextMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.New),
            AppActions.Icons[FileAction.FileActionType.New],
            iconSize: 20,
            isTooltipVisible: false,
            children:
            [
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.NewFolder), "\uE8F4"),
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.NewFile), AppActions.Icons[FileAction.FileActionType.NewFile]),
            ],
            isVisible: Data.FileActions.IsNewMenuVisible),
        new IconMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.Cut),
            AppActions.Icons[FileAction.FileActionType.Cut],
            StyleHelper.ContentAnimation.UpMarquee,
            18,
            Data.FileActions.IsCutState,
            altAction: AppActions.List.Find(a => a.Name is FileAction.FileActionType.KeyboardCut)),
        new IconMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.Copy),
            AppActions.Icons[FileAction.FileActionType.Copy],
            StyleHelper.ContentAnimation.Bounce,
            18,
            Data.FileActions.IsCopyState,
            altAction: AppActions.List.Find(a => a.Name is FileAction.FileActionType.KeyboardCopy)),
        new DynamicAltTextMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.Paste),
            Data.FileActions.CutItemsCount,
            AppActions.Icons[FileAction.FileActionType.Paste],
            StyleHelper.ContentAnimation.Bounce,
            iconSize: 18,
            altAction: AppActions.List.Find(a => a.Name is FileAction.FileActionType.KeyboardPaste)),
        new IconMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.Rename),
            AppActions.Icons[FileAction.FileActionType.Rename],
            StyleHelper.ContentAnimation.Bounce,
            18),
        new IconMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.Restore),
            AppActions.Icons[FileAction.FileActionType.Restore],
            iconSize: 18,
            isVisible: Data.FileActions.IsRestoreMenuVisible),
        new IconMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.Delete),
            AppActions.Icons[FileAction.FileActionType.Delete],
            iconSize: 18),
        new IconMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.Uninstall),
            AppActions.Icons[FileAction.FileActionType.Uninstall],
            StyleHelper.ContentAnimation.DownMarquee,
            18,
            isVisible: Data.FileActions.IsUninstallVisible),
        new IconMenu(description: "More",
            icon: AppActions.Icons[FileAction.FileActionType.More],
            iconSize: 20,
            children:
            [
                new CompoundIconSubMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.CopyItemPath), new Controls.PathIcon()),
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.SearchApkOnWeb),
                    AppActions.Icons[FileAction.FileActionType.SearchApkOnWeb],
                    isVisible: Data.FileActions.IsApkActionsVisible),
                new SubMenuSeparator(),
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.FollowLink), AppActions.Icons[FileAction.FileActionType.FollowLink]),
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.PasteLink), AppActions.Icons[FileAction.FileActionType.PasteLink]),
                new SubMenuSeparator(),
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.UpdateModified), AppActions.Icons[FileAction.FileActionType.UpdateModified]),
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.Edit), AppActions.Icons[FileAction.FileActionType.Edit]),
                new SubMenuSeparator(Data.FileActions.IsApkActionsVisible),
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.Package),
                    AppActions.Icons[FileAction.FileActionType.Package],
                    isVisible: Data.FileActions.IsApkActionsVisible,
                    children:
                    [
                        new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.Install), AppActions.Icons[FileAction.FileActionType.Install]),
                        new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.SubMenuUninstall), AppActions.Icons[FileAction.FileActionType.Uninstall]),
                        new SubMenuSeparator(),
                        new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.CopyToTemp), AppActions.Icons[FileAction.FileActionType.CopyToTemp]),
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

        App.Current.Dispatcher.Invoke(() =>
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

    public static ObservableList<SubMenu> List { get; } = [
        new CompoundIconSubMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.Pull),
            new PullIcon(-5)),
        new CompoundIconSubMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.ContextPush),
            new PushIcon(-5),
            children:
            [
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.PushFolders), AppActions.Icons[FileAction.FileActionType.PushFolders]),
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.PushFiles), AppActions.Icons[FileAction.FileActionType.NewFile]),
            ]),
        new SubMenuSeparator(),
        new SubMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.ContextNew),
            AppActions.Icons[FileAction.FileActionType.New],
            children:
            [
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.NewFolder), "\uE8F4"),
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.NewFile), AppActions.Icons[FileAction.FileActionType.NewFile]),
            ]),
        new SubMenuSeparator(),
        new SubMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.Cut), AppActions.Icons[FileAction.FileActionType.Cut]),
        new SubMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.Copy), AppActions.Icons[FileAction.FileActionType.Copy]),
        new SubMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.Paste), AppActions.Icons[FileAction.FileActionType.Paste]),
        new SubMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.PasteLink), AppActions.Icons[FileAction.FileActionType.PasteLink]),
        new SubMenuSeparator(),
        new SubMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.Rename), AppActions.Icons[FileAction.FileActionType.Rename]),
        new SubMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.FollowLink), AppActions.Icons[FileAction.FileActionType.FollowLink]),
        new CompoundIconSubMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.CopyItemPath), new Controls.PathIcon()),
        new SubMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.More),
            AppActions.Icons[FileAction.FileActionType.More],
            children:
            [
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.UpdateModified), AppActions.Icons[FileAction.FileActionType.UpdateModified]),
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.Edit), AppActions.Icons[FileAction.FileActionType.Edit]),
            ]),
        new SubMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.Uninstall), AppActions.Icons[FileAction.FileActionType.Uninstall]),
        new SubMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.Restore), AppActions.Icons[FileAction.FileActionType.Restore]),
        new SubMenuSeparator(),
        new SubMenu(
            AppActions.List.Find(a => a.Name is FileAction.FileActionType.Package),
            AppActions.Icons[FileAction.FileActionType.Package],
            children:
            [
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.Install), AppActions.Icons[FileAction.FileActionType.Install]),
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.SubMenuUninstall), AppActions.Icons[FileAction.FileActionType.Uninstall]),
                new SubMenuSeparator(),
                new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.CopyToTemp), AppActions.Icons[FileAction.FileActionType.CopyToTemp]),
            ]),
        new SubMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.SearchApkOnWeb), AppActions.Icons[FileAction.FileActionType.SearchApkOnWeb]),
        new SubMenuSeparator(),
        new SubMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.Delete), AppActions.Icons[FileAction.FileActionType.Delete]),
        new (AppActions.List.Find(a => a.Name is FileAction.FileActionType.ContextPushPackages), AppActions.Icons[FileAction.FileActionType.Package]),
        new DummySubMenu(),
    ];
}

internal static class PathContextMenu
{
    public static ObservableList<SubMenu> List { get; } =
    [
        new SubMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.EditCurrentPath), AppActions.Icons[FileAction.FileActionType.Edit]),
        new SubMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.CopyCurrentPath), AppActions.Icons[FileAction.FileActionType.Copy]),
        new SubMenuSeparator(),
        new SubMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.Refresh), "\uE72C"),
    ];
}

internal static class FileOpMenu
{
    public static ObservableList<IMenuItem> List { get; } =
    [
        new AltObjectMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.OpenFileOps),
            AppActions.Icons[FileAction.FileActionType.OpenFileOps],
            isContentDropDown: true,
            children:
            [
                new GeneralSubMenu(App.Current.Resources["CompactFileOpDropDown"], true)
            ]),
    ];
}

internal static class SettingsMenu
{
    public static ObservableList<IMenuItem> List { get; } =
    [
        new IconMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.OpenSettings),
            "\uE713",
            iconSize: 18),
    ];
}

internal static class SettingsPaneMenu
{
    public static ObservableList<IMenuItem> List { get; } =
    [
        new IconMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.HideSettings),
            "\uE761",
            iconSize: 20),
    ];
}

internal static class EditorControls
{
    public static ObservableList<IMenuItem> List { get; } =
    [
        new DualActionButton(AppActions.List.Find(a => a.Name is FileAction.FileActionType.CloseEditor),
            AppActions.Icons[FileAction.FileActionType.FileOpRemove],
            iconSize: 16),
        new DualActionButton(AppActions.List.Find(a => a.Name is FileAction.FileActionType.SaveEditor),
            "\uE74E",
            animation: StyleHelper.ContentAnimation.Bounce,
            iconSize: 16),
    ];
}

internal static class FileOpControls
{
    public static ObservableList<IMenuItem> List { get; } =
    [
        AppActions.ToggleActions.Find(a => a.FileAction.Name is FileAction.FileActionType.FileOpFilter).Button,
        AppActions.ToggleActions.Find(a => a.FileAction.Name is FileAction.FileActionType.FileOpStop).Button,
        new IconMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.FileOpRemove),
            AppActions.Icons[FileAction.FileActionType.FileOpRemove],
            iconSize: 20),
        new IconMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.FileOpValidate),
            "\uE73E",
            iconSize: 20),
#if DEBUG
        new IconMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.FileOpTestNext),
            "\uE14A",
            iconSize: 20),
#endif
    ];
}

internal static class LogControls
{
    public static ObservableList<IMenuItem> List { get; } =
    [
        AppActions.ToggleActions.Find(a => a.FileAction.Name is FileAction.FileActionType.PauseLogs).Button,
        new IconMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.ClearLogs),
            AppActions.Icons[FileAction.FileActionType.FileOpRemove],
            iconSize: 20)
    ];
}

internal static class ResetSettings
{
    public static ObservableList<IMenuItem> List { get; } =
    [
        new CompoundDualAction(AppActions.List.Find(a => a.Name is FileAction.FileActionType.ResetSettings),
            new ResetSettingsIcon()),
    ];
}

internal static class SettingsControls
{
    public static ObservableList<IMenuItem> List { get; } =
    [
        AppActions.ToggleActions.Find(a => a.FileAction.Name is FileAction.FileActionType.SortSettings).Button,
        AppActions.ToggleActions.Find(a => a.FileAction.Name is FileAction.FileActionType.ExpandSettings).Button,
    ];
}

internal static class LogToggle
{
    public static ObservableList<IMenuItem> List { get; } =
    [
        AppActions.ToggleActions.Find(a => a.FileAction.Name is FileAction.FileActionType.LogToggle).Button,
    ];
}

internal static class PeekDetailed
{
    public static BaseAction Action { get; } = new(
            () => true,
            () => Data.RuntimeSettings.IsDetailedPeekMode = true);
}

internal static class DialogExtraButtons
{
    public static ObservableList<IMenuItem> List { get; } =
    [
        new IconMenu(AppActions.List.Find(a => a.Name is FileAction.FileActionType.CopyMessageToClipboard), "\uF0E3"),
    ];
}
