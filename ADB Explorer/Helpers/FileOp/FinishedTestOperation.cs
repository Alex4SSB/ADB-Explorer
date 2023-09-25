using ADB_Explorer.Converters;
using ADB_Explorer.Models;
using ADB_Explorer.Services;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Helpers;

public class CompletedTestOperation : FileOperation
{
    private readonly CompletedSyncProgressViewModel info;

    public override ObservableList<SyncFile> Children => TargetPath.Children;

    public CompletedTestOperation(Dispatcher dispatcher, ADBService.AdbDevice adbDevice, SyncFile filePath, AdbSyncStatsInfo adbInfo) :
        base(dispatcher, adbDevice, filePath)
    {
        info = new(adbInfo);
        TargetPath = filePath;
    }

    public static CompletedTestOperation CreateFileCompleted(Dispatcher dispatcher, ADBService.AdbDevice adbDevice, string filePath)
        => new(dispatcher, adbDevice, new(filePath), new(filePath, 1, 0, 2.3m, 3141592653589, 123));

    public static CompletedTestOperation CreateFolderCompleted(Dispatcher dispatcher, ADBService.AdbDevice adbDevice, string folderPath)
        => new(
            dispatcher, 
            adbDevice, 
            new(folderPath, AbstractFile.FileType.Folder), 
            new(folderPath, 10, 0, 2.3m, 3141592653589, 123));

    public static CompletedTestOperation CreateFileSkipped(Dispatcher dispatcher, ADBService.AdbDevice adbDevice, string filePath)
        => new(dispatcher, adbDevice, new(filePath), new(filePath, 0, 1, 0m, 0, 0));
    
    public static CompletedTestOperation CreateFolderPartiallySkipped(Dispatcher dispatcher, ADBService.AdbDevice adbDevice, string folderPath)
        => new(dispatcher, adbDevice, new(folderPath, AbstractFile.FileType.Folder), new(folderPath, 7, 3, 2.3m, 3141592653589, 123));

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
