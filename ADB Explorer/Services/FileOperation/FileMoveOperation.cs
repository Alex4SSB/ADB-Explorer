using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services.AppInfra;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Services;

public class FileMoveOperation : AbstractShellFileOperation
{
    private CancellationTokenSource cancelTokenSource;
    private readonly ObservableList<FileClass> fileList;
    private string targetParent;
    private string targetName;
    private string fullTargetPath;
    private readonly string currentPath;
    private string recycleName;
    private string indexerPath;
    private DateTime? dateModified;

    public FileMoveOperation(Dispatcher dispatcher, ADBService.AdbDevice adbDevice, FileClass filePath, string targetParent, string targetName, string currentPath, ObservableList<FileClass> fileList, bool isCopy = false)
        : base(dispatcher, adbDevice, filePath)
    {
        if (isCopy)
            OperationName = OperationType.Copy;
        else if (targetParent == AdbExplorerConst.RECYCLE_PATH)
            OperationName = OperationType.Recycle;
        else if (filePath.TrashIndex is not null)
            OperationName = OperationType.Restore;
        else
            OperationName = OperationType.Move;

        TargetPath = new(targetParent, fileType: AbstractFile.FileType.Folder);

        this.fileList = fileList;
        this.targetParent = targetParent;
        this.targetName = targetName;
        this.currentPath = currentPath;
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

        if (OperationName is OperationType.Recycle)
        {
            recycleName = $"{{{DateTimeOffset.Now.ToUnixTimeMilliseconds()}}}";
            fullTargetPath = FileHelper.ConcatPaths(targetParent, recycleName);
            dateModified = FilePath.ModifiedTime;
            indexerPath = $"{AdbExplorerConst.RECYCLE_PATH}/.{recycleName}{AdbExplorerConst.RECYCLE_INDEX_SUFFIX}";
        }
        else
            fullTargetPath = FileHelper.ConcatPaths(targetParent, targetName);

        var cmd = OperationName is OperationType.Copy ? "cp" : "mv";
        var flag = OperationName is OperationType.Copy && FilePath.IsDirectory ? "-r" : "";
        if (OperationName is OperationType.Copy)
            dateModified = DateTime.Now;

        var operationTask = ADBService.ExecuteDeviceAdbShellCommand(Device.ID, cmd, flag,
            ADBService.EscapeAdbShellString(FilePath.FullPath),
            ADBService.EscapeAdbShellString(fullTargetPath));

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
                    if (TargetPath.FullPath == currentPath)
                    {
                        if (OperationName is OperationType.Copy)
                        {
                            FileClass newFile = new(FilePath);
                            newFile.UpdatePath(fullTargetPath);
                            newFile.ModifiedTime = dateModified;
                            fileList.Add(newFile);

                            Data.FileActions.ItemToSelect = newFile;
                        }
                        else
                        {
                            FilePath.UpdatePath(fullTargetPath);
                            fileList.Add(FilePath);

                            Data.FileActions.ItemToSelect = FilePath;
                        }
                    }
                    else if (FilePath.ParentPath == currentPath && OperationName is not OperationType.Copy)
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
                TargetPath.AddUpdates(updates);

                if (TargetPath.Children.Count > 0)
                    StatusInfo = new FailedOpProgressViewModel($"({updates.Count()} Failed)");
                else
                    StatusInfo = new FailedOpProgressViewModel($"Error: {updates.Last().Message}");

                if (OperationName is OperationType.Recycle)
                {
                    ShellFileOperation.SilentDelete(Device, fullTargetPath, indexerPath);
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
