using static ADB_Explorer.Models.AbstractFile;

namespace ADB_Explorer.Services;

internal static partial class FileActionLogic
{
    public static void ClearExplorer(bool clearDevice = true)
    {
        App.SafeInvoke(() =>
        {
            Data.DirList?.FileList?.Clear();
            Data.Packages.Clear();
            Data.SelectedFiles = [];
            Data.SelectedPackages = [];

            Data.FileActions.PushFilesFoldersEnabled =
            Data.FileActions.PullEnabled =
            Data.FileActions.DeleteEnabled =
            Data.FileActions.BackupPackageEnabled =
            Data.FileActions.RenameEnabled =
            Data.FileActions.HomeEnabled =
            Data.FileActions.NewEnabled =
            Data.FileActions.PasteEnabled =
            Data.FileActions.IsUninstallVisible.Value =
            Data.FileActions.CutEnabled =
            Data.FileActions.CopyEnabled =
            Data.FileActions.IsExplorerVisible =
            Data.FileActions.IsSearchMode =
            Data.FileActions.PackageActionsEnabled =
            Data.FileActions.IsCopyItemPathEnabled =
            Data.FileActions.UpdateModifiedEnabled =
            Data.FileActions.IsFollowLinkEnabled =
            Data.RuntimeSettings.IsExplorerLoaded =
            Data.FileActions.ParentEnabled = false;

            Data.FileActions.IsCutPasteDeleteVisible.Value = true;
            Data.FileActions.IsPullCopyVisible.Value = true;
            Data.FileActions.IsPasteVisible.Value = true;

            Data.FileActions.IsAppDrive = false;
            Data.FileActions.IsRecycleBin = false;
            Data.FileActions.IsArchive = false;
            Data.FileActions.IsTemp = false;

            Data.FileActions.ExplorerFilter = "";

            if (clearDevice)
            {
                // Only this device's names - the cache is shared by every tab's device.
                var deviceId = Data.ActiveDevice?.ID;
                foreach (var key in Data.CurrentDisplayNames.Keys.Where(key => key.DeviceId == deviceId).ToList())
                    Data.CurrentDisplayNames.Remove(key);

                Data.CurrentPath = "";
                Data.DirList?.ClearCurrentLocation();
                Data.RaiseClearNavigationBox();

                // Nothing else ever nulls this out, so UpdateFileActions() below would otherwise
                // keep resolving the stale, disconnected device instead of falling back to
                // DevicesObject.Current (correctly null here) - e.g. leaving PushPackageEnabled on.
                Data.Files.Device = null;

                UpdateFileActions();
            }
        });

        Data.RequestExplorer(ExplorerRequest.FilterActions);
    }

    public static void UpdateFileActions() => UpdateFileActions(Data.Files);

    public static void UpdateFileActions(FileList list)
    {
        using (Data.Use(list))
            UpdateFileActionsCore(list);
    }

    /// <summary>
    /// Selection, location and device state that every action's enabled state is derived from.
    /// </summary>
    private sealed class ActionContext
    {
        public FileList List { get; }
        public FileActionsEnable Actions { get; }
        public IEnumerable<FileClass> SelectedFiles { get; }
        public FileClass? SelectedFile { get; }
        public bool HasFileSelection { get; }
        public bool HasPackageSelection { get; }
        public bool SingleFileSelected { get; }
        public bool SinglePackageSelected { get; }

        public bool IsAppDrive { get; }
        public bool IsRecycleBin { get; }
        public bool IsSearchMode { get; }
        public bool IsExplorerVisible { get; }
        public bool IsDriveViewVisible { get; }
        public bool IsTreeList { get; }
        public bool EnableApk { get; }
        public LogicalDeviceViewModel? Device { get; }
        public string? DeviceId { get; }
        public bool IsNotRecovery { get; }
        public bool HasRoot { get; }
        public bool FuseProtectedRoot { get; }

