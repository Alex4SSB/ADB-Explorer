using ADB_Explorer.Models;

namespace ADB_Explorer.Services;

public class FilePushOperation : FileSyncOperation
{
    public FilePushOperation(Dispatcher dispatcher, ADBService.AdbDevice adbDevice, SyncFile sourcePath, SyncFile targetPath)
        : base(dispatcher, OperationType.Push, adbDevice.PushFile, adbDevice, sourcePath, targetPath) {}
}
