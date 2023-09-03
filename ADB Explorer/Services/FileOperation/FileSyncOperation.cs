using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Services;

public abstract class FileSyncOperation : FileOperation
{
    public delegate AdbSyncStatsInfo FileSyncMethod(
        string targetPath,
        string sourcePath,
        ref ObservableList<FileOpProgressInfo> progressUpdates,
        CancellationToken cancellationToken);

    private readonly FileSyncMethod adbMethod;
    private Task<AdbSyncStatsInfo> operationTask;
    private CancellationTokenSource cancelTokenSource;
    private ObservableList<FileOpProgressInfo> progressUpdates;

    public override SyncFile FilePath { get; }

    public FileSyncOperation(
        Dispatcher dispatcher,
        OperationType operationName,
        FileSyncMethod adbMethod,
        ADBService.AdbDevice adbDevice,
        SyncFile sourcePath,
        SyncFile targetPath) : base(dispatcher, adbDevice, sourcePath)
    {
        OperationName = operationName;
        this.adbMethod = adbMethod;

        FilePath = sourcePath;
        TargetPath = targetPath;
    }

    public override void Start()
    {
        if (Status == OperationStatus.InProgress)
        {
            throw new Exception("Cannot start an already active operation!");
        }

        Status = OperationStatus.InProgress;
        StatusInfo = new InProgSyncProgressViewModel();
        cancelTokenSource = new CancellationTokenSource();

        progressUpdates = new();
        progressUpdates.CollectionChanged += ProgressUpdates_CollectionChanged;

        string target = OperationName is OperationType.Push ? $"{TargetPath.FullPath}/{FilePath.FullName}" : TargetPath.FullPath;

        operationTask = Task.Run(() =>
        {
            return adbMethod(target, FilePath.FullPath, ref progressUpdates, cancelTokenSource.Token);
        }, cancelTokenSource.Token);

        operationTask.ContinueWith((t) => progressUpdates.CollectionChanged -= ProgressUpdates_CollectionChanged);

        operationTask.ContinueWith((t) => 
        {
            Status = OperationStatus.Completed;
            if (t.Result is null)
                StatusInfo = null;
            else if (t.Result.FilesTransferred + t.Result.FilesSkipped < 1)
                StatusInfo = new CompletedShellProgressViewModel();
            else
                StatusInfo = new CompletedSyncProgressViewModel(t.Result);

        }, TaskContinuationOptions.OnlyOnRanToCompletion);

        operationTask.ContinueWith((t) =>
        {
            Status = OperationStatus.Canceled;
            StatusInfo = new CanceledOpProgressViewModel();
        }, TaskContinuationOptions.OnlyOnCanceled);

        operationTask.ContinueWith((t) =>
        {
            string message = string.IsNullOrEmpty(t.Exception.InnerException.Message)
                ? progressUpdates.OfType<SyncErrorInfo>().Last().Message
                : t.Exception.InnerException.Message;

            Status = OperationStatus.Failed;
            StatusInfo = new FailedOpProgressViewModel($"Error: {message}");
        }, TaskContinuationOptions.OnlyOnFaulted);
    }

    private void ProgressUpdates_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (Status is not OperationStatus.InProgress)
            return;

        if (FilePath.PathType is AbstractFile.FilePathType.Android)
            FilePath.AddUpdates(e.NewItems.Cast<FileOpProgressInfo>());
        else
            TargetPath.AddUpdates(e.NewItems.Cast<FileOpProgressInfo>());

        if (progressUpdates.LastOrDefault() is AdbSyncProgressInfo currProgress and not null)
        {
            StatusInfo = new InProgSyncProgressViewModel(currProgress);
        }
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