        public bool IsRegularItem { get; }
        public bool IsFollowLinkEnabled { get; }
        public bool FollowLinkAllowsAction { get; }
        public bool NoBrokenLinks { get; }
        public string? ListingPath { get; }
        public DriveRestrictions Restrictions { get; }
        public bool IsArchive { get; }
        public bool IsWritable { get; }
        public bool CanPasteIntoTar { get; }
        public bool ArchiveAllowsModify { get; }
        public bool IsExplorerFolder { get; }

        public bool CutIsMove { get; }
        public bool CutIsCopy { get; }
        public bool CutIsLink { get; }

        public ActionContext(FileList list)
        {
            List = list;
            Actions = list.Actions;
            SelectedFiles = list.SelectedFiles ?? [];
            var selectedPackages = list.SelectedPackages ?? [];
            HasFileSelection = SelectedFiles.Any();
            HasPackageSelection = selectedPackages.Any();
            SingleFileSelected = SelectedFiles.Count() == 1;
            SinglePackageSelected = selectedPackages.Count() == 1;
            SelectedFile = SingleFileSelected ? SelectedFiles.First() : null;

            IsAppDrive = Actions.IsAppDrive;
            IsRecycleBin = Actions.IsRecycleBin;
            IsSearchMode = Actions.IsSearchMode;
            IsExplorerVisible = Actions.IsExplorerVisible;
            IsDriveViewVisible = Actions.IsDriveViewVisible;
            IsTreeList = !ReferenceEquals(list, Data.Files);
            EnableApk = Data.Settings.EnableApk;
            Device = list.Device ?? Data.DevicesObject?.Current;
            DeviceId = Device?.ID;
            IsNotRecovery = Device?.Type is not DeviceType.Recovery;
            HasRoot = HasRootShell;
            FuseProtectedRoot = SelectionIsFuseProtectedAndroidRoot;

            IsRegularItem = !HasFileSelection || HasRoot
                || SelectedFiles.AnyAll(item => item.Type is FileType.File or FileType.Folder);

            IsFollowLinkEnabled = !IsRecycleBin
                && SingleFileSelected
                && SelectedFile is { IsLink: true, Type: not FileType.BrokenLink };

            FollowLinkAllowsAction = !IsFollowLinkEnabled || HasRoot;
            NoBrokenLinks = SelectedFiles.AnyAll(f => f.Type is not FileType.BrokenLink);

            ListingPath = list.DirList?.CurrentPath ?? list.Path;
            Restrictions = DriveHelper.GetRestrictions(ListingPath, Device);
            IsArchive = DeviceId is not null && !string.IsNullOrEmpty(ListingPath)
                ? ArchivePath.IsArchivePath(ListingPath, DeviceId)
                : Actions.IsArchive;

            // CanWrite (when set) is computed by the caller against the actual selected node's own
            // path - e.g. a tree drive node's Path is its parent, so the listing path's restrictions
            // would otherwise check the wrong location's writability for it.
            if (list.CanWrite is bool canWrite)
                IsWritable = canWrite;
            else if (Restrictions.ReadOnly is true)
                IsWritable = false;
            else if (IsSearchMode)
                IsWritable = Data.SearchOriginCanWrite;
            else
                IsWritable = list.DirList?.CurrentLocation?.CanWriteLocation == true;

            CanPasteIntoTar = DeviceId is not null
                && ArchiveHelper.CanPasteIntoArchive(list.Path ?? "", DeviceId);

            ArchiveAllowsModify = !IsArchive || CanPasteIntoTar;
            IsExplorerFolder = IsExplorerVisible
                && !IsRecycleBin
                && !IsAppDrive
                && !IsArchive
                && !IsSearchMode;

            var selectedFiles = SelectedFiles;
            var allSelectedAreCut = Data.CopyPaste.IsFromDevice(list.Device ?? ActionDevice)
                && Data.CopyPaste.Files.Length == selectedFiles.Count()
                && Data.CopyPaste.Files.AnyAll(item => selectedFiles.Any(f => NavigationTreeNode.PathsEqual(f.FullPath, item)));

            CutIsMove = allSelectedAreCut && Data.CopyPaste.PasteState is DragDropEffects.Move;
            CutIsCopy = allSelectedAreCut && Data.CopyPaste.PasteState is DragDropEffects.Copy;
            CutIsLink = allSelectedAreCut && Data.CopyPaste.PasteState is DragDropEffects.Link;
        }
    }

