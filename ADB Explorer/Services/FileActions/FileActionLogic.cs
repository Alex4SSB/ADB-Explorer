using static ADB_Explorer.Models.AbstractFile;

namespace ADB_Explorer.Services;

internal static partial class FileActionLogic
{
    private static FileList ActionList => Data.Active;

    private static FileActionsEnable ActionFlags => ActionList.Actions;

    private static LogicalDeviceViewModel? ActionDevice => ActionList.Device ?? Data.DevicesObject?.Current;

    private static string ActionPath => ActionList.Path;

    private static NavigationTreeViewModel? ExplorerTree
        => App.Services.GetService<ExplorerViewModel>()?.Tree;

    private static bool HasRootShell => ActionDevice?.HasRootShell == true;

    private static bool SelectionIsFuseProtectedAndroidRoot =>
        Data.SelectedFiles.Any(f => ShellAccessHelper.IsFuseProtectedAndroidRoot(f.FullPath));

    private static bool IsTrashDriveSelectedInDriveView()
        => Data.FileActions.IsDriveViewVisible
           && Data.RuntimeSettings.SelectedDrive?.Type is AbstractDrive.DriveType.Trash;

    private static VirtualDriveViewModel? SelectedTrashDrive()
        => Data.RuntimeSettings.SelectedDrive is VirtualDriveViewModel { Type: AbstractDrive.DriveType.Trash } trash
            ? trash
            : null;

    public static void UpdateModifiedDates()
    {
        ShellFileOperation.ChangeDateFromName(ActionDevice, Data.SelectedFiles, App.AppDispatcher);
    }

    public static void RestoreItems()
    {
        var listed = Data.DirList?.FileList;
        var restoreSource = !Data.SelectedFiles.Any() && listed is not null ? listed : Data.SelectedFiles;
        var restoreItems = restoreSource.Where(file => file.TrashIndex is not null && !string.IsNullOrEmpty(file.TrashIndex.OriginalPath));
        string[] existingItems = [];
        List<FileClass> existingFiles = [];
        bool merge = false;

        var restoreTask = Task.Run(() =>
        {
            existingItems = AdbService.PathsExist(ActionDevice.ID, restoreItems.Select(file => file.TrashIndex!.OriginalPath));
            if (existingItems?.Length > 0)
            {
                if (restoreItems.Any(item => item.IsDirectory && existingItems.Contains(item.TrashIndex!.OriginalPath)))
                    merge = true;

                existingItems = [.. existingItems.Select(path => path[(path.LastIndexOf('/') + 1)..])];
            }

            foreach (var item in restoreItems)
            {
                if (existingItems.Contains(item.FullName))
                    return;

                if (restoreItems.Count(file => file.FullName == item.FullName && file.TrashIndex!.OriginalPath == item.TrashIndex!.OriginalPath) > 1)
                {
                    existingItems = [.. existingItems, item.FullName];
                    existingFiles.Add(item);
                    if (item.IsDirectory)
                        merge = true;
                }
            }
        });

        restoreTask.ContinueWith((t) =>
        {
            App.SafeBeginInvoke(async () =>
            {
                if (existingItems.Length is int count and > 0)
                {
                    var result = await DialogService.ShowConfirmation(
                        count == 1
                            ? Strings.Resources.S_CONFLICT_ITEMS
                            : string.Format(Strings.Resources.S_CONFLICT_ITEMS_PLURAL, count),
                        Strings.Resources.S_RESTORE_CONF_TITLE,
                        primaryText: merge
                            ? Strings.Resources.S_MERGE_OR_REPLACE
                            : Strings.Resources.S_REPLACE,
                        secondaryText: count == restoreItems.Count() ? "" : Strings.Resources.S_SKIP,
                        cancelText: Strings.Resources.S_CANCEL,
                        icon: DialogService.DialogIcon.Exclamation);

                    if (result.Item1 is Wpf.Ui.Controls.ContentDialogResult.None)
                    {
                        return;
                    }

                    if (result.Item1 is Wpf.Ui.Controls.ContentDialogResult.Secondary)
                    {
                        restoreItems = existingFiles.Count != count
                            ? restoreItems.Where(item => !existingItems.Contains(item.FullName))
                            : restoreItems.Except(existingFiles);
                    }
                }

                ShellFileOperation.MoveItems(device: ActionDevice,
                                         items: restoreItems,
                                         targetPath: null,
                                         currentPath: ActionPath,
                                         fileList: Data.DirList?.FileList,
                                         dispatcher: App.AppDispatcher);

                var remainingItems = Data.DirList?.FileList is { } listed
                    ? listed.Except(restoreItems)
                    : [];
                TrashHelper.EnableRecycleButtons(remainingItems);

                // Clear all remaining files if none of them are indexed
                if (!remainingItems.Any(item => item.TrashIndex is not null))
                {
                    _ = Task.Run(() => ShellFileOperation.SilentDelete(ActionDevice, remainingItems));
                }

                if (!Data.SelectedFiles.Any())
                    TrashHelper.EnableRecycleButtons();
            });
        });
    }

