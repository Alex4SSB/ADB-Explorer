using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Services;

public class FileRenameOperation : AbstractShellFileOperation
{
    private CancellationTokenSource cancelTokenSource;
    private string fullTargetPath;

    public FileRenameOperation(Dispatcher dispatcher, ADBService.AdbDevice adbDevice, FileClass filePath, string targetPath)
        : base(dispatcher, adbDevice, filePath)
    {
        OperationName = OperationType.Rename;

        TargetPath = new(FileHelper.ConcatPaths(FilePath.ParentPath, targetPath), fileType: AbstractFile.FileType.Folder);
        fullTargetPath = targetPath;
    }

    public override void Start()
    {
        if (Status == OperationStatus.InProgress)
        {
            throw new Exception("Cannot start an already active operation!");
        }

        Status = OperationStatus.InProgress;
        StatusInfo = new InProgShellProgressViewModel();
        cancelTokenSource = new CancellationTokenSource();
        
        var operationTask = ADBService.ExecuteDeviceAdbShellCommand(Device.ID, "mv",
                ADBService.EscapeAdbShellString(FilePath.FullPath),
                ADBService.EscapeAdbShellString(fullTargetPath));

        operationTask.ContinueWith((t) =>
        {
            if (t.Result == "")
            {
                Status = OperationStatus.Completed;
                StatusInfo = new CompletedShellProgressViewModel();

                Dispatcher.Invoke(() =>
                {
                    FilePath.UpdatePath(fullTargetPath);

                    Data.FileActions.ItemToSelect = FilePath;
                });
            }
            else
            {
                Status = OperationStatus.Failed;
                StatusInfo = new FailedOpProgressViewModel(t.Result);
            }

        }, TaskContinuationOptions.OnlyOnRanToCompletion);

        operationTask.ContinueWith((t) =>
        {
            Status = OperationStatus.Canceled;
            StatusInfo = new CanceledOpProgressViewModel();
        }, TaskContinuationOptions.OnlyOnCanceled);

        operationTask.ContinueWith((t) =>
        {
            Status = OperationStatus.Failed;
            StatusInfo = new FailedOpProgressViewModel(t.Exception.InnerException.Message);
        }, TaskContinuationOptions.OnlyOnFaulted);
    }

    public override void Cancel()
    {
        if (Status != OperationStatus.InProgress)
        {
            throw new Exception("Cannot cancel a deactivated operation!");
        }

        cancelTokenSource.Cancel();
    }
}