    private static void UpdateFileActionsCore(FileList list)
    {
        var context = new ActionContext(list);

        UpdateSelectionState(context);
        UpdatePackageActions(context);
        UpdateNavigationActions(context);
        UpdateCreateActions(context);
        UpdateDeleteActions(context);
        UpdateTransferActions(context);
        UpdateArchiveActions(context);
        ApplyListRestrictions(context);

        if (!Data.CopyPaste.IsDrag && !context.IsTreeList)
            Data.RequestExplorer(ExplorerRequest.FilterActions);
    }

    private static void UpdateSelectionState(ActionContext c)
    {
        var actions = c.Actions;
        var selectedFiles = c.SelectedFiles;

        actions.IsRegularItem = c.IsRegularItem;
        actions.IsSingleFolder = c.SelectedFile is not null && CanEnterSelection(c.SelectedFile);
        actions.IsFollowLinkEnabled = c.IsFollowLinkEnabled;
        actions.IsOpenItemLocationEnabled = !c.IsRecycleBin && c.IsSearchMode && c.SelectedFile is not null;
        actions.IsArchive = c.IsArchive;

        actions.IsCurrentLocationReadOnly = (c.IsExplorerFolder || c.CanPasteIntoTar) && !c.IsWritable;
        actions.IsSelectionFuseProtectedAndroidRoot = c.HasFileSelection && c.FuseProtectedRoot;

        actions.IsSelectionIllegalOnWindows = c.HasFileSelection
            && !FileHelper.FileNameLegal(selectedFiles, FileHelper.RenameTarget.Windows);

        actions.IsSelectionIllegalNaming = !c.IsRecycleBin
            && !c.IsAppDrive
            && !c.IsArchive
            && c.HasFileSelection
            && !FileHelper.FileNameLegal(selectedFiles, FileHelper.RenameTarget.RestrictedNaming);

        actions.IsSelectionIllegalOnWinRoot = c.HasFileSelection
            && !FileHelper.FileNameLegal(selectedFiles, FileHelper.RenameTarget.WinRoot);

        actions.IsSelectionConflictingNames = c.Restrictions.CaseInsensitiveNames
            && selectedFiles.Select(f => f.FullName).Distinct(StringComparer.InvariantCultureIgnoreCase).Count() != selectedFiles.Count();
    }

    private static void UpdatePackageActions(ActionContext c)
    {
        var actions = c.Actions;
        var selectedFiles = c.SelectedFiles;

        actions.IsApkActionsVisible.Value = c.EnableApk && c.Device is not null;
        actions.PushPackageEnabled = actions.IsApkActionsVisible && c.IsNotRecovery;

        actions.UninstallPackageEnabled = c.IsAppDrive && c.HasPackageSelection;
        actions.ContextPushPackagesEnabled = c.IsAppDrive && !c.HasPackageSelection;

        actions.IsOpenApkLocationEnabled = c.IsAppDrive && c.SinglePackageSelected;
        actions.IsApkWebSearchEnabled = actions.IsOpenApkLocationEnabled
            && !string.IsNullOrEmpty(Data.RuntimeSettings.DefaultBrowserPath);

        // APK enabled in settings, all selected files are installable (or app backups), not in trash or recovery.
        var allInstallOrBackup = selectedFiles.AnyAll(file =>
            file.IsInstallApk || AppBackupHelper.IsApkBackup(file.FullName));

        actions.PackageActionsEnabled = c.EnableApk
            && allInstallOrBackup
            && !c.IsRecycleBin
            && c.IsNotRecovery;

        actions.SubmenuUninstallEnabled = selectedFiles.AnyAll(file => file.IsInstallApk) && c.IsNotRecovery;
        actions.InstallPackageEnabled = c.IsNotRecovery;
    }

