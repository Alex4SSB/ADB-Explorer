using ADB_Explorer.Converters;
using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Services;

/// <summary>
/// Extracts a selected archive member (file or directory) to a real device path.
/// Used when pasting archive clipboard items onto the device.
/// </summary>
public class FileExtractOperation : AbstractShellFileOperation
{
    public string ArchiveSourcePath { get; }
    public string ArchiveInternalPath { get; }
    public bool IsArchiveDirectory { get; }

    public FileExtractOperation(
        FileClass source,
        SyncFile targetPath,
        LogicalDeviceViewModel device,
        Dispatcher dispatcher)
        : base(source, device, dispatcher)
    {
        if (!ArchivePath.TryParse(source.FullPath, out var archivePath, out var internalPath, device.ID))
            throw new ArgumentException("Source is not an archive path.", nameof(source));

        ArchiveSourcePath = archivePath;
        ArchiveInternalPath = internalPath;
        IsArchiveDirectory = source.IsDirectory;
        TargetPath = targetPath;
        OperationName = OperationType.Copy;
    }

    public override void Start()
    {
        if (Status == OperationStatus.InProgress)
            throw new Exception("Cannot start an already active operation!");

        Status = OperationStatus.InProgress;
        StatusInfo = new InProgShellProgressViewModel();

        var operationTask = Task.Run(() =>
        {
            ArchiveExtract.ExtractSelection(
                Device.ID,
                ArchiveSourcePath,
                ArchiveInternalPath,
                IsArchiveDirectory,
                TargetPath.FullPath,
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
            var message = t.Exception?.InnerException?.Message ?? t.Exception?.Message ?? "Extract failed";
            StatusInfo = new FailedOpProgressViewModel(FileOpStatusConverter.StatusString(
                typeof(ShellErrorInfo),
                failed: -1,
                message: message,
                total: true));
        }, TaskContinuationOptions.OnlyOnFaulted);
    }
}
