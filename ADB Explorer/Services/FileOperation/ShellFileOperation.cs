namespace ADB_Explorer.Services;

public static partial class ShellFileOperation
{
    public static void DeleteItems(LogicalDeviceViewModel device, IEnumerable<FileClass> items, Dispatcher dispatcher)
    {
        var archiveGroups = new Dictionary<string, List<FileClass>>(StringComparer.Ordinal);
        var regular = new List<FileClass>();

        foreach (var item in items)
        {
            if (ArchivePath.TryParse(item.FullPath, out var archivePath, out _, device.ID)
                && ArchiveHelper.CanDeleteFromArchive(item.FullPath, device.ID))
            {
                if (!archiveGroups.TryGetValue(archivePath, out var group))
                    archiveGroups[archivePath] = group = [];

                group.Add(item);
            }
            else
            {
                regular.Add(item);
            }
        }

        foreach (var (archivePath, members) in archiveGroups)
        {
            var fileOp = FileArchiveDeleteOperation.Create(members, archivePath, device, dispatcher);
            fileOp.WhenCompleted(() => OnArchiveDeleteCompleted(fileOp));
            Data.FileOpQ.AddOperation(fileOp);
        }

        foreach (var item in regular)
        {
            var fileOp = new FileDeleteOperation(dispatcher, device, item);
            fileOp.WhenCompleted(() => OnDeleteCompleted(fileOp));

            Data.FileOpQ.AddOperation(fileOp);
        }
    }

    private static void OnArchiveDeleteCompleted(FileArchiveDeleteOperation op)
    {
        if (op.Device.ID == Data.ActiveDevice?.ID)
        {
            foreach (var member in op.Members)
                member.CutState = DragDropEffects.None;
        }

        var memberPaths = op.Members.Select(member => member.FullPath).ToHashSet();
        var removed = Data.ForEachInstance(
            instance => instance.EffectiveDevice?.ID == op.Device.ID
                && ArchivePath.TryParse(instance.FileList.Path, out var archive, out _, op.Device.ID)
                && archive == op.TarArchivePath,
            (instance, _) => instance.FileList.DirList?.FileList.RemoveAll(file => memberPaths.Contains(file.FullPath)));

        if (removed)
            FileActionLogic.UpdateFileActions();
    }

    private static void OnDeleteCompleted(FileDeleteOperation op)
    {
        // delete file trash indexer if present, even if not current device
        if (op.FilePath.TrashIndex is TrashIndexer indexer)
            SilentDelete(op.Device, indexer.IndexerPath);

        if (op.Device.ID == Data.ActiveDevice?.ID)
        {
            // remove file from cut items and clear its trash indexer if current device
            op.FilePath.CutState = DragDropEffects.None;
            op.FilePath.TrashIndex = null!;
        }

        // update every pane listing the deleted item's folder, or a search that found it or something inside it
        var deletedPath = op.FilePath.FullPath;
        var removed = Data.ForEachListingHolding(op.TargetPath.ParentPath, op.Device.ID, (instance, _) =>
            instance.FileList.DirList?.FileList.RemoveAll(file => file.FullPath == deletedPath
                || FileHelper.RelationFrom(deletedPath, file.FullPath) is AbstractFile.RelationType.Descendant));

        if (removed)
            FileActionLogic.UpdateFileActions();

        TrashHelper.SyncDriveViewTrashCountAfterDelete(op);

        if (op.FilePath.IsDirectory)
            RemoveDeletedTreeFolder(op.Device.ID, op.FilePath.FullPath);
    }

    public static void Rename(FileClass item, string targetPath, LogicalDeviceViewModel device)
    {
        var fileOp = new FileRenameOperation(item, targetPath, device, App.AppDispatcher);
        fileOp.WhenCompleted(() => OnRenameCompleted(fileOp));

        Data.FileOpQ.AddOperation(fileOp);
    }

