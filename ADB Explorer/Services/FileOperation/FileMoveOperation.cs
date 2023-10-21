using ADB_Explorer.Converters;
using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services.AppInfra;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Services;

public class FileMoveOperation : AbstractShellFileOperation
{
    private readonly ObservableList<FileClass> fileList;
    private string recycleName;
    private string indexerPath;
    private DateTime? dateModified;

    public FileMoveOperation(FileClass filePath, SyncFile targetPath, ObservableList<FileClass> fileList, ADBService.AdbDevice adbDevice, Dispatcher dispatcher, bool isCopy = false)
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

        if (OperationName is OperationType.Recycle)
        {
            recycleName = $"{{{DateTimeOffset.Now.ToUnixTimeMilliseconds()}}}";
            TargetPath.UpdatePath(FileHelper.ConcatPaths(TargetPath.ParentPath, recycleName));
            dateModified = FilePath.ModifiedTime;
            indexerPath = $"{AdbExplorerConst.RECYCLE_PATH}/.{recycleName}{AdbExplorerConst.RECYCLE_INDEX_SUFFIX}";
        }

        var cmd = OperationName is OperationType.Copy ? "cp" : "mv";
        var flag = OperationName is OperationType.Copy && FilePath.IsDirectory ? "-r" : "";
        if (OperationName is OperationType.Copy)
            dateModified = DateTime.Now;

        var operationTask = ADBService.ExecuteDeviceAdbShellCommand(Device.ID, CancelTokenSource.Token, cmd, flag,
            ADBService.EscapeAdbShellString(FilePath.FullPath),
            ADBService.EscapeAdbShellString(TargetPath.FullPath));

        operationTask.ContinueWith((t) =>
        {
            if (t.Result == "")
            {
                Status = OperationStatus.Completed;
                StatusInfo = new CompletedShellProgressViewModel();

                if (OperationName is OperationType.Recycle)
                {
                    var date = dateModified.HasValue ? dateModified.Value.ToString(AdbExplorerConst.ADB_EXPLORER_DATE_FORMAT) : "?";
                    ShellFileOperation.WriteLine(Device, indexerPath, ADBService.EscapeAdbShellString($"{recycleName}|{FilePath.FullPath}|{date}"));
                }

                Dispatcher.Invoke(() =>
                {
                    if (TargetPath.ParentPath == Data.CurrentPath)
                    {
                        if (OperationName is OperationType.Copy)
                        {
                            FileClass newFile = new(FilePath);
                            newFile.UpdatePath(TargetPath.FullPath);
                            newFile.ModifiedTime = dateModified;
                            fileList.Add(newFile);

                            Data.FileActions.ItemToSelect = newFile;
                        }
                        else
                        {
                            FilePath.UpdatePath(TargetPath.FullPath);
                            fileList.Add(FilePath);

                            Data.FileActions.ItemToSelect = FilePath;
                        }
                    }
                    else if (FilePath.ParentPath == Data.CurrentPath && OperationName is not OperationType.Copy)
                    {
                        fileList.Remove(FilePath);
                    }

                    if (OperationName is OperationType.Recycle or OperationType.Restore)
                    {
                        FileActionLogic.RemoveFile(FilePath);
                    }

                    if (OperationName is OperationType.Restore)
                        ShellFileOperation.SilentDelete(Device, indexerPath);
                });
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
                    ShellFileOperation.SilentDelete(Device, TargetPath.FullPath, indexerPath);
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
