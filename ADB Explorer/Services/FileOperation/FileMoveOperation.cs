using ADB_Explorer.Converters;
using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Services;

public class FileMoveOperation : AbstractShellFileOperation
{
    public string RecycleName;
    public string IndexerPath;
    public DateTime? DateModified;

    public FileMoveOperation(FileClass filePath, SyncFile targetPath, ADBService.AdbDevice adbDevice, Dispatcher dispatcher, bool isCopy = false)
        : base(filePath, adbDevice, dispatcher)
    {
        if (isCopy)
            OperationName = OperationType.Copy;
        else if (targetPath.FullPath.StartsWith(AdbExplorerConst.RECYCLE_PATH))
            OperationName = OperationType.Recycle;
        else if (filePath.TrashIndex is not null)
            OperationName = OperationType.Restore;
        else
            OperationName = OperationType.Move;

        TargetPath = targetPath;
    }

    public override void Start()
    {
        if (Status == OperationStatus.InProgress)
        {
            throw new Exception("Cannot start an already active operation!");
        }

        Status = OperationStatus.InProgress;
        StatusInfo = new InProgShellProgressViewModel();

        if (OperationName is OperationType.Recycle)
        {
            RecycleName = $"{{{DateTimeOffset.Now.ToUnixTimeMilliseconds()}}}";
            TargetPath.UpdatePath(FileHelper.ConcatPaths(TargetPath.ParentPath, RecycleName));
            DateModified = FilePath.ModifiedTime;
            IndexerPath = $"{AdbExplorerConst.RECYCLE_PATH}/.{RecycleName}{AdbExplorerConst.RECYCLE_INDEX_SUFFIX}";
        }

        var cmd = OperationName is OperationType.Copy ? "cp" : "mv";
        var flag = OperationName is OperationType.Copy && FilePath.IsDirectory ? "-r" : "";
        if (OperationName is OperationType.Copy)
            DateModified = DateTime.Now;

        var operationTask = ADBService.ExecuteDeviceAdbShellCommand(Device.ID, CancelTokenSource.Token, cmd, flag,
            ADBService.EscapeAdbShellString(FilePath.FullPath),
            ADBService.EscapeAdbShellString(TargetPath.FullPath));

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

                var res = AdbRegEx.RE_SHELL_ERROR.Matches(t.Result);
                var updates = res.Where(m => m.Success).Select(m => new ShellErrorInfo(m, FilePath.FullPath));
                AddUpdates(updates);

                var message = updates.Last().Message;
                if (message.Contains(':'))
                    message = message.Split(':').Last().TrimStart();

                var errorString = FileOpStatusConverter.StatusString(typeof(ShellErrorInfo),
                                                   failed: Children.Count > 0 ? updates.Count() : -1,
                                                   message: message,
                                                   total: true);

                StatusInfo = new FailedOpProgressViewModel(errorString);

                if (OperationName is OperationType.Recycle)
                {
                    ShellFileOperation.SilentDelete(Device, TargetPath.FullPath, IndexerPath);
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
}
