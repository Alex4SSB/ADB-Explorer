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

    public VirtualFileDataObject VFDO { get; set; } = null;

    private DragDropEffects dropEffects = DragDropEffects.None;
    public DragDropEffects DropEffects
    {
        get
        {
            return VFDO is null ? dropEffects : VFDO.CurrentEffect;
        }
        set
        {
            dropEffects = value;
        }
    }

    public ShellItem OriginalShellItem { get; set; }

    public DateTime TransferStart { get; private set; }
    public DateTime TransferEnd { get; private set; }

    IEnumerable<SyncFile> Files => [FilePath, .. FilePath.AllChildren()];
    private long? TotalBytes => Files.Sum(f => f.Size);
    private IEnumerable<SyncFile> ActiveFiles => Files.Where(f => f.CurrentPercentage is > 0 and < 100);

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

        UnixFileStatus fileMode = UnixFileStatus.AllPermissions | UnixFileStatus.Regular;

        Mutex mutex = new();

        var task = Task.Run(() =>
        {
            TransferStart = DateTime.Now;

            if (OperationName is OperationType.Push)
            {
                var paths = FolderHelper.GetBottomMostFolders(Files)
                    .Select(f => FileHelper.ConcatPaths(TargetPath.FullPath, FileHelper.ExtractRelativePath(f.FullPath, FilePath.FullPath, false)));

                if (paths.Any())
                    ShellFileOperation.MakeDirs(Device, paths);
            }
            else
            {
                foreach (var dir in FolderHelper.GetBottomMostFolders(Files))
                {
                    var targetDirPath = FileHelper.ConcatPaths(TargetPath, FileHelper.ExtractRelativePath(dir.FullPath, FilePath.FullPath, false));
                    Directory.CreateDirectory(targetDirPath);

                    if (Data.Settings.EnableLog && !Data.RuntimeSettings.IsLogPaused)
                        Data.CommandLog.Add(new($"@Windows: mkdir {targetDirPath}"));
                }
            }

            ParallelOptions options = new()
            {
                MaxDegreeOfParallelism = Data.Settings.AllowMultiOp ? -1 : 1
            };

            Parallel.ForEach(Files.Where(f => !f.IsDirectory), options, (item) =>
            {
                void SyncProgressCallback(SyncProgressChangedEventArgs eventArgs) => AddUpdates(item, eventArgs, mutex);

                using SyncService service = new(Device.Device.DeviceData);
                var targetPath = FilePath.IsDirectory
                        ? FileHelper.ConcatPaths(TargetPath, FileHelper.ExtractRelativePath(item.FullPath, FilePath.FullPath))
                        : TargetPath.FullPath;

                if (OperationName is OperationType.Push)
                {
                    if (Data.Settings.EnableLog && !Data.RuntimeSettings.IsLogPaused)
                        Data.CommandLog.Add(new($"@AdvancedSharpAdbClient: push {item.FullPath} -> {targetPath}"));

                    var lastWriteTime = item.DateModified ?? DateTime.Now;

                    // target = [Android parent folder]\[relative path from Windows parent folder to current item]
                    using var stream = new FileStream(item.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    service.Push(stream, targetPath, fileMode, lastWriteTime, SyncProgressCallback, in isCanceled);
                }
                else
                {
                    if (Data.Settings.EnableLog && !Data.RuntimeSettings.IsLogPaused)
                        Data.CommandLog.Add(new($"@AdvancedSharpAdbClient: pull {item.FullPath} -> {targetPath}"));

                    // target = [Windows parent folder]\[relative path from Android parent folder to current item]
                    using var stream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.Read);
                    service.Pull(item.FullPath, stream, SyncProgressCallback, in isCanceled);
                    
                    if (item.DateModified is not null)
                        File.SetLastWriteTime(targetPath, item.DateModified.Value);
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

            var files = Files.Where(f => !f.IsDirectory);
            var completed = files.Count(f => f.CurrentPercentage == 100);

            AdbSyncStatsInfo adbInfo = new(FilePath.FullPath, TotalBytes, TransferEnd.Subtract(TransferStart).TotalSeconds, completed, files.Count() - completed);
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
        item.Size ??= eventArgs.TotalBytesToReceive;

        mutex.WaitOne();
        progressUpdates.Add(new AdbSyncProgressInfo(item.FullPath, null, (int)eventArgs.ProgressPercentage, eventArgs.ReceivedBytesSize));
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
            var total = (double)Files.Sum(f => f.BytesTransferred) / TotalBytes;
            currProgress.TotalPercentage = (int)(total * 100);
            if (currProgress.TotalPercentage is < 0 or > 100)
            {
                if (Files.Count() == 1)
                    currProgress.TotalPercentage = currProgress.CurrentFilePercentage;
                else
                    currProgress.TotalPercentage = null;
            }

            AdbSyncProgressInfo info = currProgress;

            // Total percentage is displayed for single file
            if (Files.Count() == 1)
                info = new(currProgress.AndroidPath, currProgress.CurrentFilePercentage, null, currProgress.BytesTransferred);
            else if(ActiveFiles.Count() > 1)
            {
                info = new(string.Format(Strings.Resources.S_FILES_PLURAL, ActiveFiles.Count()),
                           currProgress.TotalPercentage,
                           (int)ActiveFiles.Average(f => f.CurrentPercentage),
                           currProgress.TotalBytesTransferred);
            }

            StatusInfo = new InProgSyncProgressViewModel(info);
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
