using ADB_Explorer.Models;
using ADB_Explorer.Services;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Helpers;

public class InProgressTestOperation : FileOperation
{
    private readonly InProgSyncProgressViewModel info;

    public override SyncFile FilePath { get; }

    public override ObservableList<SyncFile> Children => FilePath.Children;

    private InProgressTestOperation(Dispatcher dispatcher, ADBService.AdbDevice adbDevice, string filePath, AdbSyncProgressInfo adbInfo) :
        base(dispatcher, adbDevice, new(filePath))
    {
        info = new(adbInfo);
        FilePath = new SyncFile(filePath, adbInfo.CurrentFilePercentage.HasValue ? AbstractFile.FileType.Folder : AbstractFile.FileType.File);
    }

    public static InProgressTestOperation CreateProgressStart(Dispatcher dispatcher, ADBService.AdbDevice adbDevice, string filePath)
    {
        return new InProgressTestOperation(dispatcher, adbDevice, filePath, new AdbSyncProgressInfo(null, null, null, null));
    }

    public static InProgressTestOperation CreateFileInProgress(Dispatcher dispatcher, ADBService.AdbDevice adbDevice, string filePath)
    {
        return new InProgressTestOperation(dispatcher, adbDevice, filePath, new AdbSyncProgressInfo(null, 40, null, null));
    }

    public static InProgressTestOperation CreateFolderInProgress(Dispatcher dispatcher, ADBService.AdbDevice adbDevice, string filePath)
    {
        return new InProgressTestOperation(dispatcher, adbDevice, filePath, new AdbSyncProgressInfo("Subfile.txt", 40, 60, null));
    }

    public override void Start()
    {
        Status = OperationStatus.InProgress;
        StatusInfo = info;
    }

    public override void Cancel()
    {
        Status = OperationStatus.Canceled;
        StatusInfo = new CanceledOpProgressViewModel();
    }

    public void Fail(string errorMsg)
    {
        Status = OperationStatus.Failed;
        StatusInfo = new FailedOpProgressViewModel(errorMsg);
    }

    public void UpdateStatus(FileOpProgressInfo update)
    {
        if (update is SyncErrorInfo)
            return;

        StatusInfo = new InProgSyncProgressViewModel((AdbSyncProgressInfo)update);
    }
}
