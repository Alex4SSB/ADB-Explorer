using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services.AppInfra;

namespace ADB_Explorer.Services;

public class FileMoveOperation : FileOperation
{
    private Task operationTask;
    private CancellationTokenSource cancelTokenSource;
    private readonly ObservableList<FileClass> fileList;
    private string targetParent;
    private string targetName;
    private string fullTargetPath;
    private readonly string currentPath;
    private string recycleName;
    private string indexerPath;
    private DateTime? dateModified;

    public FileMoveOperation(Dispatcher dispatcher, ADBService.AdbDevice adbDevice, FilePath filePath, string targetParent, string targetName, string currentPath, ObservableList<FileClass> fileList, bool isCopy = false)
        : base(dispatcher, adbDevice, filePath)
    {
        if (isCopy)
            OperationName = OperationType.Copy;
        else if (targetParent == AdbExplorerConst.RECYCLE_PATH)
            OperationName = OperationType.Recycle;
        else if (((FileClass)filePath).TrashIndex is not null)
            OperationName = OperationType.Restore;
        else
            OperationName = OperationType.Move;

        TargetPath = new(targetParent, fileType: Converters.FileTypeClass.FileType.Folder);

        if (!targetParent.EndsWith('/'))
            targetParent += '/';

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
        cancelTokenSource = new CancellationTokenSource();

        operationTask = Task.Run(() =>
        {
            if (OperationName is OperationType.Recycle)
            {
                recycleName = $"{{{DateTimeOffset.Now.ToUnixTimeMilliseconds()}}}";
                fullTargetPath = $"{targetParent}{recycleName}";
                dateModified = ((FileClass)FilePath).ModifiedTime;
                indexerPath = $"{AdbExplorerConst.RECYCLE_PATH}/.{recycleName}{AdbExplorerConst.RECYCLE_INDEX_SUFFIX}";
            }
            else
                fullTargetPath = $"{targetParent}{targetName}";
            
            var command = OperationName is OperationType.Copy ? "cp" : "mv";
            if (OperationName is OperationType.Copy)
                dateModified = DateTime.Now;

            return ADBService.ExecuteDeviceAdbShellCommand(Device.ID, command, out _, out _, new[] {
                OperationName is OperationType.Copy && FilePath.IsDirectory ? "-r" : "",
                ADBService.EscapeAdbShellString(FilePath.FullPath),
                ADBService.EscapeAdbShellString(fullTargetPath) });
        });

        operationTask.ContinueWith((t) =>
        {
            var operationStatus = ((Task<int>)t).Result == 0 ? OperationStatus.Completed : OperationStatus.Failed;
            Status = operationStatus;
            StatusInfo = null;

            if (operationStatus is OperationStatus.Completed)
            {
                switch (OperationName)
                {
                    case OperationType.Recycle:
                        var date = dateModified.HasValue ? dateModified.Value.ToString(AdbExplorerConst.ADB_EXPLORER_DATE_FORMAT) : "?";
                        ShellFileOperation.WriteLine(Device, indexerPath, ADBService.EscapeAdbShellString($"{recycleName}|{FilePath.FullPath}|{date}"));
                        break;
                    default:
                        break;
                }

                Dispatcher.Invoke(() =>
                {
                    if (TargetPath.FullPath == currentPath)
                    {
                        if (OperationName is OperationType.Copy)
                        {
                            FileClass newFile = new((FileClass)FilePath);
                            newFile.UpdatePath(fullTargetPath);
                            newFile.ModifiedTime = dateModified;
                            fileList.Add(newFile);

                            Data.FileActions.ItemToSelect = newFile;
                        }
                        else
                        {
                            FilePath.UpdatePath(fullTargetPath);
                            fileList.Add((FileClass)FilePath);

                            Data.FileActions.ItemToSelect = FilePath;
                        }
                    }
                    else if (FilePath.ParentPath == currentPath && OperationName is not OperationType.Copy)
                    {
                        fileList.Remove((FileClass)FilePath);
                    }

                    if (OperationName is OperationType.Recycle or OperationType.Restore)
                    {
                        FileActionLogic.RemoveFile((FileClass)FilePath);
                    }

                    if (OperationName is OperationType.Restore)
                        ShellFileOperation.SilentDelete(Device, indexerPath);
                });
            }
            else if (OperationName is OperationType.Recycle)
            {
                ShellFileOperation.SilentDelete(Device, fullTargetPath, indexerPath);
            }

        }, TaskContinuationOptions.OnlyOnRanToCompletion);

        operationTask.ContinueWith((t) =>
        {
            Status = OperationStatus.Canceled;
            StatusInfo = null;
        }, TaskContinuationOptions.OnlyOnCanceled);

        operationTask.ContinueWith((t) =>
        {
            Status = OperationStatus.Failed;
            StatusInfo = t.Exception.InnerException.Message;
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