    public static void CopyItemPath()
    {
        var path = ActionFlags.IsAppDrive ? Data.SelectedPackages.First().Name : Data.SelectedFiles.First().FullPath;
        Clipboard.SetText(path);
    }

    public static async Task CreateNewItem(FileClass file, string? newName = null, LogicalDeviceViewModel? device = null, bool selectCreated = true)
    {
        if (!string.IsNullOrEmpty(newName))
            file.UpdatePath(FileHelper.ConcatPaths(file.ParentPath, newName));

        if (Data.Settings.ShowExtensions)
            file.UpdateType();

        try
        {
            device ??= ActionDevice;
            if (device is null)
            {
                Data.DirList?.FileList.Remove(file);
                ExplorerTree?.CancelTempFile(file);
                return;
            }

            if (TryConsumePendingClipboardImage(file, out var clipboardStagingPath))
            {
                ShowCreatedItem(file);

                PushClipboardImageFile(file, clipboardStagingPath, device);
                return;
            }
            else if (TryConsumePendingCompress(file, out var compressSources))
            {
                ShowCreatedItem(file);

                ShellFileOperation.CompressArchive(device, file, compressSources, App.AppDispatcher);
                return;
            }
            else if (ArchivePath.TryParse(file.FullPath, out var archivePath, out var internalPath, device.ID)
                && !string.IsNullOrEmpty(internalPath)
                && ArchiveHelper.CanPasteIntoArchive(file.FullPath, device.ID))
            {
                await Task.Run(() => ArchiveExtract.CreateTarMember(
                    device.ID,
                    archivePath,
                    internalPath,
                    file.Type is FileType.Folder,
                    Data.DeviceCts.Token));
            }
            else if (file.Type is FileType.Folder)
                await ShellFileOperation.MakeDir(device, file.FullPath);
            else if (file.Type is FileType.File)
                await ShellFileOperation.MakeFile(device, file.FullPath);
            else
                throw new NotSupportedException();
        }
        catch (Exception e)
        {
            DialogService.ShowMessage(e.Message,
                                      Strings.Resources.S_CREATE_ERR_TITLE,
                                      DialogService.DialogIcon.Critical,
                                      copyToClipboard: true,
                                      error: DialogError.CreateFileFailed);
            Data.DirList?.FileList.Remove(file);
            ExplorerTree?.CancelTempFile(file);
            return;
        }

        file.IsTemp = false;
        file.ModifiedTime = DateTime.Now;
        if (file.Type is FileType.File)
            file.Size = 0;

        // Temp rename may have marked this resolved before the path existed on device.
        file.IsCreationTimeResolved = false;

        if (Data.DirList?.FileList is { } listing
            && device.ID == (Data.Files.Device?.ID ?? Data.ActiveDevice?.ID)
            && NavigationTreeNode.PathsEqual(Data.CurrentPath, file.ParentPath)
            && listing.IndexOf(file) < 0)
            listing.Insert(0, file);

        RefreshNewItemInList(file);
        ExplorerTree?.CompleteTempFile(file);
        if (selectCreated && Data.DirList?.FileList?.Contains(file) == true)
            Data.ItemToSelect.Value = file;
    }

    /// <summary>Turns the placeholder of a new item into a regular, selected list entry while its content is being produced.</summary>
    private static void ShowCreatedItem(FileClass file)
    {
        file.IsTemp = false;
        file.ModifiedTime = DateTime.Now;
        file.Size = null;
        file.IsCreationTimeResolved = false;
        file.UpdateType();

        RefreshNewItemInList(file);
        Data.ItemToSelect.Value = file;
    }

    private static void RefreshNewItemInList(FileClass file)
    {
        if (Data.DirList?.FileList is not { } files)
            return;

        var index = files.IndexOf(file);
        if (index < 0)
            return;

        files.Remove(file);
        files.Insert(index, file);
    }