    private static void OnRenameCompleted(FileRenameOperation op)
    {
        var oldPath = op.FilePath.FullPath;
        var newPath = op.TargetPath.FullPath;

        var renamed = Data.ForEachListingHolding(op.FilePath.ParentPath, op.Device.ID, (instance, isFocused) =>
        {
            // A search can list what is inside the renamed folder, which moves with it.
            if (op.FilePath.IsDirectory && instance.FileList.DirList?.FileList is { } listing)
            {
                foreach (var inner in listing.Where(f => FileHelper.RelationFrom(oldPath, f.FullPath) is AbstractFile.RelationType.Descendant).ToList())
                    inner.UpdatePath(newPath + inner.FullPath[oldPath.Length..]);
            }

            var file = instance.FileList.DirList?.FileList?.Find(f => f.FullPath == oldPath);
            if (file is null)
                return;

            file.UpdatePath(newPath);

            if (!StillMatchesSearch(instance, file))
            {
                instance.FileList.DirList!.FileList.Remove(file);
                return;
            }

            // Only the focused pane keeps a selection.
            if (!isFocused)
                return;

            if (Data.SelectedFiles.Count() == 1 && Data.SelectedFiles.First() == file)
                Data.ItemToSelect.Value = null;

            if (Data.FileOpQ.TotalCount == 1)
                Data.ItemToSelect.Value = file;
        });

        if (renamed)
            FileActionLogic.UpdateFileActions();

        if (op.FilePath.IsDirectory)
            RenameTreeFolder(op.Device.ID, oldPath, newPath);
    }

    /// <summary>
    /// Whether a renamed item still belongs in its pane's search results. With content search on, a
    /// file may have been found by its contents, which a rename doesn't change, so it is kept.
    /// </summary>
    private static bool StillMatchesSearch(ExplorerInstance instance, FileClass file)
    {
        if (!instance.FileList.Actions.IsSearchMode)
            return true;

        var query = instance.FileList.Actions.ExplorerFilter?.Trim();
        if (string.IsNullOrEmpty(query))
            return true;

        if (Data.Settings.SearchContents && file.Type is AbstractFile.FileType.File)
            return true;

        return FileHelper.MatchesSearchQuery(FileHelper.GetFullName(file.FullPath), query);
    }

    public static void MoveItems(LogicalDeviceViewModel device,
                                 IEnumerable<FileClass> items,
                                 string? targetPath,
                                 string currentPath,
                                 ObservableList<FileClass> fileList,
                                 Dispatcher dispatcher,
                                 DragDropEffects cutType = DragDropEffects.None)
        // fileList is only used for same-folder copy-paste rename collisions; it's null when
        // called from a context (e.g. a tree node delete/recycle) with no live directory listing.
        => MoveItems(device,
                     items,
                     targetPath,
                     currentPath,
                     fileList?.Select(f => f.FullName) ?? [],
                     dispatcher,
                     cutType);

