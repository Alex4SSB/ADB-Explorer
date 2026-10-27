using Vanara.Windows.Shell;
using static ADB_Explorer.Models.AbstractFile;

namespace ADB_Explorer.Services;

internal static partial class FileActionLogic
{
    public static void PushItems(bool isFolderPicker, bool isContextMenu)
    {
        Data.RaiseFocusNavigationBox(false);

        string targetPath, targetName = "";
        string title = "";
        var deviceId = ActionDevice?.ID ?? "";
        if (isContextMenu && Data.SelectedFiles.Count() == 1)
        {
            var selected = Data.SelectedFiles.First();
            var path = selected.IsLink ? selected.LinkTarget : selected.FullPath;
            targetPath = ArchiveHelper.ResolvePasteTargetPath(path, deviceId);
            targetName = selected.FullName;

            title = isFolderPicker
                ? Strings.Resources.S_SELECT_FOLDER_PUSH_DESTINATION
                : Strings.Resources.S_SELECT_FILE_PUSH_DESTINATION;
        }
        else
        {
            targetPath = GetUiPasteTargetPath();

            title = isFolderPicker
                ? Strings.Resources.S_SELECT_FOLDER_PUSH
                : Strings.Resources.S_SELECT_FILE_PUSH;
        }
        
        var dialog = new CommonOpenFileDialog()
        {
            IsFolderPicker = isFolderPicker,
            Multiselect = true,
            DefaultDirectory = Data.Settings.DefaultFolder,
            Title = title,
        };

        if (dialog.ShowDialog() != CommonFileDialogResult.Ok)
            return;

        var shItems = dialog.FileNames.Select(ShellItem.Open);
        
        CopyPasteService.VerifyAndPush(targetPath, shItems);
    }

    public static FileSyncOperation PushShellObject(
        ShellItem item,
        string targetPath,
        DragDropEffects dropEffects = DragDropEffects.Copy,
        ShellItem? originalShellItem = null,
        IReadOnlySet<string>? replacePaths = null,
        IReadOnlySet<string>? conflictPaths = null,
        LogicalDeviceViewModel? device = null)
    {
        if (item is null)
            return null;

        device ??= Data.ActiveDevice;
        if (device is null)
            return null;

        var source = GetPushSource(item, targetPath, device.ID, replacePaths ?? EmptyPathSet, conflictPaths ?? EmptyPathSet);
        if (source is null)
            return null;

        var pushDevice = device;
        return QueuePush(() => FileSyncOperation.PushFile(source, DestinationOf(source, targetPath), pushDevice, App.AppDispatcher), dropEffects, originalShellItem);
    }

    /// <summary><paramref name="item"/>'s tree, less what needn't be pushed; null when nothing is left.</summary>
    private static SyncFile? GetPushSource(
        ShellItem item,
        string targetPath,
        string deviceId,
        IReadOnlySet<string> replacePaths,
        IReadOnlySet<string> conflictPaths)
    {
        SyncFile source;
        try
        {
            source = new SyncFile(item, true);
        }
        catch
        {
            return null;
        }

        if (string.IsNullOrEmpty(source.FullName))
            return null;

        if (!FilterPushTree(source, FileHelper.ConcatPaths(targetPath, source.FullName), deviceId, replacePaths, conflictPaths))
            return null;

        return source;
    }

    /// <summary>Drops what needn't be sent from <paramref name="source"/>'s tree to a device; false when nothing is left.</summary>
    private static bool FilterPushTree(
        SyncFile source,
        string androidDest,
        string deviceId,
        IReadOnlySet<string> replacePaths,
        IReadOnlySet<string> conflictPaths)
    {
        if (conflictPaths.Count > 0)
            return FileMergeHelper.FilterSyncTreeByConflictResolution(source, source.FullName, replacePaths, conflictPaths, '/');

        return FileMergeHelper.FilterIdenticalPushTree(source, androidDest, deviceId);
    }

    /// <summary>Where <paramref name="source"/> lands on the device, inside <paramref name="targetPath"/>.</summary>
    private static SyncFile DestinationOf(SyncFile source, string targetPath)
        => new(FileHelper.ConcatPaths(targetPath, source.FullName), source.IsDirectory ? FileType.Folder : FileType.File) { Size = source.Size };

