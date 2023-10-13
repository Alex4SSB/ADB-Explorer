using ADB_Explorer.Converters;
using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services.AppInfra;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Services;

public class FileDeleteOperation : AbstractShellFileOperation
{
    private CancellationTokenSource cancelTokenSource;
    private readonly ObservableList<FileClass> fileList;

    public FileDeleteOperation(Dispatcher dispatcher, ADBService.AdbDevice adbDevice, FileClass path, ObservableList<FileClass> fileList)
        : base(dispatcher, adbDevice, path)
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

        var task = ADBService.ExecuteDeviceAdbShellCommand(Device.ID, "rm", "-rf", ADBService.EscapeAdbShellString(FilePath.FullPath)); 

        task.ContinueWith((t) =>
        {
            if (t.Result == "")
            {
                Status = OperationStatus.Completed;
                StatusInfo = new CompletedShellProgressViewModel();

                Dispatcher.Invoke(() =>
                {
                    FileActionLogic.RemoveFile(base.FilePath);

                    fileList.Remove(base.FilePath);
                });

                if (base.FilePath.TrashIndex is TrashIndexer indexer)
                {
                    ShellFileOperation.SilentDelete(Device, indexer.IndexerPath);
                }
            }
            else
            {
                Status = OperationStatus.Failed;
                var res = AdbRegEx.RE_SHELL_ERROR.Matches(t.Result);
                var updates = res.Where(m => m.Success).Select(m => new ShellErrorInfo(m, base.FilePath.FullPath));
                base.AddUpdates(updates);

                var message = updates.Last().Message;
                if (message.Contains(':'))
                    message = message.Split(':').Last().TrimStart();

                var errorString = FileOpStatusConverter.StatusString(typeof(ShellErrorInfo),
                                                   failed: Children.Count > 0 ? updates.Count() : -1,
                                                   message: message,
                                                   total: true);

                StatusInfo = new FailedOpProgressViewModel(errorString);
            }

        }, TaskContinuationOptions.OnlyOnRanToCompletion);

        task.ContinueWith((t) =>
        {
            Status = OperationStatus.Canceled;
            StatusInfo = new CanceledOpProgressViewModel();
        }, TaskContinuationOptions.OnlyOnCanceled);

        task.ContinueWith((t) =>
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
