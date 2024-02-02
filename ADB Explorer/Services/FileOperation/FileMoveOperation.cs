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
    public readonly bool isLink;

    public FileMoveOperation(FileClass filePath, SyncFile targetPath, ADBService.AdbDevice adbDevice, Dispatcher dispatcher, FileClass.CutType cutType = FileClass.CutType.None)
        : base(filePath, adbDevice, dispatcher)
    {
        if (cutType is FileClass.CutType.Copy or FileClass.CutType.Link)
            OperationName = OperationType.Copy;
        else if (targetPath.FullPath.StartsWith(AdbExplorerConst.RECYCLE_PATH))
        {
            OperationName = OperationType.Recycle;
            AltTarget = NavHistory.SpecialLocation.RecycleBin;
        }
        else if (filePath.TrashIndex is not null)
        {
            OperationName = OperationType.Restore;
            AltSource = NavHistory.SpecialLocation.RecycleBin;
        }
        else
            OperationName = OperationType.Move;

        TargetPath = targetPath;
        isLink = cutType is FileClass.CutType.Link;
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
            IndexerPath = $"{AdbExplorerConst.RECYCLE_PATH}/.{RecycleName}{AdbExplorerConst.RECYCLE_INDEX_SUFFIX}";
        }

        var cmd = OperationName is OperationType.Copy ? "cp" : "mv";
        var flag = "";

        if (OperationName is OperationType.Copy && FilePath.IsDirectory)
            flag += "r";

        if (isLink)
            flag += "s";

        if (flag.Length > 0)
            flag = "-" + flag;

        if (OperationName is OperationType.Copy or OperationType.Recycle)
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

                var res = AdbRegEx.RE_SHELL_ERROR().Matches(t.Result);
                var updates = res.Where(m => m.Success).Select(m => new ShellErrorInfo(m, FilePath.FullPath));
                AddUpdates(updates);

                var message = updates.Any() ? updates.Last().Message : t.Result;
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