    /// <summary>Items sharing a parent folder go in one op; an item alone in its folder gets its own.</summary>
    private static IEnumerable<IReadOnlyList<SyncFile>> GroupByParent(IEnumerable<SyncFile> items)
        => items.GroupBy(item => item.ParentPath).Select(group => (IReadOnlyList<SyncFile>)[.. group]);

    private static FileSyncOperation QueuePush(Func<FileSyncOperation> createOperation, DragDropEffects dropEffects, ShellItem? originalShellItem = null)
    {
        FileSyncOperation pushOperation = null!;
        App.SafeInvoke(() =>
        {
            pushOperation = createOperation();
            pushOperation.DropEffects = dropEffects;
            pushOperation.OriginalShellItem = originalShellItem;
            pushOperation.WhenFinished(status => OnPushFinished(pushOperation, status));
            Data.FileOpQ.AddOperation(pushOperation);
        });

        return pushOperation;
    }

    public static void TransferItems(
        IEnumerable<FileClass> items,
        string targetPath,
        bool isMove,
        LogicalDeviceViewModel sourceDevice,
        LogicalDeviceViewModel targetDevice,
        IReadOnlySet<string>? replacePaths = null,
        IReadOnlySet<string>? conflictPaths = null)
    {
        replacePaths ??= EmptyPathSet;
        conflictPaths ??= EmptyPathSet;

        var intoArchive = ArchiveHelper.CanPasteIntoArchive(targetPath, targetDevice.ID);
        List<FileTransferOperation> operations = [];
        List<SyncFile> plainSources = [];

        foreach (var item in items)
        {
            // Archive members and an archive target each need their own staging, so they aren't batched
            if (!intoArchive && !IsArchiveMember(item.FullPath, sourceDevice.ID))
            {
                var source = item.GetSyncFile(sourceDevice.ID);
                if (FilterPushTree(source, FileHelper.ConcatPaths(targetPath, source.FullName), targetDevice.ID, replacePaths, conflictPaths))
                    plainSources.Add(source);

                continue;
            }

            var operation = CreateTransferOp(item, targetPath, isMove, sourceDevice, targetDevice, intoArchive, replacePaths, conflictPaths);
            if (operation is null)
                continue;

            var archiveTarget = intoArchive ? targetPath : null;
            operation.WhenFinished(status => OnTransferFinished(operation, status, archiveTarget));
            operations.Add(operation);
        }

        foreach (var group in GroupByParent(plainSources))
        {
            FileTransferOperation operation = null!;
            App.SafeInvoke(() =>
            {
                if (group.Count == 1)
                    operation = new(group[0], DestinationOf(group[0], targetPath), sourceDevice, targetDevice, isMove, App.AppDispatcher);
                else
                    operation = FileTransferOperation.TransferBatch(group, new(targetPath, FileType.Folder), sourceDevice, targetDevice, isMove, App.AppDispatcher);
            });

            operation.WhenFinished(status => OnTransferFinished(operation, status, null));
            operations.Add(operation);
        }

        if (operations.Count > 0)
            App.SafeInvoke(() => Data.FileOpQ.AddOperations(operations));
    }

    private static bool IsArchiveMember(string path, string deviceId)
        => ArchivePath.TryParse(path, out _, out var internalPath, deviceId) && !string.IsNullOrEmpty(internalPath);

    // Archive members are unpacked first; an archive target is filled through a staging folder on the device.
    private static FileTransferOperation? CreateTransferOp(
        FileClass item,
        string targetPath,
        bool isMove,
        LogicalDeviceViewModel sourceDevice,
        LogicalDeviceViewModel targetDevice,
        bool intoArchive,
        IReadOnlySet<string> replacePaths,
        IReadOnlySet<string> conflictPaths)
    {
        SyncFile source;
        string? sourceStaging = null;

        if (ArchivePath.TryParse(item.FullPath, out var archivePath, out var internalPath, sourceDevice.ID)
            && !string.IsNullOrEmpty(internalPath))
        {
            (sourceStaging, source) = ArchiveExtract.ExtractSelectionAsSyncFile(sourceDevice.ID, archivePath, internalPath, item, Data.DeviceCts.Token);
        }
        else
        {
            source = item.GetSyncFile(sourceDevice.ID);
        }

        string? targetStaging = null;
        var destFolder = targetPath;
        if (intoArchive)
        {
            targetStaging = ArchiveExtract.CreateStagingRoot(targetDevice.ID);
            destFolder = targetStaging;
        }

        var target = DestinationOf(source, destFolder);

        if (!FilterPushTree(source, target.FullPath, targetDevice.ID, replacePaths, conflictPaths))
        {
            CleanupStaging(sourceDevice, sourceStaging);
            CleanupStaging(targetDevice, targetStaging);

            return null;
        }

        if (targetStaging is not null)
            ShellFileOperation.MakeDirs(targetDevice.ID, [targetStaging]).GetAwaiter().GetResult();

        FileTransferOperation? operation = null;
        App.SafeInvoke(() => operation = new(source, target, sourceDevice, targetDevice, isMove, App.AppDispatcher));

        // Keep UI navigation pointing at the archive member, not the temp extract path.
        if (sourceStaging is not null)
            operation!.SetArchivePullSource(archivePath!, internalPath!, sourceStaging, item.FullPath);

        if (targetStaging is not null)
            operation!.SetArchiveTarget(targetPath, targetStaging);

        return operation;
    }

