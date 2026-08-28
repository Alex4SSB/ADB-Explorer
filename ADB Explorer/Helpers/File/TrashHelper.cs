using ADB_Explorer.Models;
using ADB_Explorer.Services;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Helpers;

internal static class TrashHelper
{
    public static void EnableRecycleButtons(IEnumerable<FileClass> fileList = null)
    {
        if (fileList is null)
            fileList = Data.DirList?.FileList ?? [];

        Data.Active.Actions.RestoreEnabled = fileList.Any(file => file.TrashIndex is not null && !string.IsNullOrEmpty(file.TrashIndex.OriginalPath));
        Data.Active.Actions.DeleteEnabled = fileList.Any(item => item.Extension != AdbExplorerConst.RECYCLE_INDEX_SUFFIX);
    }

    public static List<FileClass> GetRecycleBinItems()
    {
        if (Data.Active.Device is null)
            return [];

        ParseIndexers();

        var paths = ADBService.FindFilesInPath(Data.Active.Device.ID,
                                               AdbExplorerConst.RECYCLE_PATH,
                                               excludeNames: ["*" + AdbExplorerConst.RECYCLE_INDEX_SUFFIX]);

        List<FileClass> items = [];
        foreach (var path in paths)
        {
            var name = FileHelper.GetFullName(path);
            var item = new FileClass(name, path, AbstractFile.FileType.File);
            if (Data.RecycleIndex.FirstOrDefault(index => index.MatchesRecycleFile(name)) is TrashIndexer indexer)
                item.TrashIndex = indexer;

            items.Add(item);
        }

        return items;
    }

    public static VirtualDriveViewModel? GetTrashDrive(LogicalDeviceViewModel? device)
        => device?.Drives.OfType<VirtualDriveViewModel>().FirstOrDefault(d => d.Type is AbstractDrive.DriveType.Trash);

    public static void UpdateRecycledItemsCount(CancellationToken cancellationToken = default)
        => UpdateRecycledItemsCount(Data.DevicesObject.Current, cancellationToken);

    public static void UpdateRecycledItemsCount(LogicalDeviceViewModel? device, CancellationToken cancellationToken = default)
    {
        if (device is null)
            return;

        var countTask = Task.Run(() => CountRecycleOnDevice(device), cancellationToken);
        countTask.ContinueWith((t) =>
        {
            if (t.IsCanceled || t.IsFaulted)
                return;

            var trash = GetTrashDrive(device);
            App.SafeInvoke(() => ((VirtualDriveViewModel)trash)?.SetItemsCount(t.Result));
        });
    }

    /// <summary>
    /// Returns a known trash count, probing the device once when it has not been counted yet.
    /// </summary>
    public static long EnsureRecycleCount(LogicalDeviceViewModel? device, VirtualDriveViewModel? trash = null)
    {
        trash ??= GetTrashDrive(device);
        if (trash is null)
            return -1;

        if (trash.ItemsCount is long known)
            return known;

        if (device is null)
            return -1;

        var count = CountRecycleOnDevice(device);
        trash.SetItemsCount(count);
        return count;
    }

    private static long CountRecycleOnDevice(LogicalDeviceViewModel device)
    {
        var count = ADBService.CountRecycle(device.ID);
        if (count >= 1)
            return count;

        try
        {
            ADBService.TranslateDevicePath(device.ID, AdbExplorerConst.RECYCLE_PATH);
            return 0;
        }
        catch
        {
            return -1;
        }
    }

    public static Task ParseIndexersAsync(CancellationToken cancellationToken = default)
        => Task.Run(ParseIndexers, cancellationToken);

    public static void ParseIndexers()
    {
        var device = Data.DevicesObject.Current;
        if (device is null)
            return;

        string text;
        try
        {
            var indexers = ADBService.FindFilesInPath(device.ID,
                                                      AdbExplorerConst.RECYCLE_PATH,
                                                      includeNames: ["*" + AdbExplorerConst.RECYCLE_INDEX_SUFFIX]);

            text = ShellFileOperation.ReadAllText(device, indexers);
        }
        catch
        {
            Data.RecycleIndex.Clear();
            return;
        }

        var parsed = TrashIndexer.ParseLines(text);
        Data.RecycleIndex.Clear();
        Data.RecycleIndex.AddRange(parsed);
    }

    public static void SyncDriveViewTrashCountAfterDelete(FileDeleteOperation completedOp)
    {
        if (!completedOp.FilePath.FullPath.StartsWith(AdbExplorerConst.RECYCLE_PATH, StringComparison.Ordinal))
            return;

        var pendingRecycleDeletes = Data.FileOpQ.Operations.Any(op =>
            op is FileDeleteOperation deleteOp
            && deleteOp != completedOp
            && deleteOp.Status is FileOperation.OperationStatus.Waiting or FileOperation.OperationStatus.InProgress
            && deleteOp.FilePath.FullPath.StartsWith(AdbExplorerConst.RECYCLE_PATH, StringComparison.Ordinal));

        if (!pendingRecycleDeletes)
            UpdateRecycledItemsCount(completedOp.Device);
    }
}