    private static void UpdateNavigationActions(ActionContext c)
    {
        var actions = c.Actions;

        actions.IsRefreshEnabled = c.IsDriveViewVisible || c.IsExplorerVisible;
        actions.IsPushMenuVisible.Value = !c.IsSearchMode;
        UpdateNavRefreshActionState();

        actions.IsCopyCurrentPathEnabled = c.IsExplorerVisible
            && !c.IsRecycleBin
            && !c.IsAppDrive
            && !c.IsSearchMode;

        NavigationTreeNode? treeNode = c.IsTreeList ? ExplorerTree?.ContextTarget : null;

        if (treeNode is not null)
            actions.IsOpenInNewTabEnabled = c.Device is not null && !treeNode.IsTemp && !treeNode.IsInEditMode;
        else if (c.Device is not null && !c.IsRecycleBin)
            actions.IsOpenInNewTabEnabled = GetNewTabPath(GetNewTabItem(c.List), c.Device, c.IsAppDrive, c.IsArchive, c.IsSearchMode) is not null;
        else
            actions.IsOpenInNewTabEnabled = false;

        if (c.IsAppDrive)
            actions.IsCopyItemPathEnabled = c.SinglePackageSelected;
        else
            actions.IsCopyItemPathEnabled = c.SingleFileSelected && !c.IsRecycleBin;

        actions.CopyPathDescription.Value = c.IsAppDrive
            ? Strings.Resources.S_COPY_APK_NAME
            : Strings.Resources.S_COPY_PATH;
    }

    private static void UpdateCreateActions(ActionContext c)
    {
        var actions = c.Actions;

        // Push into modifiable tar is allowed; New File/Folder inside modifiable tar is allowed.
        var isSearchFolderTarget = c.IsSearchMode && c.SelectedFile is { IsDirectory: true };

        actions.PushFilesFoldersEnabled = c.IsWritable && (c.IsExplorerFolder || c.CanPasteIntoTar || isSearchFolderTarget);
        actions.NewEnabled = c.IsWritable && (c.IsExplorerFolder || c.CanPasteIntoTar);
        actions.IsNewMenuVisible.Value = c.IsExplorerVisible
            && !c.IsRecycleBin
            && !c.IsAppDrive
            && actions.NewEnabled;

        var singlePasteTarget = c.SelectedFile is not null
            && ArchiveHelper.IsPasteTargetContainer(c.SelectedFile, c.DeviceId ?? "");

        actions.ContextPushEnabled = c.IsWritable
            && !c.IsRecycleBin
            && !c.IsAppDrive
            && c.ArchiveAllowsModify
            && (c.IsSearchMode
                ? singlePasteTarget
                : !c.HasFileSelection || singlePasteTarget);

        var contextNewOnFolder = c.IsTreeList && c.SelectedFile is { IsDirectory: true };

        actions.ContextNewEnabled = c.IsWritable
            && !c.IsRecycleBin
            && !c.IsAppDrive
            && !c.IsSearchMode
            && c.ArchiveAllowsModify
            && !c.List.ForbidNew
            && (!c.HasFileSelection || contextNewOnFolder);

        actions.UpdateModifiedEnabled = c.IsWritable
            && !c.IsRecycleBin
            && c.SelectedFiles.AnyAll(file => file.Type is FileType.File && !file.IsApk && !file.IsLink);

        actions.IsContextSortViewEnabled = c.IsExplorerVisible
            && !c.IsTreeList
            && !c.HasFileSelection
            && !c.HasPackageSelection;
    }