    public static void NewFolder()
    {
        if (!ReferenceEquals(Data.Active, Data.Files)
            && ExplorerTree?.ContextTarget is { } node)
        {
            ExplorerTree.QueueNewFolder(node);
            return;
        }

        Data.RequestExplorer(ExplorerRequest.NewFolder);
    }

    public static void ContextRename()
    {
        if (!ReferenceEquals(Data.Active, Data.Files)
            && ExplorerTree?.ContextTarget is { } node)
        {
            ExplorerTree.QueueRename(node);
            return;
        }

        Data.RequestExplorer(ExplorerRequest.Rename);
    }

    public static void RemoveSavedLocation()
    {
        if (ExplorerTree?.ContextTarget is not { IsSavedLocation: true } node)
            return;

        var deviceId = node.OwnerDevice?.ID;
        var entry = Data.Settings.SavedLocations.FirstOrDefault(e =>
            e.DeviceId == deviceId && NavigationTreeNode.PathsEqual(e.Path, node.Path));

        if (entry is not null)
            Data.Settings.SavedLocations.Remove(entry);
    }

    /// <summary>
    /// Commits a rename, unless a search-mode unique-name check is still pending (then returns false).
    /// </summary>
    public static bool CommitRename(FileClass file, string text)
    {
        var vm = file.ActiveViewModel;
        if (vm.IsCheckingUniqueName)
            return false;

        Rename(file, text);
        vm.CancelUniqueNameCheck();

        return true;
    }

    /// <summary>
    /// Ends a rename without committing. Returns the name to restore, or null when the item was discarded.
    /// </summary>
    public static string? CancelRename(FileClass file, bool discardNew)
    {
        file.ActiveViewModel.CancelUniqueNameCheck();

        if (!(file.IsTemp && discardNew) && FileHelper.DisplayName(file) is { Length: > 0 } name)
            return name;

        DiscardNewItem(file);
        return null;
    }

    private static void DiscardNewItem(FileClass file)
    {
        CancelPendingCompress(file);
        CancelPendingClipboardImage(file);
        Data.DirList.FileList.Remove(file);
    }

    private static void Rename(FileClass file, string text)
    {
        var vm = file.ActiveViewModel;

        if (!vm.IsRenameUnixLegal
            || (DriveHelper.GetRestrictions(file.FullPath).RestrictedNaming && !vm.IsRenameNamingLegal)
            || !vm.IsRenameUnique)
        {
            return;
        }

        if (file.IsTemp)
        {
            if (string.IsNullOrEmpty(text))
            {
                DiscardNewItem(file);
                return;
            }

            var newName = text;
            if (!Data.Settings.ShowExtensions)
                newName += file.Extension;

            _ = CreateNewItem(file, newName);
        }
        else if (!string.IsNullOrEmpty(text) && text != FileHelper.DisplayName(file))
        {
            try
            {
                FileHelper.RenameFile(file, StripSingleRtlMark(text));
            }
            catch (Exception)
            { }
        }
    }

    private static string StripSingleRtlMark(string text)
        => text.Count(c => c == TextHelper.RTL_MARK) == 1
            ? text.Replace($"{TextHelper.RTL_MARK}", "")
            : text;

    public static void RenameTreeNode(NavigationTreeNode node, string text)
    {
        if (node.File is not { } file)
            return;

        if (!node.IsRenameUnixLegal
            || (DriveHelper.GetRestrictions(node.Path, node.OwnerDevice).RestrictedNaming && !node.IsRenameNamingLegal)
            || !node.IsRenameUnique)
        {
            if (file.IsTemp)
                ExplorerTree?.CancelTempFile(file);
            return;
        }

        if (file.IsTemp)
        {
            if (string.IsNullOrEmpty(text))
            {
                ExplorerTree?.CancelTempFile(file);
                return;
            }

            _ = CreateNewItem(file, text, node.OwnerDevice, selectCreated: false);
            return;
        }

        if (string.IsNullOrEmpty(text) || text == node.DisplayName)
            return;

        try
        {
            text = StripSingleRtlMark(text);

            FileHelper.RenameFile(file, text, node.OwnerDevice);
            var newPath = FileHelper.ConcatPaths(FileHelper.GetParentPath(node.Path), text);
            node.DisplayName = text;
            node.UpdatePath(newPath);
            file.UpdatePath(newPath);
        }
        catch (Exception)
        { }
    }

