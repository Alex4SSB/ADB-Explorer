using ADB_Explorer.Models;
using ADB_Explorer.Services;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Helpers;

internal static class TrashHelper
{
    public static void UpdateIndexerFile() => Task.Run(() =>
    {
        var validIndexers = Data.DirList.FileList.Where(file => file.TrashIndex is not null).Select(file => file.TrashIndex);
        if (!validIndexers.Any())
        {
            ShellFileOperation.SilentDelete(Data.CurrentADBDevice, AdbExplorerConst.RECYCLE_INDEX_PATH);
            ShellFileOperation.SilentDelete(Data.CurrentADBDevice, AdbExplorerConst.RECYCLE_INDEX_BACKUP_PATH);
            return;
        }
        if (Data.DirList.FileList.Count(file => AdbExplorerConst.RECYCLE_INDEX_PATHS.Contains(file.FullPath)) < 2
            && validIndexers.Count() == Data.RecycleIndex.Count
            && Data.RecycleIndex.All(indexer => validIndexers.Contains(indexer)))
        {
            return;
        }

        var outString = string.Join("\r\n", validIndexers.Select(indexer => indexer.ToString()));
        var oldIndexFile = Data.DirList.FileList.Where(file => file.FullPath == AdbExplorerConst.RECYCLE_INDEX_PATH);

        try
        {
            if (oldIndexFile.Any())
                ShellFileOperation.MoveItem(Data.CurrentADBDevice, oldIndexFile.First(), AdbExplorerConst.RECYCLE_INDEX_BACKUP_PATH);

            ShellFileOperation.WriteLine(Data.CurrentADBDevice, AdbExplorerConst.RECYCLE_INDEX_PATH, ADBService.EscapeAdbShellString(outString));

            if (!string.IsNullOrEmpty(ShellFileOperation.ReadAllText(Data.CurrentADBDevice, AdbExplorerConst.RECYCLE_INDEX_PATH)) && oldIndexFile.Any())
                ShellFileOperation.SilentDelete(Data.CurrentADBDevice, AdbExplorerConst.RECYCLE_INDEX_BACKUP_PATH);
        }
        catch (Exception)
        { }
    });

    public static void EnableRecycleButtons(IEnumerable<FileClass> fileList = null)
    {
        if (fileList is null)
            fileList = Data.DirList.FileList;

        Data.FileActions.RestoreEnabled = fileList.Any(file => file.TrashIndex is not null && !string.IsNullOrEmpty(file.TrashIndex.OriginalPath));
        Data.FileActions.DeleteEnabled = fileList.Any(item => !AdbExplorerConst.RECYCLE_INDEX_PATHS.Contains(item.FullPath));
    }

    public static void UpdateRecycledItemsCount()
    {
        var countTask = Task.Run(() => ADBService.CountRecycle(Data.DevicesObject.Current.ID));
        countTask.ContinueWith((t) =>
        {
            if (t.IsCanceled || Data.DevicesObject.Current is null)
                return;

            var count = t.Result;
            if (count < 1)
                count = FolderHelper.FolderExists(AdbExplorerConst.RECYCLE_PATH) is null ? -1 : 0;

            var trash = Data.DevicesObject.Current?.Drives.Find(d => d.Type is AbstractDrive.DriveType.Trash);
            App.Current.Dispatcher.Invoke(() => ((VirtualDriveViewModel)trash)?.SetItemsCount(count));
        });
    }
}
