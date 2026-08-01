using ADB_Explorer.Converters;
using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Services;

/// <summary>
/// Creates a tar-family archive on the device from selected paths (or an empty archive).
/// </summary>
public class FileCompressOperation : AbstractShellFileOperation
{
    public IReadOnlyList<string> SourcePaths { get; }

    public FileCompressOperation(
        FileClass archiveFile,
        IReadOnlyList<string> sourcePaths,
        LogicalDeviceViewModel device,
        Dispatcher dispatcher)
        : base(archiveFile, device, dispatcher)
    {
        SourcePaths = sourcePaths;
        TargetPath = new SyncFile(archiveFile);
        OperationName = OperationType.Compress;
    }

    public override void Start()
    {
        if (Status == OperationStatus.InProgress)
            throw new Exception("Cannot start an already active operation!");

        Status = OperationStatus.InProgress;
        StatusInfo = new InProgShellProgressViewModel();

        var operationTask = Task.Run(() =>
        {
            ArchiveExtract.CreateTarArchive(
                Device.ID,
                FilePath.FullPath,
                SourcePaths,
                CancelTokenSource.Token);
        }, CancelTokenSource.Token);

        operationTask.ContinueWith(_ =>
        {
            Status = OperationStatus.Completed;
            StatusInfo = new CompletedShellProgressViewModel();
        }, TaskContinuationOptions.OnlyOnRanToCompletion);

        operationTask.ContinueWith(_ =>
        {
            Status = OperationStatus.Canceled;
            StatusInfo = new CanceledOpProgressViewModel();
        }, TaskContinuationOptions.OnlyOnCanceled);

        operationTask.ContinueWith(t =>
        {
            Status = OperationStatus.Failed;
            var message = t.Exception?.InnerException?.Message ?? t.Exception?.Message ?? "Compress failed";
            StatusInfo = new FailedOpProgressViewModel(FileOpStatusConverter.StatusString(
                typeof(ShellErrorInfo),
                failed: -1,
                message: message,
                total: true));
        }, TaskContinuationOptions.OnlyOnFaulted);
    }
}