    private static void CleanupStaging(LogicalDeviceViewModel device, string? stagingRoot)
    {
        if (stagingRoot is not null)
            Task.Run(() => ArchiveExtract.CleanupStaging(device.ID, stagingRoot));
    }

    private static void OnTransferFinished(FileTransferOperation op, FileOperation.OperationStatus status, string? archiveTarget)
    {
        if (status is not FileOperation.OperationStatus.Completed)
        {
            CleanupStaging(op.TargetDevice!, op.TargetStagingRoot);
            return;
        }

        if (archiveTarget is not null)
        {
            // The files wait in staging on the target; a device side paste adds them to the archive.
            var stagedType = op.FilePath.IsDirectory ? FileType.Folder : FileType.File;
            FileClass staged = new(op.FilePath.FullName, op.TargetPath.FullPath, stagedType, size: op.FilePath.Size, modifiedTime: op.FilePath.DateModified);
            var archiveOp = ShellFileOperation.PasteItemsToTar(op.TargetDevice!, [staged], archiveTarget, App.AppDispatcher);
            archiveOp?.WhenFinished(archiveStatus => OnArchiveTransferFinished(op, archiveStatus));

            return;
        }

        var targetDeviceId = op.TargetDevice!.ID;

        foreach (var (source, target) in op.TransferredItems)
        {
            // Every pane listing the target folder gets the item, if not shown yet.
            Data.ForEachListingAt(target.ParentPath, targetDeviceId, (instance, _) =>
            {
                if (instance.FileList.DirList?.FileList is { } listing && listing.All(f => f.FullName != target.FullName))
                    listing.Add(new(target) { ModifiedTime = source.DateModified });
            });

            if (source.IsDirectory)
                ExplorerTree?.AddCreatedFolder(targetDeviceId, target.FullPath);
        }

        if (op.SourceRemoved)
            RemoveTransferSourceFromListings(op);
    }

    private static void OnArchiveTransferFinished(FileTransferOperation op, FileOperation.OperationStatus status)
    {
        CleanupStaging(op.TargetDevice!, op.TargetStagingRoot);

        if (status is not FileOperation.OperationStatus.Completed
            || op.OperationName is not FileOperation.OperationType.Move
            || !op.IsFullyTransferred)
            return;

        Task.Run(() =>
        {
            op.RemoveSource();
            App.SafeBeginInvoke(() => RemoveTransferSourceFromListings(op));
        });
    }

    private static void RemoveTransferSourceFromListings(FileTransferOperation op)
    {
        foreach (var (source, _) in op.TransferredItems)
        {
            var sourcePath = source.FullPath;
            Data.ForEachListingHolding(source.ParentPath, op.Device.ID, (instance, _) =>
            {
                if (instance.FileList.DirList?.FileList is not { } listing)
                    return;

                listing.RemoveAll(f => f.FullPath == sourcePath
                    || (source.IsDirectory && FileHelper.RelationFrom(sourcePath, f.FullPath) is RelationType.Descendant));
            });

            if (source.IsDirectory)
                ExplorerTree?.RemoveDeletedFolder(op.Device.ID, sourcePath);
        }
    }

