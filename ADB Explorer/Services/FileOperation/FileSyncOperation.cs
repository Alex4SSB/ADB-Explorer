using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.ViewModels;
using static ADB_Explorer.Models.AdbExplorerConst;
using static ADB_Explorer.Services.ADBService.AdbDevice;

namespace ADB_Explorer.Services;

public abstract class FileSyncOperation : FileOperation
{
    public delegate AdbSyncStatsInfo FileSyncMethod(
        string targetPath,
        string sourcePath,
        ref ConcurrentQueue<AdbSyncProgressInfo> progressUpdates,
        CancellationToken cancellationToken);

    private FileSyncMethod adbMethod;
    private Task<AdbSyncStatsInfo> operationTask;
    private CancellationTokenSource cancelTokenSource;
    private ConcurrentQueue<AdbSyncProgressInfo> waitingProgress;
    private System.Timers.Timer progressPollTimer;

    public FileSyncOperation(
        Dispatcher dispatcher,
        OperationType operationName,
        FileSyncMethod adbMethod,
        ADBService.AdbDevice adbDevice,
        FilePath sourcePath,
        FilePath targetPath) : base(dispatcher, adbDevice, sourcePath)
    {
        OperationName = operationName;
        TargetPath = targetPath;
        this.adbMethod = adbMethod;

        // Configure progress polling timer
        progressPollTimer = new()
        {
            Interval = SYNC_PROG_UPDATE_INTERVAL.TotalMilliseconds,
            AutoReset = true
        };

        progressPollTimer.Elapsed += ProgressPollTimerHandler;
    }

    public override void Start()
    {
        if (Status == OperationStatus.InProgress)
        {
            throw new Exception("Cannot start an already active operation!");
        }

        Status = OperationStatus.InProgress;
        StatusInfo = new InProgSyncProgressViewModel();
        waitingProgress = new ConcurrentQueue<AdbSyncProgressInfo>();
        cancelTokenSource = new CancellationTokenSource();

        string target = OperationName is OperationType.Push ? $"{TargetPath.FullPath}/{FilePath.FullName}" : TargetPath.FullPath;

        operationTask = Task.Run(() =>
        {
            return adbMethod(target, FilePath.FullPath, ref waitingProgress, cancelTokenSource.Token);
        }, cancelTokenSource.Token);

        operationTask.ContinueWith((t) => progressPollTimer.Stop());

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
            Status = OperationStatus.Failed;
            StatusInfo = new FailedOpProgressViewModel(t.Exception.InnerException.Message);
        }, TaskContinuationOptions.OnlyOnFaulted);

        progressPollTimer.Start();
    }

    public override void Cancel()
    {
        if (Status != OperationStatus.InProgress)
        {
            throw new Exception("Cannot cancel a deactivated operation!");
        }

        cancelTokenSource.Cancel();
    }

    private void ProgressPollTimerHandler(object sender, System.Timers.ElapsedEventArgs e)
    {
        var currProgress = waitingProgress.DequeueAllExisting().LastOrDefault();
        if ((Status == OperationStatus.InProgress) && (currProgress != null))
        {
            StatusInfo = new InProgSyncProgressViewModel(currProgress);
        }
    }
}
