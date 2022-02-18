using ADB_Explorer.Models;
using ADB_Explorer.Services;
using System.Windows.Threading;
using static ADB_Explorer.Services.ADBService.AdbDevice;

namespace ADB_Explorer.Helpers
{
    public class InProgressTestOperation : FileOperation
    {
        private FileSyncOperation.InProgressInfo info;

        private InProgressTestOperation(Dispatcher dispatcher, ADBService.AdbDevice adbDevice, string filePath, AdbSyncProgressInfo adbInfo) :
            base(dispatcher, adbDevice, new FilePath(filePath))
        {
            this.info = new FileSyncOperation.InProgressInfo(adbInfo);
        }

        public static InProgressTestOperation CreateProgressStart(Dispatcher dispatcher, ADBService.AdbDevice adbDevice, string filePath)
        {
            return new InProgressTestOperation(dispatcher, adbDevice, filePath, new AdbSyncProgressInfo
            {
                CurrentFile = null,
                TotalPercentage = null,
                CurrentFilePercentage = null,
                CurrentFileBytesTransferred = null
            });
        }

        public static InProgressTestOperation CreateFileInProgress(Dispatcher dispatcher, ADBService.AdbDevice adbDevice, string filePath)
        {
            return new InProgressTestOperation(dispatcher, adbDevice, filePath, new AdbSyncProgressInfo
            {
                CurrentFile = null,
                TotalPercentage = 40,
                CurrentFilePercentage = null,
                CurrentFileBytesTransferred = null
            });
        }

        public static InProgressTestOperation CreateFolderInProgress(Dispatcher dispatcher, ADBService.AdbDevice adbDevice, string filePath)
        {
            return new InProgressTestOperation(dispatcher, adbDevice, filePath, new AdbSyncProgressInfo
            {
                CurrentFile = "Moshe.txt",
                TotalPercentage = 40,
                CurrentFilePercentage = 60,
                CurrentFileBytesTransferred = null
            });
        }

        public override void Start()
        {
            Status = OperationStatus.InProgress;
            StatusInfo = info;
        }

        public override void Cancel()
        {
            Status = OperationStatus.Canceled;
            StatusInfo = null;
        }
    }
}