    public static void PushShellObjects(
        IEnumerable<ShellItem> items,
        string targetPath,
        DragDropEffects dropEffects = DragDropEffects.Copy,
        IReadOnlySet<string>? replacePaths = null,
        IReadOnlySet<string>? conflictPaths = null,
        LogicalDeviceViewModel? device = null)
    {
        device ??= Data.ActiveDevice;
        if (device is null)
            return;

        var pushDevice = device;
        var sources = items.Select(item => GetPushSource(item, targetPath, pushDevice.ID, replacePaths ?? EmptyPathSet, conflictPaths ?? EmptyPathSet))
                           .OfType<SyncFile>();

        QueuePushes(sources, targetPath, dropEffects, pushDevice);
    }

    /// <summary>Pushes trees of virtual files with no shell item behind them, each file read from its own content.</summary>
    public static void PushVirtualItems(
        IReadOnlyList<SyncFile> roots,
        string targetPath,
        IReadOnlySet<string> replacePaths,
        IReadOnlySet<string> conflictPaths,
        LogicalDeviceViewModel device)
    {
        var sources = roots.Where(root => FilterPushTree(root, FileHelper.ConcatPaths(targetPath, root.FullName), device.ID, replacePaths, conflictPaths));

        // The source has nothing to remove
        QueuePushes(sources, targetPath, DragDropEffects.Copy, device);
    }

    private static void QueuePushes(IEnumerable<SyncFile> sources, string targetPath, DragDropEffects dropEffects, LogicalDeviceViewModel device)
    {
        foreach (var group in GroupByParent(sources))
        {
            if (group.Count == 1)
                QueuePush(() => FileSyncOperation.PushFile(group[0], DestinationOf(group[0], targetPath), device, App.AppDispatcher), dropEffects);
            else
                QueuePush(() => FileSyncOperation.SyncBatch(FileOperation.OperationType.Push, group, new(targetPath, FileType.Folder), device, App.AppDispatcher), dropEffects);
        }
    }

    private static void OnPushFinished(FileSyncOperation op, FileOperation.OperationStatus status)
    {
        // If operation was cancelled or had failed - don't delete the source, but still perform cleanup
        if (status is FileOperation.OperationStatus.Completed)
        {
            foreach (var (source, target) in op.TransferredItems)
                OnItemPushed(op, source, target);
        }

        // Release the COM reference; op.FilePath is not read again after this cleanup.
        op.FilePath.ShellItem = null!;
    }

    private static void OnItemPushed(FileSyncOperation op, SyncFile source, SyncFile target)
    {
        // Every pane listing the folder (and device) the file was pushed to gets it, if not shown yet
        Data.ForEachListingAt(target.ParentPath, op.Device.ID, (instance, _) =>
        {
            if (instance.FileList.DirList?.FileList is { } listing && listing.All(f => f.FullName != source.FullName))
                listing.Add(new(target) { ModifiedTime = source.DateModified });
        });

        if (source.IsDirectory)
            ExplorerTree?.AddCreatedFolder(op.Device.ID, target.FullPath);

        if (source.IsDirectory
            && source.ShellItem is ShellFolder shellFolder)
        {
            var empty = FolderHelper.GetEmptySubfoldersRecursively(shellFolder);
            var parentPath = source.FullPath;
            foreach (var folder in empty)
            {
                // A folder in a zip or on a phone has no file system path
                var folderPath = string.IsNullOrEmpty(folder.FileSystemPath) ? folder.ParsingName : folder.FileSystemPath;
                if (string.IsNullOrEmpty(folderPath) || string.IsNullOrEmpty(parentPath))
                    continue;

                string relative = FileHelper.ExtractRelativePath(folderPath, parentPath).Replace('\\', '/');
                _ = ShellFileOperation.TryMakeDir(op.Device, FileHelper.ConcatPaths(target.FullPath, relative));
            }
        }

        // In push we can delete the source once the operation has completed
        if (op.DropEffects is DragDropEffects.Move)
        {
            try
            {
                if (source.IsDirectory)
                    Directory.Delete(source.FullPath, true);
                else
                    File.Delete(source.FullPath);
            }
            catch
            { }
        }
    }

    // Pull where we know the actual target path
    public static void PullFiles(string targetPath = "")
    {
        Data.RaiseFocusNavigationBox(false);

        if (ActionFlags.IsAppDrive)
        {
            PullPackages(targetPath);
            return;
        }

        // Snapshot before the folder picker — SelectedFiles is a live Where(IsSelected).
        var pullItems = Data.SelectedFiles.ToList();
        if (pullItems.Count == 0)
            return;

        if (string.IsNullOrEmpty(targetPath))
        {
            var picked = PickDestinationFolder(pullItems.Count, pullItems[0].FullName);
            if (picked is null)
                return;

            targetPath = picked;
        }

        PullFiles(targetPath, pullItems, true);
    }

