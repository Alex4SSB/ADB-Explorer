using ADB_Explorer.Converters;
using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using static ADB_Explorer.Models.AdbExplorerConst;
using static ADB_Explorer.Services.ADBService.AdbDevice;

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

        public class InProgressInfo
        {
            private AdbSyncProgressInfo adbInfo;

            public InProgressInfo()
            {
                this.adbInfo = null;
            }

            public InProgressInfo(AdbSyncProgressInfo adbInfo)
            {
                this.adbInfo = adbInfo;
            }

            public int? TotalPercentage => adbInfo?.TotalPercentage;
            public int? CurrentFilePercentage => adbInfo?.CurrentFilePercentage;
            public UInt64? CurrentFileBytesTransferred => adbInfo?.CurrentFileBytesTransferred;
            public string CurrentFilePath => adbInfo?.CurrentFile;
            public string CurrentFileName => Path.GetFileName(CurrentFilePath);
            public string CurrentFileNameWithoutExtension => Path.GetFileNameWithoutExtension(CurrentFilePath);

            public string TotalProgress
            {
                get
                {
                    return TotalPercentage.HasValue ? $"{TotalPercentage.Value}%" : "?";
                }
            }

            public string CurrentFileProgress
            {
                get
                {
                    return CurrentFilePercentage.HasValue       ? $"{CurrentFilePercentage.Value}%" :
                           CurrentFileBytesTransferred.HasValue ? SizeConverter.ToSize(CurrentFileBytesTransferred.Value)
                                                                : string.Empty;
                }
            }
        }

        public class CompletedInfo
        {
            private AdbSyncStatsInfo adbInfo;

            public CompletedInfo(AdbSyncStatsInfo adbInfo)
            {
                this.adbInfo = adbInfo;
            }

            public UInt64 FilesTransferred => adbInfo.FilesTransferred;
            public UInt64 FilesSkipped => adbInfo.FilesSkipped;
            public decimal? AverageRateMBps => adbInfo.AverageRate;
            public UInt64? TotalBytes => adbInfo.TotalBytes;
            public decimal? TotalSeconds => adbInfo.TotalTime;
            public int FileCountCompletedRate => (int)((float)FilesTransferred / (FilesTransferred + FilesSkipped) * 100.0);

            public string FileCountCompletedString
            {
                get
                {
                    return $"{FilesTransferred} of {FilesTransferred + FilesSkipped}";
                }
            }

            public string AverageRateString
            {
                get
                {
                    return AverageRateMBps.HasValue ? $"{SizeConverter.ToSize((UInt64)(AverageRateMBps.Value * 1024 * 1024))}/s" : string.Empty;
                }
            }

            public string TotalSize
            {
                get
                {
                    return TotalBytes.HasValue ? SizeConverter.ToSize(TotalBytes.Value) : string.Empty;
                }
            }

            public string TotalTime
            {
                get
                {
                    return TotalSeconds.HasValue ? SizeConverter.ToTime(TotalSeconds.Value) : string.Empty;
                }
            }
        }

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
            StatusInfo = new InProgressInfo();
            waitingProgress = new ConcurrentQueue<AdbSyncProgressInfo>();
            cancelTokenSource = new CancellationTokenSource();

            operationTask = Task.Run(() => adbMethod(TargetPath.FullPath, FilePath.FullPath, ref waitingProgress, cancelTokenSource.Token), cancelTokenSource.Token);

            operationTask.ContinueWith((t) => progressPollTimer.Stop());

            operationTask.ContinueWith((t) => 
            {
                Status = OperationStatus.Completed;
                StatusInfo = t.Result is not null ? new CompletedInfo(t.Result) : null;
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
                StatusInfo = new InProgressInfo(currProgress);
            }
        }
    }
}