    public static void MoveItems(LogicalDeviceViewModel device,
                                 IEnumerable<FileClass> items,
                                 string? targetPath,
                                 string currentPath,
                                 IEnumerable<string> existingItems,
                                 Dispatcher dispatcher,
                                 DragDropEffects cutType = DragDropEffects.None)
    {
        // Only reached when targetPath == RECYCLE_PATH (see dispatch below), so never null here.
        IEnumerable<FileMoveOperation> Recycle()
        {
            foreach (var item in items)
            {
                SyncFile target = new(FileHelper.ConcatPaths(targetPath!, item.FullName), item.Type);
                yield return new(item, target, device, dispatcher);
            }
        }

        IEnumerable<FileMoveOperation> Restore()
        {
            if (Data.RecycleIndex.Count == 0)
                TrashHelper.ParseIndexers();

            foreach (var item in items)
            {
                if (item.Extension == AdbExplorerConst.RECYCLE_INDEX_SUFFIX)
                    continue;

                var recycleName = item.TrashIndex is null
                    ? item.FullName
                    : FileHelper.GetFullName(item.TrashIndex.RecycleName);

                var indexer = Data.RecycleIndex.FirstOrDefault(f => f.MatchesRecycleFile(recycleName));
                if (indexer is null)
                    continue;

                item.UpdatePath(FileHelper.ConcatPaths(AdbExplorerConst.RECYCLE_PATH, recycleName));
                item.TrashIndex = indexer;
                var targetParent = string.IsNullOrEmpty(targetPath)
                    ? indexer.ParentPath
                    : targetPath;

                SyncFile target = new(FileHelper.ConcatPaths(targetParent, item.FullName));
                yield return new(item, target, device, dispatcher);
            }
        }

        // Only reached when targetPath is a real destination (see dispatch below), so never null here.
        IEnumerable<FileMoveOperation> Move()
        {
            foreach (var item in items)
            {
                var targetName = item.FullName;
                // Same-folder self-copy (Ctrl+C / Ctrl+V in place) gets a unique " - Copy" name.
                // Paste from elsewhere already went through MergeFiles (replace / skip).
                if (cutType is DragDropEffects.Copy && item.ParentPath == targetPath)
                    targetName = FileHelper.DuplicateFile(existingItems, targetName, cutType);

                SyncFile target = new(FileHelper.ConcatPaths(targetPath!, targetName));
                yield return new(item, target, device, dispatcher, cutType);
            }
        }

        List<FileMoveOperation> fileops = [];
        items = [.. items];

        if (items.First().ParentPath == AdbExplorerConst.RECYCLE_PATH || currentPath == AdbExplorerConst.RECYCLE_PATH)
            fileops = [.. Restore()];
        else if (targetPath == AdbExplorerConst.RECYCLE_PATH)
            fileops = [.. Recycle()];
        else
            fileops = [.. Move()];

        dispatcher.Invoke(() =>
        {
            fileops.ForEach(op => op.WhenCompleted(() => OnMoveCompleted(op)));
            Data.FileOpQ.AddOperations(fileops);
        });
    }

    private static void OnMoveCompleted(FileMoveOperation op)
    {
        // write or delete indexer, even if not current device
        if (op.OperationName is FileOperation.OperationType.Recycle)
        {
            TrashIndexer indexer = new(op);
            WriteLine(op.Device, op.IndexerPath, AdbService.EscapeAdbShellString(indexer.ToString()));

            if (TrashHelper.GetTrashDrive(op.Device) is { } trash)
            {
                var baseline = trash.ItemsCount is null or <= 0 ? 0 : trash.ItemsCount.Value;
                trash.SetItemsCount(baseline + 1);
            }
        }
        else if (op.OperationName is FileOperation.OperationType.Restore)
        {
            SilentDelete(op.Device, op.IndexerPath);
        }

        // remove file from cut items
        op.FilePath.CutState = DragDropEffects.None;

        var sourcePath = op.FilePath.FullPath;
        var removeFromTree = op.OperationName is FileOperation.OperationType.Recycle or FileOperation.OperationType.Move
            && op.FilePath.IsDirectory;

        if (op.Device.ID == Data.ActiveDevice?.ID)
        {
            // clear file trash indexer if restore / recycle on current device
            if (op.OperationName is FileOperation.OperationType.Recycle or FileOperation.OperationType.Restore)
            {
                op.FilePath.TrashIndex = null!;
            }
        }

        // Every pane listing the source or the target folder is updated, not only the focused one.
        var sourceParent = op.FilePath.ParentPath;
        var targetParent = op.TargetPath.ParentPath;
        var isMoved = op.OperationName is not FileOperation.OperationType.Copy;
        var listingsChanged = false;

        // update UI when cut / restore / recycle source is listed
        if (isMoved && sourceParent != targetParent)
        {
            listingsChanged |= Data.ForEachListingHolding(sourceParent, op.Device.ID, (instance, _) =>
            {
                if (instance.FileList.DirList?.FileList is not { } listing)
                    return;

                var listed = listing.Find(f => f.FullPath == sourcePath) ?? op.FilePath;
                listing.Remove(listed);

                if (op.FilePath.IsDirectory)
                    listing.RemoveAll(f => FileHelper.RelationFrom(sourcePath, f.FullPath) is AbstractFile.RelationType.Descendant);
            });
        }

        // update UI when copy / cut target is listed; the moved item itself goes to the first pane, the rest get copies
        var movedItemUsed = false;
        listingsChanged |= Data.ForEachListingAt(targetParent, op.Device.ID, (instance, isFocused) =>
        {
            if (instance.FileList.DirList?.FileList is not { } listing)
                return;

            FileClass added;
            if (isMoved && !movedItemUsed)
            {
                movedItemUsed = true;
                op.FilePath.UpdatePath(op.TargetPath.FullPath);
                added = op.FilePath;
            }
            else
            {
                added = new(op.FilePath) { IsLink = op.isLink };
                added.UpdatePath(op.TargetPath.FullPath);
                added.ModifiedTime = op.DateModified;
            }

            listing.Add(added);

            // only select the item in the focused pane, and only if there aren't any other operations
            if (isFocused && Data.FileOpQ.TotalCount == 1)
                Data.ItemToSelect.Value = added;
        });

        if (listingsChanged)
            FileActionLogic.UpdateFileActions();

        if (removeFromTree)
            RemoveDeletedTreeFolder(op.Device.ID, sourcePath);

        // A move/copy that lands a folder in a new location needs the same tree update a push gets:
        // add it under its (already loaded) destination parent, if that parent is visible in the tree.
        if (op.FilePath.IsDirectory && op.OperationName is FileOperation.OperationType.Move or FileOperation.OperationType.Copy)
            AddCreatedTreeFolder(op.Device.ID, op.TargetPath.FullPath);
    }