    /// <summary>Asks for a folder to put <paramref name="count"/> items in; null when canceled.</summary>
    private static string? PickDestinationFolder(int count, string firstName)
    {
        var dialog = new CommonOpenFileDialog()
        {
            IsFolderPicker = true,
            Multiselect = false,
            DefaultDirectory = Data.Settings.DefaultFolder,
            Title = count > 1
                ? Strings.Resources.S_ITEM_DESTINATION_PLURAL
                : string.Format(Strings.Resources.S_ITEM_DESTINATION, firstName),
        };

        if (dialog.ShowDialog() != CommonFileDialogResult.Ok)
            return null;

        var targetPath = dialog.FileName;
        if (!Directory.Exists(targetPath) && FileHelper.GetFullName(targetPath) == firstName)
            targetPath = FileHelper.GetParentPath(targetPath);

        return targetPath;
    }

    public static async void PullFiles(string targetPath, IEnumerable<FileClass> pullItems, bool notify = false)
    {
        if (pullItems is null)
            return;

        // Materialize once — callers may pass a live selection query.
        var items = pullItems as IList<FileClass> ?? pullItems.ToList();
        if (items.Count == 0)
            return;

        var match = AdbRegEx.RE_WINDOWS_DRIVE_ROOT().Match(targetPath);
        var invalidFiles = items.Where(f => AdbExplorerConst.INVALID_WINDOWS_ROOT_PATHS.Contains(f.FullName)).ToList();

        if (match.Success && invalidFiles.Count > 0)
        {
            var result = await DialogService.ShowConfirmation(Strings.Plurals.FilesInvalidAtDriveRoot(invalidFiles.Count),
                                                 Strings.Resources.S_WIN_ROOT_ILLEGAL_TITLE,
                                                 primaryText: Strings.Resources.S_SKIP,
                                                 icon: DialogService.DialogIcon.Exclamation,
                                                 error: DialogError.WinRootIllegalPath);

            if (result.Item1 is not Wpf.Ui.Controls.ContentDialogResult.Primary)
                return;

            items = items.Except(invalidFiles).ToList();
            if (items.Count == 0)
                return;
        }

        if (!Directory.Exists(targetPath))
        {
            try
            {
                Directory.CreateDirectory(targetPath);
            }
            catch (Exception e)
            {
                DialogService.ShowMessage(e.Message,
                                          Strings.Resources.S_DEST_ERR,
                                          DialogService.DialogIcon.Critical,
                                          copyToClipboard: true,
                                          error: DialogError.DestinationPathFailed);
                return;
            }
        }

        var outcome = await CopyPasteService.MergeFiles(targetPath, items);
        items = [.. outcome.Items];
        if (items.Count == 0)
            return;

        try
        {
            var ops = await Task.Run(() => GeneratePullOps(
                targetPath,
                items,
                notify,
                replacePaths: outcome.ReplaceRelativePaths,
                conflictPaths: outcome.ConflictRelativePaths).ToList());
            Data.FileOpQ.AddOperations(ops);
        }
        catch (Exception e)
        {
            DialogService.ShowMessage(e.Message,
                                      Strings.Resources.S_DEST_ERR,
                                      DialogService.DialogIcon.Critical,
                                      copyToClipboard: true,
                                      error: DialogError.DestinationPathFailed);
        }
    }

    public static IEnumerable<FileSyncOperation> SilentPullFiles(LogicalDeviceViewModel device, string target, int maxThreads, IEnumerable<string> filesToReplace, params IEnumerable<FileClass> pullItems)
    {
        maxThreads = Math.Max(1, maxThreads);
        foreach (var item in pullItems)
        {
            if (item.Type is not FileType.Folder)
                continue;

            var syncFile = CopyPasteService.MergeFolderTree(item, target, filesToReplace);
            if (syncFile.Children.Count == 0)
                continue;

            var op = GeneratePullOp(target, syncFile, false, device);
            if (op is null)
                continue;

            op.MaxThreads = maxThreads;

            op.Start();
            yield return op;
        }
    }

