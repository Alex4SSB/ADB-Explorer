using ADB_Explorer.Models;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Services;

public class FileChangeModifiedOperation : AbstractShellFileOperation
{
    public readonly DateTime NewDate;

    public FileChangeModifiedOperation(FileClass filePath, DateTime newDate, ADBService.AdbDevice adbDevice, Dispatcher dispatcher)
        : base(filePath, adbDevice, dispatcher)
    {
        OperationName = OperationType.Update;
        NewDate = newDate;
    }

    public override void Start()
    {
        if (Status == OperationStatus.InProgress)
        {
            throw new Exception("Cannot start an already active operation!");
        }

        Status = OperationStatus.InProgress;
        StatusInfo = new InProgShellProgressViewModel();

        var operationTask = ADBService.ExecuteDeviceAdbShellCommand(Device.ID,
                                                                    CancelTokenSource.Token,
                                                                    "touch",
                                                                    "-m",
                                                                    "-t",
                                                                    NewDate.ToString("yyyyMMddHHmm.ss"),
                                                                    ADBService.EscapeAdbShellString(FilePath.FullPath));

        operationTask.ContinueWith((t) =>
        {
            if (t.Result == "")
            {
                Status = OperationStatus.Completed;
                StatusInfo = new CompletedShellProgressViewModel();
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
}
