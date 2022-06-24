using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace ADB_Explorer.Services
{
    public class PackageInstallOperation : FileOperation
    {
        private Task operationTask;
        private CancellationTokenSource cancelTokenSource;

        private string packageName;
        public string PackageName
        {
            get => packageName;
            set => Set(ref packageName, value);
        }

        public PackageInstallOperation(Dispatcher dispatcher, ADBService.AdbDevice adbDevice, FilePath path = null, string packageName = null) : base(dispatcher, adbDevice, path)
        {
            OperationName = OperationType.Install;
            PackageName = packageName;
        }

        public override void Start()
        {
            if (Status == OperationStatus.InProgress)
            {
                throw new Exception("Cannot start an already active operation!");
            }

            Status = OperationStatus.InProgress;
            cancelTokenSource = new CancellationTokenSource();
            var args = new string[2];
            if (string.IsNullOrEmpty(PackageName))
            {
                args[0] = "install";
                args[1] = FilePath.FullPath;
            }
            else
            {
                args[0] = "uninstall";
                args[1] = PackageName;
            }

            args[1] = ADBService.EscapeAdbShellString(args[1]);
            operationTask = Task.Run(() => ADBService.ExecuteDeviceAdbShellCommand(Device.ID, "pm", out _, out _, args));

            operationTask.ContinueWith((t) =>
            {
                var operationStatus = ((Task<int>)t).Result == 0 ? OperationStatus.Completed : OperationStatus.Failed;
                Status = operationStatus;
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
