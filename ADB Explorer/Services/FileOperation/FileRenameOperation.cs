using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Services;

public class FileRenameOperation : AbstractShellFileOperation
{
    public FileRenameOperation(FileClass filePath, string targetPath, LogicalDeviceViewModel device, Dispatcher dispatcher)
        : base(new(filePath), device, dispatcher)
    {
        OperationName = OperationType.Rename;

        TargetPath = new(targetPath, FilePath.Type);
    }

    public override void Start()
    {
        if (Status == OperationStatus.InProgress)
        {
            throw new Exception("Cannot start an already active operation!");
        }

        Status = OperationStatus.InProgress;
        StatusInfo = new InProgShellProgressViewModel();
        CancelTokenSource = new();

        Task operationTask;
        if (ArchivePath.TryParse(FilePath.FullPath, out var archivePath, out var oldInternal, Device.ID)
            && ArchivePath.TryParse(TargetPath.FullPath, out var destArchive, out var newInternal, Device.ID)
            && archivePath == destArchive
            && !string.IsNullOrEmpty(oldInternal)
            && !string.IsNullOrEmpty(newInternal)
            && ArchiveHelper.CanPasteIntoArchive(FilePath.FullPath, Device.ID))
        {
            operationTask = Task.Run(() =>
            {
                ArchiveExtract.RenameTarMember(
                    Device.ID,
                    archivePath,
                    oldInternal,
                    newInternal,
                    CancelTokenSource.Token);
            }, CancelTokenSource.Token);
        }
        else
        {
            operationTask = ADBService.ExecuteVoidShellCommand(Device.ID,
                CancelTokenSource.Token,
                "mv",
                ADBService.EscapeAdbShellString(FilePath.FullPath),
                ADBService.EscapeAdbShellString(TargetPath.FullPath));
        }

        operationTask.ContinueWith((t) =>
        {
            if (t is Task<string> shellTask)
            {
                if (shellTask.Result == "")
                {
                    Status = OperationStatus.Completed;
                    StatusInfo = new CompletedShellProgressViewModel();
                }
                else
                {
                    Status = OperationStatus.Failed;
                    StatusInfo = new FailedOpProgressViewModel(shellTask.Result);
                }
            }
            else
            {
                Status = OperationStatus.Completed;
                StatusInfo = new CompletedShellProgressViewModel();
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
            var message = t.Exception?.InnerException?.Message ?? t.Exception?.Message ?? "Rename failed";
            StatusInfo = new FailedOpProgressViewModel(message);
        }, TaskContinuationOptions.OnlyOnFaulted);
    }
}
