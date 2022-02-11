using ADB_Explorer.Models;
using System.Windows.Threading;

namespace ADB_Explorer.Services
{
    public class FilePullOperation : FileSyncOperation
    {
        public FilePullOperation(Dispatcher dispatcher, ADBService.AdbDevice adbDevice, FilePath sourcePath, FilePath targetPath)
            : base(dispatcher, "Pull", adbDevice.PullFile, adbDevice, sourcePath, targetPath) { }
    }
}
