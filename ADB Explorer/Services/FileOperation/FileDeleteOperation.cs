using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services.AppInfra;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Services;

public class FileDeleteOperation : FileOperation
{
    private Task operationTask;
    private CancellationTokenSource cancelTokenSource;
    private readonly ObservableList<FileClass> fileList;

    public FileDeleteOperation(Dispatcher dispatcher, ADBService.AdbDevice adbDevice, FilePath path, ObservableList<FileClass> fileList) : base(dispatcher, adbDevice, path)
    {
        OperationName = OperationType.Delete;
        this.fileList = fileList;
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
        operationTask = Task.Run(() => ADBService.ExecuteDeviceAdbShellCommand(Device.ID, "rm", out _, out _, new[] { "-rf", ADBService.EscapeAdbShellString(FilePath.FullPath) }));

        operationTask.ContinueWith((t) =>
        {
            var operationStatus = ((Task<int>)t).Result == 0 ? OperationStatus.Completed : OperationStatus.Failed;
            Status = operationStatus;

            StatusInfo = new CompletedShellProgressViewModel();

            if (operationStatus is OperationStatus.Completed)
            {
                Dispatcher.Invoke(() =>
                {
                    FileActionLogic.RemoveFile((FileClass)FilePath);

                    fileList.Remove((FileClass)FilePath);
                });

                if (((FileClass)FilePath).TrashIndex is TrashIndexer indexer)
                {
                    ShellFileOperation.SilentDelete(Device, indexer.IndexerPath);
                }
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
