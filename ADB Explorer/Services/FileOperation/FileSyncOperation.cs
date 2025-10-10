using ADB_Explorer.Converters;
using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.ViewModels;
using AdvancedSharpAdbClient;
using AdvancedSharpAdbClient.Models;
using Vanara.Windows.Shell;

namespace ADB_Explorer.Services;

public class FileSyncOperation : FileOperation
{
    private CancellationTokenSource cancelTokenSource;
    private ObservableList<FileOpProgressInfo> progressUpdates;

    public override SyncFile FilePath { get; }

    public override SyncFile AndroidPath => FilePath.PathType is AbstractFile.FilePathType.Android
        ? FilePath
        : TargetPath;

    public VirtualFileDataObject VFDO { get; set; }

    public ShellItem OriginalShellItem { get; set; }

    public DateTime TransferStart { get; private set; }
    public DateTime TransferEnd { get; private set; }

    private ulong TotalBytes;
    IEnumerable<SyncFile> Files = null;

    private bool isCanceled = false;

    public FileSyncOperation(OperationType operationName, FileDescriptor sourcePath, SyncFile targetPath, ADBService.AdbDevice adbDevice, FailedOpProgressViewModel status)
        : base(new FileClass(sourcePath), adbDevice, App.Current.Dispatcher)
    {
        OperationName = operationName;
        FilePath = new(new FileClass(sourcePath));
        TargetPath = targetPath;

        StatusInfo = status;
        Status = OperationStatus.Failed;
        AltSource = new(Navigation.SpecialLocation.Unknown);
    }

    public FileSyncOperation(
        OperationType operationName,
        SyncFile sourcePath,
        SyncFile targetPath,
        ADBService.AdbDevice adbDevice,
        Dispatcher dispatcher) : base(sourcePath, adbDevice, dispatcher)
    {
        OperationName = operationName;
        FilePath = sourcePath;
        TargetPath = targetPath;
    }

    public override void Start()
    {
        if (Status == OperationStatus.InProgress)
        {
            throw new Exception("Cannot start an already active operation!");
        }

        Status = OperationStatus.InProgress;
        StatusInfo = new InProgSyncProgressViewModel();
        cancelTokenSource = new CancellationTokenSource();

        progressUpdates = [];
        progressUpdates.CollectionChanged += ProgressUpdates_CollectionChanged;

        if (OperationName is OperationType.Push &&
            !File.Exists(FilePath.FullPath) && !Directory.Exists(FilePath.FullPath))
        {
            Status = OperationStatus.Failed;
            StatusInfo = new FailedOpProgressViewModel(FileOpStatusConverter.StatusString(typeof(SyncErrorInfo), message: Strings.Resources.S_SYNC_FILE_NOT_FOUND, total: true));

            return;
        }

        DateTime lastWriteTime = DateTime.MinValue;
        UnixFileStatus fileMode = UnixFileStatus.AllPermissions | UnixFileStatus.Regular;

        if (OperationName is OperationType.Push)
            lastWriteTime = FilePath.ShellItem.FileInfo.LastWriteTime;

        Mutex mutex = new();

        var task = Task.Run(() =>
        {
            TransferStart = DateTime.Now;

            Files = [FilePath, .. FilePath.AllChildren()];
            TotalBytes = (ulong)Files.Sum(f => (decimal?)f.Size);

            if (OperationName is OperationType.Push)
            {
                var paths = FolderHelper.GetBottomMostFolders(Files)
                    .Select(f => FileHelper.ConcatPaths(TargetPath.FullPath, FileHelper.ExtractRelativePath(f.FullPath, FilePath.FullPath, false)));

                ShellFileOperation.MakeDirs(Device, paths);
            }
            else
            {
                foreach (var dir in FolderHelper.GetBottomMostFolders(Files))
                {
                    var targetDirPath = FileHelper.ConcatPaths(TargetPath, FileHelper.ExtractRelativePath(dir.FullPath, FilePath.FullPath, false));
                    Directory.CreateDirectory(targetDirPath);
                }
            }

            Parallel.ForEach(Files.Where(f => !f.IsDirectory), (item) =>
            {
                if (OperationName is OperationType.Push)
                {
                    // target = [Android parent folder]\[relative path from Windows parent folder to current item]
                    var targetPath = FileHelper.ConcatPaths(TargetPath, FileHelper.ExtractRelativePath(item.FullPath, FilePath.FullPath));
                    using var stream = new FileStream(item.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    using SyncService service = new(Device.Device.DeviceData);

                    service.Push(stream, targetPath, fileMode, lastWriteTime, SyncProgressCallback, in isCanceled);

                    void SyncProgressCallback(SyncProgressChangedEventArgs eventArgs) => AddUpdates(item, eventArgs, mutex);
                }
                else
                {
                    // target = [Windows parent folder]\[relative path from Android parent folder to current item]
                    var targetPath = FileHelper.ConcatPaths(TargetPath, FileHelper.ExtractRelativePath(item.FullPath, FilePath.FullPath));
                    using var stream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.Read);
                    using SyncService service = new(Device.Device.DeviceData);

                    service.Pull(item.FullPath, stream, SyncProgressCallback, in isCanceled);

                    void SyncProgressCallback(SyncProgressChangedEventArgs eventArgs) => AddUpdates(item, eventArgs, mutex);
                }
            });

        });

        task.ContinueWith((t) =>
        {
            progressUpdates.CollectionChanged -= ProgressUpdates_CollectionChanged;
        });

        task.ContinueWith((t) =>
        {
            Status = OperationStatus.Completed;

            var totalBytes = (ulong)Files.Where(f => !f.IsDirectory).Sum(f => (decimal)f.Size);
            var completed = (ulong)Files.Where(f => !f.IsDirectory).Count(f => f.ProgressUpdates.LastOrDefault() is AdbSyncProgressInfo p && p.CurrentFilePercentage == 100);

            AdbSyncStatsInfo adbInfo = new(FilePath.FullPath, totalBytes, (decimal)TransferEnd.Subtract(TransferStart).TotalSeconds, completed);
            StatusInfo = new CompletedSyncProgressViewModel(adbInfo);

        }, TaskContinuationOptions.OnlyOnRanToCompletion);

        task.ContinueWith((t) =>
        {
            Status = OperationStatus.Canceled;
            StatusInfo = new CanceledOpProgressViewModel();
        }, TaskContinuationOptions.OnlyOnCanceled);

        task.ContinueWith((t) =>
        {
            string message = string.IsNullOrEmpty(t.Exception.InnerException.Message)
                ? progressUpdates.OfType<SyncErrorInfo>().Last().Message
                : t.Exception.InnerException.Message;

            Status = OperationStatus.Failed;
            StatusInfo = new FailedOpProgressViewModel(FileOpStatusConverter.StatusString(typeof(SyncErrorInfo), message: message, total: true));
        }, TaskContinuationOptions.OnlyOnFaulted);
    }

