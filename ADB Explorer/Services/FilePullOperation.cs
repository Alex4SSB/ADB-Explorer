using System.Windows.Threading;

namespace ADB_Explorer.Services
{
    public class FilePullOperation : FileSyncOperation
    {
        public FilePullOperation(Dispatcher dispatcher, ADBService.AdbDevice adbDevice, string sourcePath, string targetPath)
            : base(dispatcher, "Pull", adbDevice.PullFile, adbDevice, sourcePath, targetPath) { }
    }
}
