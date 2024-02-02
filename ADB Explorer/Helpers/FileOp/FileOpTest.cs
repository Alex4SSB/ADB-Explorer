using ADB_Explorer.Models;
using ADB_Explorer.Services;

namespace ADB_Explorer.Helpers;

internal class FileOpTest
{
    private static int i = 0;

    private static readonly FileOpProgressInfo[] updates = new FileOpProgressInfo[]
    {
        new AdbSyncProgressInfo("/Folder/subfolderA/file1", 10, new Random().Next(1, 99), null),
        new AdbSyncProgressInfo("/Folder/subfolderA/file2", 15, new Random().Next(1, 99), null),
        SyncErrorInfo.New(AdbRegEx.RE_FILE_SYNC_ERROR().Match("adb: error: stat failed when trying to push to /Folder/subfolderA/file3: Permission denied")),
        new AdbSyncProgressInfo("/Folder/subfolderA/file4", 25, new Random().Next(1, 99), null),
        new AdbSyncProgressInfo("/Folder/subfolderA/file5", 30, new Random().Next(1, 99), null),
        new AdbSyncProgressInfo("/Folder/subfolderA/file6", 35, new Random().Next(1, 99), null),
        new AdbSyncProgressInfo("/Folder/subfolderA/file7", 40, new Random().Next(1, 99), null),
        new AdbSyncProgressInfo("/Folder/subfolderA/file8", 45, new Random().Next(1, 99), null),
        new AdbSyncProgressInfo("/Folder/subfolderB/file9", 50, new Random().Next(1, 99), null),
        new AdbSyncProgressInfo("/Folder/subfolderB/file10", 55, new Random().Next(1, 99), null),
        new AdbSyncProgressInfo("/Folder/subfolderB/file11", 60, new Random().Next(1, 99), null),
        new AdbSyncProgressInfo("/Folder/subfolderB/file12", 65, new Random().Next(1, 99), null),
        new AdbSyncProgressInfo("/Folder/subfolderB/file13", 70, new Random().Next(1, 99), null),
        new AdbSyncProgressInfo("/Folder/subfolderB/file14", 75, new Random().Next(1, 99), null),
        new AdbSyncProgressInfo("/Folder/subfolderB/file15", 80, new Random().Next(1, 99), null),
        new AdbSyncProgressInfo("/Folder/subfolderB/file16", 85, new Random().Next(1, 99), null),
        new AdbSyncProgressInfo("/Folder/subfolderB/folderC/file17", 90, new Random().Next(1, 99), null),
        new AdbSyncProgressInfo("/Folder/subfolderB/folderC/file18", 95, new Random().Next(1, 99), null),
    };

    public static void TestCurrentOperation()
    {
        if (Data.FileOpQ.Operations.OfType<InProgressTestOperation>().FirstOrDefault() is InProgressTestOperation op)
        {
            if (i == updates.Length)
            {
                Data.FileOpQ.RemoveOperation(op);
                Data.FileOpQ.AddOperation(new CompletedTestOperation(App.Current.Dispatcher, Data.CurrentADBDevice, op.FilePath, new(op.FilePath.FullPath, (ulong)updates.Count(u => u is AdbSyncProgressInfo), (ulong)updates.Count(u => u is SyncErrorInfo), 1000000, 200, 2)));
                return;
            }
            op.AddUpdates(updates[i]);
            op.UpdateStatus(updates[i++]);

            return;
        }
        
        i = 0;

        Data.FileOpQ.AddOperation(InProgressTestOperation.CreateFolderInProgress(App.Current.Dispatcher, Data.CurrentADBDevice, "/Folder"));

        //fileOperationQueue.AddOperation(InProgressTestOperation.CreateProgressStart(Dispatcher, CurrentADBDevice, "File.exe"));
        //fileOperationQueue.AddOperation(InProgressTestOperation.CreateFileInProgress(Dispatcher, CurrentADBDevice, "File.exe"));
    }
}