    private static IEnumerable<FileSyncOperation> GeneratePullOps(
        string targetPath,
        IEnumerable<FileClass> pullItems,
        bool notify,
        LogicalDeviceViewModel? device = null,
        IReadOnlySet<string>? replacePaths = null,
        IReadOnlySet<string>? conflictPaths = null)
    {
        device ??= Data.ActiveDevice;
        var deviceId = device.ID;
        replacePaths ??= EmptyPathSet;
        conflictPaths ??= EmptyPathSet;

        List<SyncFile> plainSources = [];

        foreach (var item in pullItems)
        {
            // Archive members are each extracted to their own staging, so they aren't batched
            if (ArchivePath.TryParse(item.FullPath, out var archivePath, out var internalPath, deviceId)
                && !string.IsNullOrEmpty(internalPath))
            {
                if (GenerateArchivePullOp(targetPath, item, archivePath, internalPath, notify, device) is { } archiveOp)
                    yield return archiveOp;

                continue;
            }

            var source = item.GetSyncFile(deviceId);
            if (FilterPullTree(targetPath, source, replacePaths, conflictPaths))
                plainSources.Add(source);
        }

        foreach (var group in GroupByParent(plainSources))
        {
            FileSyncOperation op;
            if (group.Count == 1)
                op = FileSyncOperation.PullFile(group[0], SyncFile.MergeToWindowsPath(group[0], targetPath), device, App.AppDispatcher);
            else
                op = FileSyncOperation.SyncBatch(FileOperation.OperationType.Pull, group, new(targetPath, FileType.Folder) { PathType = FilePathType.Windows }, device, App.AppDispatcher);

            if (notify)
                op.WhenCompleted(() => OnPullCompleted(op));

            yield return op;
        }
    }

    private static readonly HashSet<string> EmptyPathSet = [];

    private static FileSyncOperation? GenerateArchivePullOp(
        string targetPath,
        FileClass item,
        string archivePath,
        string internalPath,
        bool notify,
        LogicalDeviceViewModel device)
    {
        string stagingRoot;
        SyncFile pullSource;
        try
        {
            (stagingRoot, pullSource) = ArchiveExtract.ExtractSelectionAsSyncFile(
                device.ID,
                archivePath,
                internalPath,
                item,
                Data.DeviceCts.Token);
        }
        catch (Exception e)
        {
#if !DEPLOY
            DebugLog.PrintLine($"Archive extract for pull failed: {e.Message}");
#endif
            throw;
        }

        var target = SyncFile.MergeToWindowsPath(pullSource, targetPath);

        if (!FileMergeHelper.FilterIdenticalPullTree(pullSource, target.FullPath))
        {
            ArchiveExtract.CleanupStaging(device.ID, stagingRoot);
            return null;
        }

        var fileOp = FileSyncOperation.PullFile(pullSource, target, device, App.AppDispatcher);

        // Keep UI navigation pointing at the archive member, not the temp extract path.
        fileOp.SetArchivePullSource(archivePath, internalPath, stagingRoot, item.FullPath);

        if (notify)
            fileOp.WhenCompleted(() => OnPullCompleted(fileOp));

        return fileOp;
    }

    private static FileSyncOperation? GeneratePullOp(
        string targetPath,
        SyncFile item,
        bool notify,
        LogicalDeviceViewModel device,
        IReadOnlySet<string>? replacePaths = null,
        IReadOnlySet<string>? conflictPaths = null)
    {
        if (!FilterPullTree(targetPath, item, replacePaths, conflictPaths))
            return null;

        var fileOp = FileSyncOperation.PullFile(item, SyncFile.MergeToWindowsPath(item, targetPath), device, App.AppDispatcher);

        if (notify)
            fileOp.WhenCompleted(() => OnPullCompleted(fileOp));

        return fileOp;
    }

    /// <summary>Drops what needn't be pulled from <paramref name="item"/>'s tree; false when nothing is left.</summary>
    private static bool FilterPullTree(
        string targetPath,
        SyncFile item,
        IReadOnlySet<string>? replacePaths,
        IReadOnlySet<string>? conflictPaths)
    {
        if (conflictPaths is { Count: > 0 })
            return FileMergeHelper.FilterSyncTreeByConflictResolution(item, item.FullName, replacePaths ?? EmptyPathSet, conflictPaths, '\\');

        return FileMergeHelper.FilterIdenticalPullTree(item, FileHelper.ConcatPaths(targetPath, item.FullName, '\\'));
    }

    private static void OnPullCompleted(FileSyncOperation op)
        => NativeMethods.RefreshExplorerDirectory(op.TargetFolderPath);
}
