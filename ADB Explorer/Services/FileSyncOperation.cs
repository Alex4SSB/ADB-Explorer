using ADB_Explorer.Helpers;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using static ADB_Explorer.Models.AdbExplorerConst;
using static ADB_Explorer.Services.ADBService.Device;

namespace ADB_Explorer.Services
{
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

        public string TargetPath { get; }

        public FileSyncOperation(
            Dispatcher dispatcher,
            string operationName,
            FileSyncMethod adbMethod,
            ADBService.Device adbDevice,
            string sourcePath,
            string targetPath) : base(dispatcher, adbDevice, sourcePath)
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
            waitingProgress = new ConcurrentQueue<AdbSyncProgressInfo>();
            cancelTokenSource = new CancellationTokenSource();

            operationTask = Task.Run(() => adbMethod(TargetPath, FilePath, ref waitingProgress, cancelTokenSource.Token));

            operationTask.ContinueWith((t) => progressPollTimer.Stop());
            operationTask.ContinueWith((t) => { Status = OperationStatus.Completed; StatusInfo = t.Result; }, TaskContinuationOptions.OnlyOnRanToCompletion);
            operationTask.ContinueWith((t) => { Status = OperationStatus.Canceled; StatusInfo = new Exception("Canceled by user"); }, TaskContinuationOptions.OnlyOnCanceled);
            operationTask.ContinueWith((t) => { Status = OperationStatus.Failed; StatusInfo = t.Exception.InnerException; }, TaskContinuationOptions.OnlyOnFaulted);

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
                StatusInfo = currProgress;
            }
        }
    }
}
