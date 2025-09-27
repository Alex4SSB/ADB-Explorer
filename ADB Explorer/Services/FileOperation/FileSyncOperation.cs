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

        void SyncProgressCallback(SyncProgressChangedEventArgs eventArgs)
        {
            progressUpdates.Add(new AdbSyncProgressInfo(FilePath.FullPath, (int)eventArgs.ProgressPercentage, null, null));

            TransferEnd = DateTime.Now;
        }

        var targetPath = TargetPath.IsDirectory ? TargetPath.ParentPath : TargetPath.FullPath;
        DateTime lastWriteTime = DateTime.MinValue;
        UnixFileStatus fileMode = UnixFileStatus.AllPermissions | UnixFileStatus.Regular;

        if (OperationName is OperationType.Push)
            lastWriteTime = FilePath.ShellItem.FileInfo.LastWriteTime;

        var task = Task.Run(() =>
        {
            TransferStart = DateTime.Now;
            using SyncService service = new(Device.Device.DeviceData);

            // TODO: Directory push/pull

            if (OperationName is OperationType.Push)
            {
                using var stream = new FileStream(FilePath.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                service.Push(stream, targetPath, fileMode, lastWriteTime, SyncProgressCallback, in isCanceled);
            }
            else
            {
                using var stream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.Read);
                service.Pull(FilePath.FullPath, stream, SyncProgressCallback, in isCanceled);
            }
        });

        task.ContinueWith((t) =>
        {
            progressUpdates.CollectionChanged -= ProgressUpdates_CollectionChanged;
        });

        task.ContinueWith((t) =>
        {
            //((AdbSyncProgressInfo)progressUpdates.LastOrDefault()).TotalPercentage;

            // TODO: Check for errors, cancellations, and skipped files

            Status = OperationStatus.Completed;

            AdbSyncStatsInfo adbInfo = new(FilePath.FullPath, FilePath.Size, (decimal)TransferEnd.Subtract(TransferStart).TotalSeconds);
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

    private void ProgressUpdates_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (Status is not OperationStatus.InProgress)
            return;

        AddUpdates(e.NewItems.Cast<FileOpProgressInfo>());

        if (progressUpdates.LastOrDefault() is AdbSyncProgressInfo currProgress and not null)
        {
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
        AndroidPath.Children.Clear();
        progressUpdates.Clear();
    }

    public override void AddUpdates(IEnumerable<FileOpProgressInfo> newUpdates)
        => AndroidPath.AddUpdates(newUpdates, this);

    public override void AddUpdates(params FileOpProgressInfo[] newUpdates)
        => AndroidPath.AddUpdates(newUpdates, this);

    public static FileSyncOperation PullFile(SyncFile sourcePath, SyncFile targetPath, ADBService.AdbDevice adbDevice, Dispatcher dispatcher)
        => new(OperationType.Pull, sourcePath, targetPath, adbDevice, dispatcher);

    public static FileSyncOperation PushFile(SyncFile sourcePath, SyncFile targetPath, ADBService.AdbDevice adbDevice, Dispatcher dispatcher)
        => new(OperationType.Push, sourcePath, targetPath, adbDevice, dispatcher);
}
