using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace ADB_Explorer.Services
{
    public class FileMoveOperation : FileOperation
    {
        private Task operationTask;
        private CancellationTokenSource cancelTokenSource;
        private readonly ObservableList<FileClass> fileList;
        private string targetPath;
        private readonly string currentPath;
        private string recycleName;

        public FileMoveOperation(Dispatcher dispatcher, ADBService.AdbDevice adbDevice, FilePath filePath, string targetPath, string currentPath, ObservableList<FileClass> fileList) : base(dispatcher, adbDevice, filePath)
        {
            OperationName = targetPath == AdbExplorerConst.RECYCLE_PATH ? OperationType.Recycle : OperationType.Move;
            this.fileList = fileList;
            this.targetPath = targetPath;
            this.currentPath = currentPath;

            TargetPath = new(targetPath, fileType: Converters.FileTypeClass.FileType.Folder);
        }

        public override void Start()
        {
            if (Status == OperationStatus.InProgress)
            {
                throw new Exception("Cannot start an already active operation!");
            }

            Status = OperationStatus.InProgress;
            cancelTokenSource = new CancellationTokenSource();

            operationTask = Task.Run(() =>
            {
                if (OperationName is OperationType.Recycle)
                {
                    recycleName = $"{{{DateTimeOffset.Now.ToUnixTimeMilliseconds()}}}";
                    targetPath = $"{targetPath}/{recycleName}";
                    ShellFileOperation.MakeDir(Device, targetPath);
                }

                return ADBService.ExecuteDeviceAdbShellCommand(Device.ID, "mv", out _, out _, new[] {
                    ADBService.EscapeAdbShellString(FilePath.FullPath),
                    ADBService.EscapeAdbShellString($"{targetPath}{(targetPath.EndsWith('/') ? "" : "/")}{FilePath.FullName}") });
            });

            operationTask.ContinueWith((t) =>
            {
                var operationStatus = ((Task<int>)t).Result == 0 ? OperationStatus.Completed : OperationStatus.Failed;
                Status = operationStatus;
                StatusInfo = null;

                if (operationStatus is OperationStatus.Completed)
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (targetPath == currentPath)
                        {
                            FilePath.UpdatePath($"{targetPath}/{FilePath.FullName}");
                            fileList.Add((FileClass)FilePath);
                        }
                        else if (FilePath.ParentPath == currentPath)
                        {
                            fileList.Remove((FileClass)FilePath);
                        }
                    });

                    if (OperationName is OperationType.Recycle)
                    {
                        ShellFileOperation.WriteLine(Device, $"{AdbExplorerConst.RECYCLE_PATH}/.RecycleIndex", ADBService.EscapeAdbShellString($"{recycleName};{FilePath.ParentPath}"));
                        Data.AddDummyRecycledItems(Dispatcher);
                    }
                }

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
