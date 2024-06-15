using ADB_Explorer.Converters;
using ADB_Explorer.Models;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Services;

public class FileDeleteOperation : AbstractShellFileOperation
{
    public FileDeleteOperation(Dispatcher dispatcher, ADBService.AdbDevice adbDevice, FileClass path)
        : base(path, adbDevice, dispatcher)
    {
        OperationName = OperationType.Delete;
        AltTarget = NavHistory.SpecialLocation.devNull;
    }

    public override void Start()
    {
        if (Status == OperationStatus.InProgress)
        {
            throw new Exception("Cannot start an already active operation!");
        }

        Status = OperationStatus.InProgress;
        StatusInfo = new InProgShellProgressViewModel();

        var task = ADBService.ExecuteVoidShellCommand(Device.ID, CancelTokenSource.Token, "rm", "-rf", ADBService.EscapeAdbShellString(FilePath.FullPath)); 

        task.ContinueWith((t) =>
        {
            if (t.Result == "")
            {
                Status = OperationStatus.Completed;
                StatusInfo = new CompletedShellProgressViewModel();
            }
            else
            {
                Status = OperationStatus.Failed;
                var res = AdbRegEx.RE_SHELL_ERROR().Matches(t.Result);
                var updates = res.Where(m => m.Success).Select(m => new ShellErrorInfo(m, base.FilePath.FullPath));
                base.AddUpdates(updates);

                var message = updates.Any() ? updates.Last().Message : "";
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
}
