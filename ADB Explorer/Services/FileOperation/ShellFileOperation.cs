using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services.AppInfra;
using ADB_Explorer.ViewModels;
using ADB_Explorer.ViewModels.Pages;
using AdvancedSharpAdbClient;
using AdvancedSharpAdbClient.Models;
using Vanara.Windows.Shell;

namespace ADB_Explorer.Services;

public abstract class AbstractShellFileOperation : FileOperation
{
    public override FileClass FilePath { get; }

    public override SyncFile AndroidPath => TargetPath;

    protected AbstractShellFileOperation(FileClass filePath, LogicalDeviceViewModel device, Dispatcher dispatcher)
        : base(filePath, device, dispatcher)
    {
        if (filePath is not null)
        {
            FilePath = filePath;
            TargetPath = new(filePath);
        }
    }

    public override void ClearChildren()
    {
        if (AndroidPath is null)
            return;

        AndroidPath.Children.Clear();
        AndroidPath.ProgressUpdates.Clear();
    }

    public override void AddUpdates(IEnumerable<FileOpProgressInfo> newUpdates)
        => AndroidPath?.AddUpdates(newUpdates);

    public override void AddUpdates(params FileOpProgressInfo[] newUpdates)
        => AndroidPath?.AddUpdates(newUpdates);
}

public static class ShellFileOperation
{
    public static void SilentDelete(LogicalDeviceViewModel device, IEnumerable<FilePath> items)
        => SilentDelete(device, items.Select(item => item.FullPath).ToArray());