    private void AddUpdates(SyncFile item, SyncProgressChangedEventArgs eventArgs, Mutex mutex)
    {
        item.Size ??= (ulong)eventArgs.TotalBytesToReceive;

        mutex.WaitOne();
        progressUpdates.Add(new AdbSyncProgressInfo(item.FullPath, null, (int)eventArgs.ProgressPercentage, (ulong)eventArgs.ReceivedBytesSize));
        mutex.ReleaseMutex();

        TransferEnd = DateTime.Now;
    }

    private void ProgressUpdates_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (Status is not OperationStatus.InProgress)
            return;

        AddUpdates(e.NewItems.Cast<FileOpProgressInfo>());

        if (progressUpdates.LastOrDefault() is AdbSyncProgressInfo currProgress and not null)
        {
            if (currProgress.TotalPercentage is null)
            {
                var total = (float)Files.Sum(f => (decimal?)((AdbSyncProgressInfo)f.ProgressUpdates.LastOrDefault())?.CurrentFileBytesTransferred) / TotalBytes;
                currProgress.TotalPercentage = (int)(total * 100);
            }

            StatusInfo = new InProgSyncProgressViewModel(currProgress);
        }
    }

    public override void Cancel()
    {
        if (Status != OperationStatus.InProgress)
        {
            throw new Exception("Cannot cancel a deactivated operation!");
        }

        isCanceled = true;
        cancelTokenSource.Cancel();
    }

    public override void ClearChildren()
    {
        FilePath.Children.Clear();
        progressUpdates.Clear();
    }

    public override void AddUpdates(IEnumerable<FileOpProgressInfo> newUpdates)
        => FilePath.AddUpdates(newUpdates, this);

    public override void AddUpdates(params FileOpProgressInfo[] newUpdates)
        => FilePath.AddUpdates(newUpdates, this);

    public static FileSyncOperation PullFile(SyncFile sourcePath, SyncFile targetPath, ADBService.AdbDevice adbDevice, Dispatcher dispatcher)
        => new(OperationType.Pull, sourcePath, targetPath, adbDevice, dispatcher);

    public static FileSyncOperation PushFile(SyncFile sourcePath, SyncFile targetPath, ADBService.AdbDevice adbDevice, Dispatcher dispatcher)
        => new(OperationType.Push, sourcePath, targetPath, adbDevice, dispatcher);
}