    private static void UpdateDeleteActions(ActionContext c)
    {
        var actions = c.Actions;

        if (c.IsRecycleBin)
        {
            if (c.HasFileSelection)
                TrashHelper.EnableRecycleButtons(c.SelectedFiles);
            else if (c.List.DirList?.FileList is { } recycleFiles)
                TrashHelper.EnableRecycleButtons(recycleFiles);
            else
            {
                var count = TrashHelper.EnsureRecycleCount(c.Device, c.List.CurrentDrive as VirtualDriveViewModel);
                actions.DeleteEnabled = count > 0;
                actions.RestoreEnabled = false;
            }
        }
        else if (SelectedTrashDrive() is { ItemsCount: > 0 })
        {
            actions.DeleteEnabled = true;
            actions.RestoreEnabled = false;
        }
        else
        {
            actions.DeleteEnabled = c.IsWritable
                && !c.FuseProtectedRoot
                && c.HasFileSelection
                && c.IsRegularItem
                && c.FollowLinkAllowsAction
                && c.ArchiveAllowsModify;

            actions.RestoreEnabled = false;
        }

        if (c.IsRecycleBin)
        {
            var recycleDelete = c.HasFileSelection
                ? Strings.Resources.S_PERM_DEL
                : Strings.Resources.S_EMPTY_TRASH;
            actions.DeleteDescription.Value = recycleDelete;
            actions.ContextDeleteDescription.Value = recycleDelete;
        }
        else if (IsTrashDriveSelectedInDriveView())
        {
            actions.DeleteDescription.Value = Strings.Resources.S_EMPTY_TRASH;
            actions.ContextDeleteDescription.Value = Strings.Resources.S_EMPTY_TRASH;
        }
        else
        {
            actions.DeleteDescription.Value = Strings.Resources.S_DELETE_ACTION;
            actions.ContextDeleteDescription.Value =
                c.IsArchive || Keyboard.Modifiers is ModifierKeys.Shift
                    ? Strings.Resources.S_PERM_DEL
                    : Strings.Resources.S_DELETE_ACTION;
        }

        actions.RestoreDescription.Value = c.IsRecycleBin && !c.HasFileSelection
            ? Strings.Resources.S_RESTORE_ALL
            : Strings.Resources.S_RESTORE_ACTION;
    }

    private static void UpdateTransferActions(ActionContext c)
    {
        var actions = c.Actions;
        var selectedFile = c.SelectedFile;

        actions.PullDescription.Value = c.IsFollowLinkEnabled
            ? Strings.Resources.S_PULL_ACTION_LINK
            : Strings.Resources.S_PULL_ACTION;

        // Pull from archive extracts selected members to /data/local/tmp then pulls (same as PrepareDescriptors).
        if (c.IsAppDrive)
            actions.PullEnabled = c.HasPackageSelection;
        else
        {
            var canPullArchiveMembers = !c.IsArchive
                || c.SelectedFiles.AnyAll(f =>
                    ArchivePath.TryParse(f.FullPath, out _, out var inner, c.DeviceId)
                    && !string.IsNullOrEmpty(inner));

            actions.PullEnabled = !c.IsRecycleBin
                && c.NoBrokenLinks
                && c.IsRegularItem
                && !actions.IsSelectionIllegalOnWindows
                && !actions.IsSelectionIllegalNaming
                && !actions.IsSelectionConflictingNames
                && canPullArchiveMembers;
        }

        actions.RenameEnabled = c.IsWritable
            && c.ArchiveAllowsModify
            && !c.FuseProtectedRoot
            && !c.IsRecycleBin
            && c.SingleFileSelected
            && c.IsRegularItem
            && c.FollowLinkAllowsAction;

        // Cut from archive is not supported (extract is copy-only).
        actions.CutEnabled = c.IsWritable
            && !c.IsArchive
            && !c.FuseProtectedRoot
            && c.NoBrokenLinks
            && !c.CutIsMove
            && c.IsRegularItem
            && c.FollowLinkAllowsAction;

        if (c.IsAppDrive)
            actions.CopyEnabled = c.HasPackageSelection;
        else
        {
            actions.CopyEnabled = c.NoBrokenLinks
                && !c.CutIsCopy
                && !c.CutIsLink
                && c.IsRegularItem
                && !c.IsRecycleBin;
        }

        IsPasteEnabled();

        actions.IsCopyAsImageEnabled = !c.IsAppDrive
            && !c.IsRecycleBin
            && selectedFile is not null
            && FileHelper.IsSupportedImageFile(selectedFile);

        // Search results have no folder of their own to link into.
        string? pasteLinkTarget;
        if (!c.HasFileSelection)
            pasteLinkTarget = c.IsSearchMode ? null : c.List.Path;
        else if (selectedFile is { IsDirectory: true })
            pasteLinkTarget = selectedFile.IsLink ? selectedFile.LinkTarget : selectedFile.FullPath;
        else
            pasteLinkTarget = null;

        actions.IsPasteLinkEnabled = !c.IsAppDrive
            && !c.IsRecycleBin
            && !c.List.ForbidPaste
            && pasteLinkTarget is not null
            && Data.CopyPaste.Files.Length == 1
            && Data.CopyPaste.IsSelf
            && Data.CopyPaste.PasteState is DragDropEffects.Copy or DragDropEffects.Link
            && IsSymlinkPasteAllowed(pasteLinkTarget);

        actions.IsCopyLinkEnabled = c.Restrictions.NoSymbolicLinks is not true
            && c.HasRoot
            && c.SingleFileSelected
            && c.NoBrokenLinks
            && c.IsRegularItem
            && !c.IsRecycleBin
            && !c.CutIsLink;
    }

