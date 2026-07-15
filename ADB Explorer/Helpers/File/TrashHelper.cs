using ADB_Explorer.Models;
using ADB_Explorer.Services;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Helpers;

internal static class TrashHelper
{
    public static void EnableRecycleButtons(IEnumerable<FileClass> fileList = null)
    {
        if (fileList is null)
            fileList = Data.DirList.FileList;

        Data.FileActions.RestoreEnabled = fileList.Any(file => file.TrashIndex is not null && !string.IsNullOrEmpty(file.TrashIndex.OriginalPath));
        Data.FileActions.DeleteEnabled = fileList.Any(item => item.Extension != AdbExplorerConst.RECYCLE_INDEX_SUFFIX);
    }

    public static List<FileClass> GetRecycleBinItems()
    {
        if (Data.DevicesObject.Current is null)
            return [];

        ParseIndexers();

        var paths = ADBService.FindFilesInPath(Data.DevicesObject.Current.ID,
                                               AdbExplorerConst.RECYCLE_PATH,
                                               excludeNames: ["*" + AdbExplorerConst.RECYCLE_INDEX_SUFFIX]);

        List<FileClass> items = [];
        foreach (var path in paths)
        {
            var name = FileHelper.GetFullName(path);
            var item = new FileClass(name, path, AbstractFile.FileType.File);
            if (Data.RecycleIndex.FirstOrDefault(index => index.RecycleName == name) is TrashIndexer indexer)
                item.TrashIndex = indexer;

            items.Add(item);
        }

        return items;
    }

    public static void UpdateRecycledItemsCount(CancellationToken cancellationToken = default)
    {
        var countTask = Task.Run(() => ADBService.CountRecycle(Data.DevicesObject.Current.ID), cancellationToken);
        countTask.ContinueWith((t) =>
        {
            if (t.IsCanceled || Data.DevicesObject.Current is null)
                return;

            var count = t.Result;
            if (count < 1)
                count = FolderHelper.FolderExists(AdbExplorerConst.RECYCLE_PATH) is null ? -1 : 0;

            var trash = Data.DevicesObject.Current?.Drives.Find(d => d.Type is AbstractDrive.DriveType.Trash);
            App.SafeInvoke(() => ((VirtualDriveViewModel)trash)?.SetItemsCount(count));
        });
    }

    public static Task ParseIndexersAsync(CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        Data.RecycleIndex.Clear();

        var indexers = ADBService.FindFilesInPath(Data.DevicesObject.Current.ID,
                                                  AdbExplorerConst.RECYCLE_PATH,
                                                  includeNames: ["*" + AdbExplorerConst.RECYCLE_INDEX_SUFFIX]);

        var lines = ShellFileOperation.ReadAllText(Data.DevicesObject.Current, indexers).Split(ADBService.LINE_SEPARATORS,
                                                                                          StringSplitOptions.RemoveEmptyEntries);

        lines.ToList().ForEach(line => Data.RecycleIndex.Add(new(line)));
    }, cancellationToken);

    public static void ParseIndexers()
    {
        Data.RecycleIndex.Clear();

        var indexers = ADBService.FindFilesInPath(Data.DevicesObject.Current.ID,
                                                  AdbExplorerConst.RECYCLE_PATH,
                                                  includeNames: ["*" + AdbExplorerConst.RECYCLE_INDEX_SUFFIX]);

        var lines = ShellFileOperation.ReadAllText(Data.DevicesObject.Current, indexers).Split(ADBService.LINE_SEPARATORS,
                                                                                          StringSplitOptions.RemoveEmptyEntries);

        lines.ToList().ForEach(line => Data.RecycleIndex.Add(new(line)));
    }

    public static void SyncDriveViewTrashCountAfterDelete(FileDeleteOperation completedOp)
    {
        if (!Data.FileActions.IsDriveViewVisible
            || !completedOp.FilePath.FullPath.StartsWith(AdbExplorerConst.RECYCLE_PATH, StringComparison.Ordinal))
            return;

        var pendingRecycleDeletes = Data.FileOpQ.Operations.Any(op =>
            op is FileDeleteOperation deleteOp
            && deleteOp != completedOp
            && deleteOp.Status is FileOperation.OperationStatus.Waiting or FileOperation.OperationStatus.InProgress
            && deleteOp.FilePath.FullPath.StartsWith(AdbExplorerConst.RECYCLE_PATH, StringComparison.Ordinal));

        if (!pendingRecycleDeletes)
            UpdateRecycledItemsCount();
    }
}