    public static void ChangeDateFromName(LogicalDeviceViewModel device, IEnumerable<FileClass> items, Dispatcher dispatcher)
    {
        List<FileChangeModifiedOperation> operations = [];

        foreach (var item in items)
        {
            var match = AdbRegEx.RE_FILE_NAME_DATE().Match(item.FullName);
            if (!match.Success)
                continue;

            DateTime nameDate = DateTime.MinValue;
            var date = match.Groups["Date"].Value;
            var time = match.Groups["Time"].Value;
            var dateTime = match.Groups["DnT"].Value;

            if (DateOnly.TryParseExact(date, "yyyyMMdd", null, DateTimeStyles.None, out DateOnly res))
            {
                nameDate = res.ToDateTime(TimeOnly.MinValue);
                if (TimeOnly.TryParseExact(time, "HHmmss", null, DateTimeStyles.None, out TimeOnly timeRes))
                    nameDate = res.ToDateTime(timeRes);
            }
            else if (DateTime.TryParseExact(dateTime, "yyyy-MM-dd-HH-mm-ss", null, DateTimeStyles.None, out DateTime dntRes))
            {
                nameDate = dntRes;
            }
            else
                continue;

            if (item.ModifiedTime is DateTime modified && modified > nameDate)
            {
                operations.Add(new FileChangeModifiedOperation(item, nameDate, device, dispatcher));
            }
        }

        operations.ForEach(op => op.WhenCompleted(() => OnChangeModifiedCompleted(op)));
        Data.FileOpQ.AddOperations(operations);
    }

    private static void OnChangeModifiedCompleted(FileChangeModifiedOperation op)
    {
        // update every pane listing the file's folder
        op.FilePath.ModifiedTime = op.NewDate;
        Data.ForEachListingAt(op.FilePath.ParentPath, op.Device.ID, (instance, _) =>
        {
            if (instance.FileList.DirList?.FileList?.Find(f => f.FullPath == op.FilePath.FullPath) is { } listed)
                listed.ModifiedTime = op.NewDate;
        });
    }

    private static void RemoveDeletedTreeFolder(string deviceId, string path)
    {
        App.Services.GetService<ExplorerViewModel>()?.Tree?.RemoveDeletedFolder(deviceId, path);
    }

    private static void AddCreatedTreeFolder(string deviceId, string path)
    {
        App.Services.GetService<ExplorerViewModel>()?.Tree?.AddCreatedFolder(deviceId, path);
    }

    private static void RenameTreeFolder(string deviceId, string oldPath, string newPath)
    {
        App.Services.GetService<ExplorerViewModel>()?.Tree?.RenameFolder(deviceId, oldPath, newPath);
    }
}
