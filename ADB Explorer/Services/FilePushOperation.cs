using System.Windows.Threading;

namespace ADB_Explorer.Services
{
    public class FilePushOperation : FileSyncOperation
    {
        public FilePushOperation(Dispatcher dispatcher, ADBService.AdbDevice adbDevice, string sourcePath, string targetPath)
            : base(dispatcher, "Push", adbDevice.PushFile, adbDevice, sourcePath, targetPath) {}
    }
}