    public static async void DeleteFiles(bool? permanent = null)
    {
        permanent ??= Keyboard.Modifiers is ModifierKeys.Shift;

        // Snapshot everything derived from Data.Active up front: a tree context menu's Data.Use scope
        // ends asynchronously once the menu closes (see NavigationPane.TreeContextMenu_Closed), which
        // can happen before the confirmation dialog below is answered, reverting Data.Active in the
        // meantime and making the live ActionDevice / ActionPath / Data.DirList properties unsafe to
        // read again after the first await.
        var device = ActionDevice;
        if (device is null)
            return;

        var isRecycleBin = ActionFlags.IsRecycleBin;
        var hasRootShell = device.HasRootShell;
        var actionPath = ActionPath;
        var dirFileList = Data.DirList?.FileList;

        var emptyTrashFromDriveView = IsTrashDriveSelectedInDriveView() && !Data.SelectedFiles.Any();
        var emptyingRecycleBin = (isRecycleBin && !Data.SelectedFiles.Any()) || emptyTrashFromDriveView;

        List<FileClass> itemsToDelete;
        if (emptyingRecycleBin)
        {
            if (emptyTrashFromDriveView || dirFileList is null)
                itemsToDelete = TrashHelper.GetRecycleBinItems();
            else
                itemsToDelete = [.. dirFileList.Where(f => f.Extension != AdbExplorerConst.RECYCLE_INDEX_SUFFIX)];
        }
        else
        {
            itemsToDelete = [.. hasRootShell
                ? Data.SelectedFiles
                : Data.SelectedFiles.Where(file => file.Type is FileType.File or FileType.Folder)];
        }
        
        string deletedString;
        if (itemsToDelete.Count == 1)
            deletedString = FileHelper.DisplayName(itemsToDelete.First());
        else
        {
            deletedString = $"{itemsToDelete.Count} ";
            if (itemsToDelete.All(item => item.IsDirectory))
                deletedString += Strings.Resources.S_MENU_FOLDERS;
            else if (itemsToDelete.All(item => !item.IsDirectory))
                deletedString += Strings.Resources.S_MENU_FILES;
            else
                deletedString += Strings.Resources.S_BROWSER_ITEMS_PLURAL;
        }

        if (!isRecycleBin && !emptyTrashFromDriveView && Data.Settings.EnableRecycle && !permanent.Value)
        {
            // Archive members cannot be moved to the recycle bin — always permanent-delete them.
            if (itemsToDelete.Any(f => ArchivePath.IsArchivePath(f.FullPath, device.ID)))
            {
                permanent = true;
            }
        }

        if (!Data.Settings.EnableRecycle || permanent.Value || emptyingRecycleBin)
        {
            var result = await DialogService.ShowConfirmation(
            string.Format(Strings.Resources.S_DELETE_PERMANENT, deletedString),
            Strings.Resources.S_DEL_CONF_TITLE,
            emptyingRecycleBin ? Strings.Resources.S_EMPTY_TRASH : Strings.Resources.S_DELETE_ACTION,
            icon: DialogService.DialogIcon.Delete);

            if (result.Item1 is not Wpf.Ui.Controls.ContentDialogResult.Primary)
                return;
        }

        if (!isRecycleBin && !emptyTrashFromDriveView && Data.Settings.EnableRecycle && !permanent.Value)
        {
            await ShellFileOperation.MakeDir(device, AdbExplorerConst.RECYCLE_PATH);

            ShellFileOperation.MoveItems(device,
                                         itemsToDelete,
                                         AdbExplorerConst.RECYCLE_PATH,
                                         actionPath,
                                         dirFileList,
                                         App.AppDispatcher);
        }
        else
        {
            ShellFileOperation.DeleteItems(device, itemsToDelete, App.AppDispatcher);

            if (emptyingRecycleBin)
                TrashHelper.GetTrashDrive(device)?.SetItemsCount(0);

            if (isRecycleBin)
            {
                var remainingItems = dirFileList is { } listed
                    ? listed.Except(itemsToDelete)
                    : [];
                TrashHelper.EnableRecycleButtons(remainingItems);

                // Clear all remaining files if none of them are indexed
                if (!remainingItems.Any(item => item.TrashIndex is not null))
                {
                    _ = Task.Run(() => ShellFileOperation.SilentDelete(device, remainingItems));
                }
            }
            else if (emptyTrashFromDriveView)
            {
                var indexPaths = AdbService.FindFilesInPath(device.ID,
                                                            AdbExplorerConst.RECYCLE_PATH,
                                                            includeNames: ["*" + AdbExplorerConst.RECYCLE_INDEX_SUFFIX]);
                if (indexPaths.Length > 0)
                    ShellFileOperation.SilentDelete(device, indexPaths);
            }
        }
    }
}
