using ADB_Explorer.Converters;
using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;
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

namespace ADB_Explorer.Helpers
{
    public class InProgressTestOperation : FileOperation
    {
        private AdbSyncProgressInfo info;

        private InProgressTestOperation(Dispatcher dispatcher, ADBService.Device adbDevice, string filePath, AdbSyncProgressInfo info) :
            base(dispatcher, adbDevice, filePath)
        {
            this.info = info;
        }

        public static InProgressTestOperation CreateProgressStart(Dispatcher dispatcher, ADBService.Device adbDevice, string filePath)
        {
            return new InProgressTestOperation(dispatcher, adbDevice, filePath, new AdbSyncProgressInfo
            {
                CurrentFile = null,
                TotalPercentage = null,
                CurrentFilePercentage = null,
                CurrentFileBytesTransferred = null
            });
        }

        public static InProgressTestOperation CreateFileInProgress(Dispatcher dispatcher, ADBService.Device adbDevice, string filePath)
        {
            return new InProgressTestOperation(dispatcher, adbDevice, filePath, new AdbSyncProgressInfo
            {
                CurrentFile = null,
                TotalPercentage = 40,
                CurrentFilePercentage = null,
                CurrentFileBytesTransferred = null
            });
        }

        public static InProgressTestOperation CreateFolderInProgress(Dispatcher dispatcher, ADBService.Device adbDevice, string filePath)
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
