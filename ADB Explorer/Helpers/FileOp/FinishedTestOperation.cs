using ADB_Explorer.Models;
using ADB_Explorer.Services;
using ADB_Explorer.ViewModels;
using static ADB_Explorer.Services.ADBService.AdbDevice;

namespace ADB_Explorer.Helpers;

public class CompletedTestOperation : FileOperation
{
    private readonly CompletedSyncProgressViewModel info;

    private CompletedTestOperation(Dispatcher dispatcher, ADBService.AdbDevice adbDevice, FilePath filePath, AdbSyncStatsInfo adbInfo) :
        base(dispatcher, adbDevice, filePath)
    {
        info = new(adbInfo);
    }

    public static CompletedTestOperation CreateFileCompleted(Dispatcher dispatcher, ADBService.AdbDevice adbDevice, string filePath)
    {
        return new CompletedTestOperation(dispatcher, adbDevice, new FilePath(filePath), new AdbSyncStatsInfo
        {
            AverageRate = (decimal)2.3,
            FilesSkipped = 0,
            FilesTransferred = 1,
            TargetPath = filePath,
            TotalBytes = 3141592653589,
            TotalTime = 123
        });
    }
    public static CompletedTestOperation CreateFolderCompleted(Dispatcher dispatcher, ADBService.AdbDevice adbDevice, string folderPath)
    {
        return new CompletedTestOperation(
            dispatcher, 
            adbDevice, 
            new FilePath(folderPath, "", Converters.FileTypeClass.FileType.Folder, null), 
            new AdbSyncStatsInfo
            {
                AverageRate = (decimal)2.3,
                FilesSkipped = 0,
                FilesTransferred = 10,
                TargetPath = folderPath,
                TotalBytes = 3141592653589,
                TotalTime = 123
            });
    }

    public static CompletedTestOperation CreateFileSkipped(Dispatcher dispatcher, ADBService.AdbDevice adbDevice, string filePath)
    {
        return new CompletedTestOperation(dispatcher, adbDevice, new FilePath(filePath), new AdbSyncStatsInfo
        {
            AverageRate = (decimal)0,
            FilesSkipped = 1,
            FilesTransferred = 0,
            TargetPath = filePath,
            TotalBytes = 0,
            TotalTime = 0
        });
    }
    public static CompletedTestOperation CreateFolderPartiallySkipped(Dispatcher dispatcher, ADBService.AdbDevice adbDevice, string folderPath)
    {
        return new CompletedTestOperation(
            dispatcher,
            adbDevice,
            new FilePath(folderPath, "", Converters.FileTypeClass.FileType.Folder, null),
            new AdbSyncStatsInfo
            {
                AverageRate = (decimal)2.3,
                FilesSkipped = 3,
                FilesTransferred = 7,
                TargetPath = folderPath,
                TotalBytes = 3141592653589,
                TotalTime = 123
            });
    }

    public override void Start()
    {
        Status = OperationStatus.Completed;
        StatusInfo = info;
    }

    public override void Cancel()
    {
        Status = OperationStatus.Canceled;
        StatusInfo = new CanceledOpProgressViewModel();
    }
}