    public static void SilentDelete(LogicalDeviceViewModel device, params string[] items)
    {
        string[] args = ["-rf", .. items.Select(item => ADBService.EscapeAdbShellString(item))];
        ADBService.ExecuteDeviceAdbShellCommand(device.ID, "rm", out _, out _, CancellationToken.None, args);
    }

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
            fileOp.PropertyChanged += ArchiveDeleteOp_PropertyChanged;
            Data.FileOpQ.AddOperation(fileOp);
        }

        foreach (var item in regular)
        {
            var fileOp = new FileDeleteOperation(dispatcher, device, item);
            fileOp.PropertyChanged += DeleteFileOp_PropertyChanged;

            Data.FileOpQ.AddOperation(fileOp);
        }
    }

    private static void ArchiveDeleteOp_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (sender is not FileArchiveDeleteOperation op)
            return;

        if (e.PropertyName is not nameof(FileOperation.Status)
            || op.Status is not FileOperation.OperationStatus.Completed)
            return;

        if (op.Device.ID == Data.DevicesObject.Current?.ID)
        {
            foreach (var member in op.Members)
                member.CutState = DragDropEffects.None;

            if (ArchivePath.TryParse(Data.CurrentPath, out var currentArchive, out _, op.Device.ID)
                && currentArchive == op.TarArchivePath)
            {
                foreach (var member in op.Members)
                    Data.DirList.FileList.Remove(member);

                FileActionLogic.UpdateFileActions();
            }
        }

        op.PropertyChanged -= ArchiveDeleteOp_PropertyChanged;
    }

    private static void DeleteFileOp_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        var op = sender as FileDeleteOperation;

        // when operation completes, remove this event handler anyway
        if (e.PropertyName is not nameof(FileOperation.Status) || op.Status is not FileOperation.OperationStatus.Completed)
            return;

        // delete file trash indexer if present, even if not current device
        if (op.FilePath.TrashIndex is TrashIndexer indexer)
            SilentDelete(op.Device, indexer.IndexerPath);

        if (op.Device.ID == Data.DevicesObject.Current?.ID)
        {
            // remove file from cut items and clear its trash indexer if current device
            op.FilePath.CutState = DragDropEffects.None;
            op.FilePath.TrashIndex = null;

            // update UI if current path
            if (op.TargetPath.ParentPath == Data.CurrentPath)
            {
                Data.Files.DirList?.FileList.Remove(op.FilePath);
                FileActionLogic.UpdateFileActions();
            }
        }

        TrashHelper.SyncDriveViewTrashCountAfterDelete(op);

        if (op.FilePath.IsDirectory)
            RemoveDeletedTreeFolder(op.Device.ID, op.FilePath.FullPath);

        op.PropertyChanged -= DeleteFileOp_PropertyChanged;
    }

    public static void Rename(FileClass item, string targetPath, LogicalDeviceViewModel device)
    {
        var fileOp = new FileRenameOperation(item, targetPath, device, App.AppDispatcher);
        fileOp.PropertyChanged += RenameFileOp_PropertyChanged;

        Data.FileOpQ.AddOperation(fileOp);
    }

    private static void RenameFileOp_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        var op = sender as FileRenameOperation;

        // when operation completes, remove this event handler anyway
        if (e.PropertyName is not nameof(FileOperation.Status) || op.Status is not FileOperation.OperationStatus.Completed)
            return;

        var oldPath = op.FilePath.FullPath;
        var newPath = op.TargetPath.FullPath;

        if (op.Device.ID == Data.DevicesObject.Current?.ID
            && op.FilePath.ParentPath == Data.CurrentPath)
        {
            var file = Data.Files.DirList?.FileList?.Find(f => f.FullPath == oldPath);

            if (file is not null)
            {
                op.Dispatcher.Invoke(() =>
                {
                    file.UpdatePath(newPath);
                    FileActionLogic.UpdateFileActions();
                });

                if (Data.SelectedFiles.Count() == 1 && Data.SelectedFiles.First() == file)
                    Data.ItemToSelect.Value = null;

                if (Data.FileOpQ.TotalCount == 1)
                    Data.ItemToSelect.Value = file;
            }
        }

        if (op.FilePath.IsDirectory)
            RenameTreeFolder(op.Device.ID, oldPath, newPath);

        op.PropertyChanged -= RenameFileOp_PropertyChanged;
    }

    public static bool SilentCopy(LogicalDeviceViewModel device, string fullPath, string targetPath, out string stderr, bool throwOnError = false)
    {
        var exitCode = ADBService.ExecuteDeviceAdbShellCommand(device.ID,
                                                               "cp",
                                                               out _,
                                                               out stderr,
                                                               CancellationToken.None,
                                                               "-p",
                                                               ADBService.EscapeAdbShellString(fullPath),
                                                               ADBService.EscapeAdbShellString(targetPath));

        if (exitCode != 0 && throwOnError)
            throw new Exception(stderr);

        return exitCode == 0;
    }

    public static bool SilentCopy(LogicalDeviceViewModel device, string fullPath, string targetPath, bool throwOnError = false)
        => SilentCopy(device, fullPath, targetPath, out _, throwOnError);

    public static bool SilentMove(LogicalDeviceViewModel device, FilePath item, string targetPath) => SilentMove(device, item.FullPath, targetPath);

    public static bool SilentMove(LogicalDeviceViewModel device, string fullPath, string targetPath, bool throwOnError = true)
    {
        var exitCode = ADBService.ExecuteDeviceAdbShellCommand(device.ID,
                                                               "mv",
                                                               out _,
                                                               out var stderr,
                                                               CancellationToken.None,
                                                               ADBService.EscapeAdbShellString(fullPath),
                                                               ADBService.EscapeAdbShellString(targetPath));

        if (exitCode != 0 && throwOnError)
        {
            throw new Exception(stderr);
        }

        return exitCode == 0;
    }

    /// <summary>
    /// Pushes a Windows file or folder tree to <paramref name="androidDestPath"/> via AdvancedSharpAdbClient sync
    /// (no classic <c>adb push</c>).
    /// </summary>
    public static void SilentPush(
        LogicalDeviceViewModel device,
        ShellItem windowsItem,
        string androidDestPath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(windowsItem.ParsingName) && !Directory.Exists(windowsItem.ParsingName))
            throw new FileNotFoundException(Strings.Resources.S_SYNC_FILE_NOT_FOUND, windowsItem.ParsingName);
        
        var source = new SyncFile(windowsItem, includeContent: true);
        try
        {
            IEnumerable<SyncFile> files = [source, .. source.AllChildren()];

            if (source.IsDirectory)
            {
                var dirPaths = FolderHelper.GetBottomMostFolders(files)
                    .Select(f => FileHelper.ConcatPaths(
                        androidDestPath,
                        FileHelper.ExtractRelativePath(f.FullPath, source.FullPath, false)));

                MakeDirs(device.ID, dirPaths).GetAwaiter().GetResult();
            }

            UnixFileStatus fileMode = UnixFileStatus.AllPermissions | UnixFileStatus.Regular;
            var useSyncV2 = device.SupportsSyncV2;
            var isCanceled = false;
            using var cancelReg = cancellationToken.Register(() => isCanceled = true);

            foreach (var item in files.Where(f => !f.IsDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var targetPath = source.IsDirectory
                    ? FileHelper.ConcatPaths(androidDestPath, FileHelper.ExtractRelativePath(item.FullPath, source.FullPath))
                    : androidDestPath;

                using SyncService service = new(device.DeviceData);
                using var stream = new FileStream(item.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read);

                var lastWriteTime = item.DateModified ?? DateTime.Now;
                service.Push(stream, targetPath, fileMode, lastWriteTime, _ => { }, useSyncV2, in isCanceled);

                SyncTransferTracker.AddPushBytes(stream.Length);
            }
        }
        finally
        {
            source.ClearAll();
        }
    }

    public static void MoveItems(LogicalDeviceViewModel device,
                                 IEnumerable<FileClass> items,
                                 string targetPath,
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

    /// <summary>
    /// Extracts archive selections to <paramref name="targetPath"/> (device paste from archive clipboard).
    /// Caller must have already resolved name conflicts (merge/replace/skip); existing targets are replaced.
    /// </summary>
    public static void ExtractItems(LogicalDeviceViewModel device,
                                    IEnumerable<FileClass> items,
                                    string targetPath,
                                    Dispatcher dispatcher,
                                    int masterPid = 0)
    {
        items = [.. items];
        List<FileExtractOperation> fileops = [];

        foreach (var item in items)
        {
            if (!ArchivePath.TryParse(item.FullPath, out _, out _, device.ID))
                continue;

            SyncFile target = new(FileHelper.ConcatPaths(targetPath, item.FullName), item.Type);
            fileops.Add(new(item, target, device, dispatcher) { MasterPid = masterPid });
        }

        if (fileops.Count == 0)
            return;

        dispatcher.Invoke(() =>
        {
            fileops.ForEach(op => op.PropertyChanged += ExtractFileOp_PropertyChanged);
            Data.FileOpQ.AddOperations(fileops);
        });
    }

    /// <summary>
    /// Pastes device files into a modifiable tar archive (extract + overlay + repack).
    /// </summary>
    public static void PasteItemsToTar(
        LogicalDeviceViewModel device,
        IEnumerable<FileClass> items,
        string archiveTargetComposite,
        Dispatcher dispatcher,
        DragDropEffects cutType = DragDropEffects.Copy)
    {
        List<FileClass> list = [.. items];
        if (list.Count == 0)
            return;

        var op = FileArchiveModifyOperation.FromDevicePaste(list, archiveTargetComposite, device, dispatcher, cutType);
        dispatcher.Invoke(() =>
        {
            op.PropertyChanged += ArchiveModifyOp_PropertyChanged;
            Data.FileOpQ.AddOperation(op);
        });
    }

    /// <summary>
    /// Pushes Windows items into a modifiable tar archive (extract + push overlay + repack).
    /// </summary>
    public static void PushItemsToTar(
        LogicalDeviceViewModel device,
        IEnumerable<ShellItem> items,
        string archiveTargetComposite,
        Dispatcher dispatcher)
    {
        List<ShellItem> list = [.. items];
        if (list.Count == 0)
            return;

        var op = FileArchiveModifyOperation.FromWindowsPush(list, archiveTargetComposite, device, dispatcher);
        dispatcher.Invoke(() =>
        {
            op.PropertyChanged += ArchiveModifyOp_PropertyChanged;
            Data.FileOpQ.AddOperation(op);
        });
    }

    private static void ArchiveModifyOp_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (sender is not FileArchiveModifyOperation op)
            return;

        if (e.PropertyName is not nameof(FileOperation.Status)
            || op.Status is not FileOperation.OperationStatus.Completed)
            return;

        foreach (var src in op.DeviceSources)
            src.CutState = DragDropEffects.None;

        if (op.Device.ID == Data.DevicesObject.Current?.ID
            && ArchivePath.TryParse(Data.CurrentPath, out var currentArchive, out _, op.Device.ID)
            && currentArchive == op.TarArchivePath)
        {
            Data.RuntimeSettings.Refresh = true;
            FileActionLogic.UpdateFileActions();
        }

        op.PropertyChanged -= ArchiveModifyOp_PropertyChanged;
    }

    private static void ExtractFileOp_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (sender is not FileExtractOperation op)
            return;

        if (e.PropertyName is not nameof(FileOperation.Status)
            || op.Status is not FileOperation.OperationStatus.Completed)
            return;

        op.FilePath.CutState = DragDropEffects.None;

        if (op.Device.ID == Data.DevicesObject.Current.ID
            && op.TargetPath.ParentPath == Data.CurrentPath)
        {
            FileClass newFile = new(op.FilePath);
            newFile.UpdatePath(op.TargetPath.FullPath);
            Data.DirList.FileList.Add(newFile);

            if (Data.FileOpQ.TotalCount == 1)
                Data.ItemToSelect.Value = newFile;

            FileActionLogic.UpdateFileActions();
        }

        op.PropertyChanged -= ExtractFileOp_PropertyChanged;
    }

    /// <summary>
    /// Creates a tar-family archive at <paramref name="archiveFile"/> from <paramref name="sourcePaths"/>.
    /// </summary>
    public static void CompressArchive(
        LogicalDeviceViewModel device,
        FileClass archiveFile,
        IReadOnlyList<string> sourcePaths,
        Dispatcher dispatcher)
    {
        var op = new FileCompressOperation(archiveFile, sourcePaths, device, dispatcher);
        dispatcher.Invoke(() =>
        {
            op.PropertyChanged += CompressOp_PropertyChanged;
            Data.FileOpQ.AddOperation(op);
        });
    }

    private static void CompressOp_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (sender is not FileCompressOperation op)
            return;

        if (e.PropertyName is not nameof(FileOperation.Status))
            return;

        if (op.Status is FileOperation.OperationStatus.Completed)
        {
            if (op.Device.ID == Data.DevicesObject.Current?.ID
                && op.FilePath.ParentPath == Data.CurrentPath)
            {
                op.FilePath.UpdateType();
                FileActionLogic.UpdateFileActions();
                _ = op.FilePath.UpdateExtraInfoAsync(CancellationToken.None);
            }

            op.PropertyChanged -= CompressOp_PropertyChanged;
            return;
        }

        if (op.Status is FileOperation.OperationStatus.Failed or FileOperation.OperationStatus.Canceled)
        {
            if (op.Device.ID == Data.DevicesObject.Current?.ID
                && op.FilePath.ParentPath == Data.CurrentPath)
            {
                Data.DirList.FileList.Remove(op.FilePath);
                FileActionLogic.UpdateFileActions();
            }

            op.PropertyChanged -= CompressOp_PropertyChanged;
        }
    }

    public static void MoveItems(LogicalDeviceViewModel device,
                                 IEnumerable<FileClass> items,
                                 string targetPath,
                                 string currentPath,
                                 IEnumerable<string> existingItems,
                                 Dispatcher dispatcher,
                                 DragDropEffects cutType = DragDropEffects.None,
                                 int masterPid = 0)
    {
        IEnumerable<FileMoveOperation> Recycle()
        {
            foreach (var item in items)
            {
                SyncFile target = new(FileHelper.ConcatPaths(targetPath, item.FullName), item.Type);
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

        IEnumerable<FileMoveOperation> Move()
        {
            foreach (var item in items)
            {
                var targetName = item.FullName;
                // Same-folder self-copy (Ctrl+C / Ctrl+V in place) gets a unique " - Copy" name.
                // Paste from elsewhere already went through MergeFiles (replace / skip).
                if (cutType is DragDropEffects.Copy && item.ParentPath == targetPath)
                    targetName = FileHelper.DuplicateFile(existingItems, targetName, cutType);

                SyncFile target = new(FileHelper.ConcatPaths(targetPath, targetName));
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

        fileops.ForEach(op => op.MasterPid = masterPid);

        dispatcher.Invoke(() =>
        {
            fileops.ForEach(op => op.PropertyChanged += MoveFileOp_PropertyChanged);
            Data.FileOpQ.AddOperations(fileops);
        });
    }

    private static void MoveFileOp_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        var op = sender as FileMoveOperation;

        // when operation completes, remove this event handler anyway
        if (e.PropertyName is nameof(FileOperation.Status)
            && op.Status is FileOperation.OperationStatus.Completed)
        {
            // write or delete indexer, even if not current device
            if (op.OperationName is FileOperation.OperationType.Recycle)
            {
                TrashIndexer indexer = new(op);
                WriteLine(op.Device, op.IndexerPath, ADBService.EscapeAdbShellString(indexer.ToString()));
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

            if (op.Device.ID == Data.DevicesObject.Current?.ID)
            {
                // notify master process of completion
                if (op.MasterPid > 0 && op.OperationName is not FileOperation.OperationType.Copy)
                {
                    IpcService.NotifyFileMoved(op.MasterPid, op.Device, op.FilePath);
                }

                // clear file trash indexer if restore / recycle on current device
                if (op.OperationName is FileOperation.OperationType.Recycle or FileOperation.OperationType.Restore)
                {
                    op.FilePath.TrashIndex = null;
                }

                var listing = Data.Files.DirList?.FileList;

                // update UI when copy / cut target is current path
                if (listing is not null && op.TargetPath.ParentPath == Data.CurrentPath)
                {
                    if (op.OperationName is FileOperation.OperationType.Copy)
                    {
                        FileClass newFile = new(op.FilePath)
                        {
                            IsLink = op.isLink
                        };
                        newFile.UpdatePath(op.TargetPath.FullPath);
                        newFile.ModifiedTime = op.DateModified;
                        
                        listing.Add(newFile);

                        // only select the item if there aren't any other operations
                        if (Data.FileOpQ.TotalCount == 1)
                            Data.ItemToSelect.Value = newFile;
                    }
                    else
                    {
                        op.FilePath.UpdatePath(op.TargetPath.FullPath);
                        listing.Add(op.FilePath);

                        // only select the item if there aren't any other operations
                        if (Data.FileOpQ.TotalCount == 1)
                            Data.ItemToSelect.Value = op.FilePath;
                    }

                    FileActionLogic.UpdateFileActions();
                }

                // update UI when cut / restore / recycle source is current path
                else if (listing is not null
                    && op.FilePath.ParentPath == Data.CurrentPath
                    && op.OperationName is not FileOperation.OperationType.Copy)
                {
                    var listed = listing.Find(f => f.FullPath == sourcePath) ?? op.FilePath;
                    listing.Remove(listed);
                    FileActionLogic.UpdateFileActions();
                }
            }

            if (removeFromTree)
                RemoveDeletedTreeFolder(op.Device.ID, sourcePath);

            // A move/copy that lands a folder in a new location needs the same tree update a push gets:
            // add it under its (already loaded) destination parent, if that parent is visible in the tree.
            if (op.FilePath.IsDirectory && op.OperationName is FileOperation.OperationType.Move or FileOperation.OperationType.Copy)
                AddCreatedTreeFolder(op.Device.ID, op.TargetPath.FullPath);

            op.PropertyChanged -= MoveFileOp_PropertyChanged;
        }
    }

    public static async Task MakeDir(LogicalDeviceViewModel device, string fullPath)
        => await MakeDirs(device.ID, [fullPath]);

    public static async Task TryMakeDir(LogicalDeviceViewModel device, string fullPath)
    {
        try
        {
            await MakeDir(device, fullPath);
        }
        catch
        {
        }
    }

    public static async Task MakeDirs(LogicalDeviceViewModel device, IEnumerable<string> paths)
        => await MakeDirs(device.ID, paths);

    public static async Task MakeDirs(string deviceId, IEnumerable<string> paths)
    {
        var result = await ADBService.ExecuteVoidShellCommand(deviceId,
                                                              CancellationToken.None,
                                                              "mkdir",
                                                              ["-p", .. paths.Select(path => ADBService.EscapeAdbShellString(path))]);

        if (!string.IsNullOrEmpty(result))
            throw new Exception(result);
    }

    public static async Task MakeFile(LogicalDeviceViewModel device, string fullPath)
    {
        var result = await ADBService.ExecuteVoidShellCommand(device.ID,
                                                              CancellationToken.None,
                                                              "touch",
                                                              ADBService.EscapeAdbShellString(fullPath));

        if (!string.IsNullOrEmpty(result))
            throw new Exception(result);
    }

    public static async void WriteLine(LogicalDeviceViewModel device, string fullPath, string newLine)
    {
        var result = await ADBService.ExecuteVoidShellCommand(device.ID,
                                                              CancellationToken.None,
                                                              "echo",
                                                              [newLine, ">>", ADBService.EscapeAdbShellString(fullPath)]);

        if (!string.IsNullOrEmpty(result))
        {
            throw new Exception(result);
        }
    }

    public static string ReadAllText(LogicalDeviceViewModel device, params string[] paths)
    {
        if (paths.Length == 0)
            return string.Empty;

        var exitCode = ADBService.ExecuteDeviceAdbShellCommand(device.ID,
                                                               "cat",
                                                               out string stdout,
                                                               out string stderr,
                                                               CancellationToken.None, [.. paths.Select(path => ADBService.EscapeAdbShellString(path))]);

        if (exitCode != 0)
            throw new Exception(stderr);

        return stdout;
    }

    public static string GetPackageName(LogicalDeviceViewModel device, string fullPath)
    {
        ADBService.ExecuteDeviceAdbShellCommand(device.ID,
                                                "pm",
                                                out string stdout,
                                                out _,
                                                CancellationToken.None,
                                                "install",
                                                "-R",
                                                "--pkg",
                                                "''",
                                                ADBService.EscapeAdbShellString(fullPath));

        var match = AdbRegEx.RE_PACKAGE_NAME().Match(stdout);
        return match.Success ? match.Groups["package"].Value : fullPath[..fullPath.LastIndexOf('.')][(fullPath.LastIndexOf('/') + 1)..];
    }

    public static void InstallPackages(LogicalDeviceViewModel device, IEnumerable<FileClass> items, Dispatcher dispatcher)
    {
        foreach (var item in items)
        {
            if (AppBackupHelper.IsApkBackup(item.FullName))
            {
                RestorePackageBackup(device, item, dispatcher);
                continue;
            }

            var op = new PackageInstallOperation(dispatcher, device, item);
            op.PropertyChanged += InstallOp_PropertyChanged;

            Data.FileOpQ.AddOperation(op);
        }
    }

    public static void PushPackages(LogicalDeviceViewModel device, IEnumerable<ShellItem> items, Dispatcher dispatcher)
    {
        foreach (var item in items)
        {
            if (AppBackupHelper.IsApkBackup(item.ParsingName) || AppBackupHelper.IsApkBackup(item.Name))
            {
                RestorePackageBackup(device, item, dispatcher);
                continue;
            }

            var op = new PackageInstallOperation(dispatcher, device, new(new FilePath(item)), pushPackage: true);
            op.PropertyChanged += InstallOp_PropertyChanged;
            
            Data.FileOpQ.AddOperation(op);
        }
    }

    public static void BackupPackages(
        LogicalDeviceViewModel device,
        IEnumerable<Package> packages,
        string windowsFolder,
        Dispatcher dispatcher)
    {
        foreach (var package in packages)
        {
            var destName = FileHelper.DuplicateFile(
                Directory.Exists(windowsFolder) ? Directory.GetFiles(windowsFolder).Select(Path.GetFileName) : [],
                AppBackupHelper.WindowsBackupFileName(package.Name));
            var windowsDest = FileHelper.ConcatPaths(windowsFolder, destName, '\\');
            Directory.CreateDirectory(windowsFolder);
            var tempArchive = AppBackupHelper.DeviceTempArchivePath();
            var display = new FileClass(destName, windowsDest, AbstractFile.FileType.File);

            var op = new AppBackupOperation(display, tempArchive, windowsDest, package, device, dispatcher);
            Data.FileOpQ.AddOperation(op);
        }
    }

    public static void RestorePackageBackup(LogicalDeviceViewModel device, FileClass deviceFile, Dispatcher dispatcher)
    {
        var tempArchive = AppBackupHelper.DeviceTempArchivePath();
        if (!SilentCopy(device, deviceFile.FullPath, tempArchive, out var stderr))
        {
            DialogService.ShowMessage(
                stderr,
                Strings.Resources.S_MENU_INSTALL,
                DialogService.DialogIcon.Critical,
                copyToClipboard: true);
            return;
        }

        Data.FileOpQ.AddOperation(new AppRestoreOperation(deviceFile, tempArchive, device, dispatcher));
    }

    public static void RestorePackageBackup(LogicalDeviceViewModel device, ShellItem windowsItem, Dispatcher dispatcher)
    {
        var tempArchive = AppBackupHelper.DeviceTempArchivePath();
        var source = new SyncFile(windowsItem);
        var target = new SyncFile(tempArchive);
        var push = FileSyncOperation.PushFile(source, target, device, dispatcher);
        var display = new FileClass(windowsItem);

        push.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not nameof(FileOperation.Status))
                return;

            if (push.Status is FileOperation.OperationStatus.Completed)
            {
                dispatcher.Invoke(() =>
                    Data.FileOpQ.AddOperation(new AppRestoreOperation(display, tempArchive, device, dispatcher)));
            }
            else if (push.Status is FileOperation.OperationStatus.Failed or FileOperation.OperationStatus.Canceled)
            {
                SilentDelete(device, tempArchive);
            }
        };

        Data.FileOpQ.AddOperation(push);
    }

    public static void UninstallPackages(LogicalDeviceViewModel device, IEnumerable<string> packages, Dispatcher dispatcher)
    {
        foreach (var item in packages)
        {
            var op = new PackageInstallOperation(dispatcher, device, packageName: item);
            op.PropertyChanged += InstallOp_PropertyChanged;

            Data.FileOpQ.AddOperation(op);
        }
    }

    private static void InstallOp_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        var op = sender as PackageInstallOperation;

        // when operation completes, remove this event handler anyway
        if (e.PropertyName is not nameof(FileOperation.Status) || op.Status is not FileOperation.OperationStatus.Completed)
            return;

        if (op.Device.ID == Data.DevicesObject.Current.ID
            && Data.FileActions.IsAppDrive)
        {
            // update UI when on current device and current path
            if (op.IsUninstall)
                Data.Packages.RemoveAll(pkg => pkg.Name == op.PackageName);
            else if (op.PushPackage)
                Data.FileActions.RefreshPackages = true;
        }

        op.PropertyChanged -= InstallOp_PropertyChanged;
    }

    public static ulong? GetPackagesCount(LogicalDeviceViewModel device, bool includeSystem = true)
    {
        string[] args = includeSystem
            ? ["list", "packages", "|", "wc", "-l"]
            : ["list", "packages", "-3", "|", "wc", "-l"];

        var result = ADBService.ExecuteDeviceAdbShellCommand(device.ID, "pm", out string stdout, out _, CancellationToken.None, args);
        if (result != 0 || !ulong.TryParse(stdout, out ulong value))
            return null;

        return value;
    }

    private static readonly string PKG_LIST_SYSTEM = $"{AdbExplorerConst.ADB_UNIT_SEP}SYS{AdbExplorerConst.ADB_UNIT_SEP}";
    private static readonly string PKG_LIST_USER = $"{AdbExplorerConst.ADB_UNIT_SEP}USER{AdbExplorerConst.ADB_UNIT_SEP}";

    public static ObservableList<Package> GetPackages(LogicalDeviceViewModel device, bool includeSystem = true, bool optionalParams = true)
    {
        // More package-specific info can be acquired using dumpsys package [package_name]

        var optional = optionalParams ? " -U --show-versioncode" : "";
        var userCmd = $"pm list packages -3 -f{optional}";
        var script = includeSystem
            ? string.Join("; ",
                $"echo {PKG_LIST_SYSTEM}",
                $"pm list packages -s -f{optional}",
                $"echo {AdbExplorerConst.ADB_FIELD_SEP}",
                $"echo {PKG_LIST_USER}",
                userCmd,
                $"echo {AdbExplorerConst.ADB_FIELD_SEP}")
            : userCmd;

        var exitCode = ADBService.ExecuteDeviceAdbShellCommand(device.ID, script, out string stdout, out _, CancellationToken.None);
        if (exitCode != 0 || string.IsNullOrWhiteSpace(stdout))
            return [];

        ObservableList<Package> packages = [];

        if (includeSystem)
        {
            packages.AddRange(ParsePackageSection(ExtractPackageListSection(stdout, PKG_LIST_SYSTEM), Package.PackageType.System, device.SerialNumber));
            packages.AddRange(ParsePackageSection(ExtractPackageListSection(stdout, PKG_LIST_USER), Package.PackageType.User, device.SerialNumber));
        }
        else
        {
            packages.AddRange(ParsePackageSection(stdout, Package.PackageType.User, device.SerialNumber));
        }

        return packages;
    }

    private static IEnumerable<Package> ParsePackageSection(string section, Package.PackageType type, string serialNumber)
        => section.Split(ADBService.LINE_SEPARATORS, StringSplitOptions.RemoveEmptyEntries)
                  .Select(pkg => Package.New(pkg, type))
                  .OfType<Package>()
                  .Select(pkg =>
                  {
                      pkg.DeviceSerial = serialNumber;
                      return pkg;
                  });

    private static string ExtractPackageListSection(string stdout, string label)
    {
        var start = stdout.IndexOf(label, StringComparison.Ordinal);
        if (start < 0)
            return "";

        var end = stdout.IndexOf(AdbExplorerConst.ADB_FIELD_SEP, start + label.Length);
        if (end < 0)
            end = stdout.Length;

        return stdout[(start + label.Length)..end].Trim(AdbExplorerConst.ADB_FIELD_SEP, ' ', '\r', '\n');
    }

    public static void ChangeDateFromName(LogicalDeviceViewModel device, IEnumerable<FileClass> items, Dispatcher dispatcher)
    {
        List<FileOperation> operations = [];

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

        operations.ForEach(op => op.PropertyChanged += ChangeModifiedOp_PropertyChanged);
        Data.FileOpQ.AddOperations(operations);
    }

    private static void ChangeModifiedOp_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        var op = sender as FileChangeModifiedOperation;

        // when operation completes, remove this event handler anyway
        if (e.PropertyName is not nameof(FileOperation.Status) || op.Status is not FileOperation.OperationStatus.Completed)
            return;

        if (op.Device.ID == Data.DevicesObject.Current.ID
            && op.FilePath.ParentPath == Data.CurrentPath)
        {
            // update UI when on current device and current path
            op.FilePath.ModifiedTime = op.NewDate;
        }

        op.PropertyChanged -= ChangeModifiedOp_PropertyChanged;
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
