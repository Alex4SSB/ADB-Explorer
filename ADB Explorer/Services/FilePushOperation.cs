using System.Windows.Threading;

namespace ADB_Explorer.Services
{
    public class FilePushOperation : FileSyncOperation
    {
        public FilePushOperation(Dispatcher dispatcher, ADBService.Device adbDevice, string sourcePath, string targetPath)
            : base(dispatcher, adbDevice.PushFile, adbDevice, sourcePath, targetPath) {}
    }
}
