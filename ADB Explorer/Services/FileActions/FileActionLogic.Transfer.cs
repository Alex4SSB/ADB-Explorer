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

        FileSyncOperation pushOperation = null;
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

        string androidDest = FileHelper.ConcatPaths(targetPath, source.FullName);
        var target = new SyncFile(androidDest,
            source.IsDirectory ? FileType.Folder : FileType.File)
            { Size = source.Size };

        device ??= Data.ActiveDevice;
        if (device is null)
            return null;

        replacePaths ??= EmptyPathSet;
        conflictPaths ??= EmptyPathSet;

        if (conflictPaths.Count > 0)
        {
            if (!FileMergeHelper.FilterSyncTreeByConflictResolution(
                    source,
                    source.FullName,
                    replacePaths,
                    conflictPaths,
                    '/'))
            {
                return null;
            }
        }
        else if (!FileMergeHelper.FilterIdenticalPushTree(source, androidDest, device.ID))
        {
            return null;
        }

        var pushDevice = device;
        App.SafeInvoke(() =>
        {
            pushOperation = FileSyncOperation.PushFile(source, target, pushDevice, App.AppDispatcher);
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

        foreach (var item in items)
        {
            var operation = CreateTransferOp(item, targetPath, isMove, sourceDevice, targetDevice, intoArchive, replacePaths, conflictPaths);
            if (operation is null)
                continue;

            var archiveTarget = intoArchive ? targetPath : null;
            operation.WhenFinished(status => OnTransferFinished(operation, status, archiveTarget));
            operations.Add(operation);
        }

        if (operations.Count > 0)
            App.SafeInvoke(() => Data.FileOpQ.AddOperations(operations));
    }

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

        var targetDest = FileHelper.ConcatPaths(destFolder, source.FullName);
        var target = new SyncFile(targetDest, source.IsDirectory ? FileType.Folder : FileType.File)
            { Size = source.Size };

        bool anythingToTransfer;
        if (conflictPaths.Count > 0)
            anythingToTransfer = FileMergeHelper.FilterSyncTreeByConflictResolution(source, source.FullName, replacePaths, conflictPaths, '/');
        else
            anythingToTransfer = FileMergeHelper.FilterIdenticalPushTree(source, targetDest, targetDevice.ID);

        if (!anythingToTransfer)
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
        var name = op.TargetPath.FullName;

        // Every pane listing the target folder gets the item, if not shown yet.
        Data.ForEachListingAt(op.TargetPath.ParentPath, targetDeviceId, (instance, _) =>
        {
            if (instance.FileList.DirList?.FileList is { } listing && listing.All(f => f.FullName != name))
                listing.Add(new(op.TargetPath) { ModifiedTime = op.FilePath.DateModified });
        });

        if (op.FilePath.IsDirectory)
            ExplorerTree?.AddCreatedFolder(targetDeviceId, op.TargetPath.FullPath);

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
        var sourcePath = op.FilePath.FullPath;
        Data.ForEachListingHolding(op.FilePath.ParentPath, op.Device.ID, (instance, _) =>
        {
            if (instance.FileList.DirList?.FileList is not { } listing)
                return;

            listing.RemoveAll(f => f.FullPath == sourcePath
                || (op.FilePath.IsDirectory && FileHelper.RelationFrom(sourcePath, f.FullPath) is RelationType.Descendant));
        });

        if (op.FilePath.IsDirectory)
            ExplorerTree?.RemoveDeletedFolder(op.Device.ID, sourcePath);
    }

    public static void PushShellObjects(
        IEnumerable<ShellItem> items,
        string targetPath,
        DragDropEffects dropEffects = DragDropEffects.Copy,
        IReadOnlySet<string>? replacePaths = null,
        IReadOnlySet<string>? conflictPaths = null,
        LogicalDeviceViewModel? device = null)
        => items.ForEach(item => PushShellObject(item, targetPath, dropEffects, replacePaths: replacePaths, conflictPaths: conflictPaths, device: device));

    private static void OnPushFinished(FileSyncOperation op, FileOperation.OperationStatus status)
    {
        // If operation was cancelled or had failed - don't delete the source, but still perform cleanup
        if (status is FileOperation.OperationStatus.Completed)
        {
            // Every pane listing the folder (and device) the file was pushed to gets it, if not shown yet
            Data.ForEachListingAt(op.TargetPath.ParentPath, op.Device.ID, (instance, _) =>
            {
                if (instance.FileList.DirList?.FileList is { } listing && listing.All(f => f.FullName != op.FilePath.FullName))
                    listing.Add(new(op.TargetPath) { ModifiedTime = op.FilePath.DateModified });
            });

            if (op.FilePath.IsDirectory)
                ExplorerTree?.AddCreatedFolder(op.Device.ID, op.TargetPath.FullPath);

            if (op.FilePath.IsDirectory
                && op.FilePath.ShellItem is ShellFolder shellFolder)
            {
                var empty = FolderHelper.GetEmptySubfoldersRecursively(shellFolder);
                var parentPath = op.FilePath?.FullPath;
                foreach (var folder in empty)
                {
                    if (string.IsNullOrEmpty(folder.FileSystemPath) || string.IsNullOrEmpty(parentPath))
                        continue;

                    string relative = FileHelper.ExtractRelativePath(folder.FileSystemPath, parentPath).Replace('\\', '/');
                    _ = ShellFileOperation.TryMakeDir(op.Device, FileHelper.ConcatPaths(op.TargetPath.FullPath, relative));
                }
            }

            // In push we can delete the source once the operation has completed
            if (op.DropEffects is DragDropEffects.Move)
            {
                try
                {
                    if (op.FilePath.IsDirectory)
                        Directory.Delete(op.FilePath.FullPath, true);
                    else
                        File.Delete(op.FilePath.FullPath);
                }
                catch
                { }
            }
        }

        // Release the COM reference; op.FilePath is not read again after this cleanup.
        op.FilePath.ShellItem = null!;
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
            var result = await DialogService.ShowConfirmation(string.Format(Strings.Resources.S_WIN_ROOT_ILLEGAL, invalidFiles.Count),
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

        foreach (var item in pullItems)
        {
            FileSyncOperation? op;
            if (ArchivePath.TryParse(item.FullPath, out var archivePath, out var internalPath, deviceId)
                && !string.IsNullOrEmpty(internalPath))
            {
                op = GenerateArchivePullOp(targetPath, item, archivePath, internalPath, notify, device);
            }
            else
            {
                op = GeneratePullOp(targetPath, item.GetSyncFile(deviceId), notify, device, replacePaths, conflictPaths);
            }

            if (op is not null)
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
        var target = SyncFile.MergeToWindowsPath(item, targetPath);

        if (conflictPaths is { Count: > 0 })
        {
            if (!FileMergeHelper.FilterSyncTreeByConflictResolution(
                    item,
                    item.FullName,
                    replacePaths ?? EmptyPathSet,
                    conflictPaths,
                    '\\'))
            {
                return null;
            }
        }
        else if (!FileMergeHelper.FilterIdenticalPullTree(item, target.FullPath))
        {
            return null;
        }

        var fileOp = FileSyncOperation.PullFile(item, target, device, App.AppDispatcher);

        if (notify)
            fileOp.WhenCompleted(() => OnPullCompleted(fileOp));

        return fileOp;
    }

    private static void OnPullCompleted(FileSyncOperation op)
        => NativeMethods.RefreshExplorerDirectory(op.TargetPath.ParentPath);
}