    private static void UpdateArchiveActions(ActionContext c)
    {
        var actions = c.Actions;

        var clipboardIsSelectedArchiveContents = IsClipboardContentsOfSelectedArchive();
        actions.IsCopyContentsEnabled =
            TryGetSelectedNavigableArchive(out _)
            && !clipboardIsSelectedArchiveContents;

        actions.IsExtractHereEnabled =
            (CanExtractSelectedArchiveHere() && !clipboardIsSelectedArchiveContents)
            || CanExtractClipboardArchiveHere();

        var tarAvailable = c.Device is not null
            && ShellCommands.TarExists(c.Device.ID);

        actions.IsCompressToEnabled.Value = actions.NewEnabled
            && !c.IsArchive
            && tarAvailable;
        actions.IsCompressToContextEnabled = actions.IsCompressToEnabled
            && c.HasFileSelection;

        actions.BackupPackageEnabled = c.IsAppDrive
            && c.HasPackageSelection
            && tarAvailable
            && c.IsNotRecovery;
    }

    private static void ApplyListRestrictions(ActionContext c)
    {
        var actions = c.Actions;

        if (c.List.ForbidDestructive)
        {
            actions.CutEnabled = false;
            actions.RenameEnabled = false;
            if (!c.IsRecycleBin)
                actions.DeleteEnabled = false;
        }

        if (c.List.DirList is null)
        {
            if (!c.IsTreeList)
            {
                actions.NewEnabled = false;
                actions.ContextNewEnabled = false;
                actions.RenameEnabled = false;
            }

            actions.IsCompressToEnabled.Value = false;
            actions.IsCompressToContextEnabled = false;
            actions.IsSingleFolder = false;
        }

        Data.FileActions.IsContextNewFileVisible.Value = !c.IsTreeList;
        Data.FileActions.IsContextNewArchiveVisible.Value = !c.IsTreeList && Data.FileActions.IsCompressToEnabled;

        if (c.IsTreeList)
            Data.FileActions.ContextDeleteDescription.Value = actions.ContextDeleteDescription.Value;
    }

    private static void UpdateNavRefreshActionState()
    {
        if (Data.FileActions.IsSearchMode && Data.FileActions.ListingInProgress)
        {
            Data.FileActions.NavRefreshDescription.Value = string.Format(
                Strings.Resources.S_MENU_STOP_LOADING,
                SearchStopPathLabel());
            Data.FileActions.NavRefreshIcon.Value = new BaseIcon("\uE711", 16);
        }
        else
        {
            Data.FileActions.NavRefreshDescription.Value = Strings.Resources.S_MENU_REFRESH;
            Data.FileActions.NavRefreshIcon.Value = new BaseIcon("\uE72C", 16);
        }
    }

    private static string SearchStopPathLabel()
    {
        var root = Data.SearchOriginPath;
        if (string.IsNullOrEmpty(root))
            return Data.FileActions.ExplorerFilter ?? "";

        return Data.CurrentDisplayNames.TryGetValue((Data.ActiveDevice?.ID, root), out var displayName)
            ? displayName
            : FileHelper.GetFullName(root);
    }
}
