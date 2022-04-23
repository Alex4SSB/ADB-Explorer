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
    public abstract class FileDeleteOperation : FileOperation
    {
        private Task operationTask;
        private CancellationTokenSource cancelTokenSource;

        public FileDeleteOperation(Dispatcher dispatcher, ADBService.AdbDevice adbDevice, FilePath path) : base(dispatcher, adbDevice, path) {}

        public override void Start()
        {
            if (Status == OperationStatus.InProgress)
            {
                throw new Exception("Cannot start an already active operation!");
            }

            Status = OperationStatus.InProgress;
            cancelTokenSource = new CancellationTokenSource();
            operationTask = Task.Run(() => ADBService.ExecuteDeviceAdbCommandAsync(Device.ID, "rm", cancelTokenSource.Token, new[] { "-rf", FilePath.FullPath }), cancelTokenSource.Token);

            operationTask.ContinueWith((t) => 
            {
                Status = OperationStatus.Completed;
                StatusInfo = null;
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
}
