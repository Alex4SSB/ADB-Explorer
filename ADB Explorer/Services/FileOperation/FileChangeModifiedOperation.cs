using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace ADB_Explorer.Services
{
    public class FileChangeModifiedOperation : FileOperation
    {
        private Task operationTask;
        private CancellationTokenSource cancelTokenSource;
        private readonly DateTime newDate;

        public FileChangeModifiedOperation(Dispatcher dispatcher, ADBService.AdbDevice adbDevice, FilePath filePath, ObservableList<FileClass> fileList, DateTime newDate) : base(dispatcher, adbDevice, filePath)
        {
            OperationName = OperationType.Update;
            this.newDate = newDate;
        }

        public override void Start()
        {
            if (Status == OperationStatus.InProgress)
            {
                throw new Exception("Cannot start an already active operation!");
            }

            Status = OperationStatus.InProgress;
            cancelTokenSource = new CancellationTokenSource();

            operationTask = Task.Run(() => ADBService.ExecuteDeviceAdbShellCommand(Device.ID, "touch", out _, out _, new[] { "-m", "-t", newDate.ToString("yyyyMMddHHmm.ss"), ADBService.EscapeAdbShellString(FilePath.FullPath) }));

            operationTask.ContinueWith((t) =>
            {
                var operationStatus = ((Task<int>)t).Result == 0 ? OperationStatus.Completed : OperationStatus.Failed;
                Status = operationStatus;
                StatusInfo = null;

                Dispatcher.Invoke(() =>
                {
                    //fileList.Find(file => file.FullPath == FilePath.fullp).ModifiedTime = newDate;
                    ((FileClass)FilePath).ModifiedTime = newDate;
                });

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
