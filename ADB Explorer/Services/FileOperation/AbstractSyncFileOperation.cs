namespace ADB_Explorer.Services;

/// <summary>
/// Base for operations that move a <see cref="SyncFile"/> tree through AdvancedSharpAdbClient sync,
/// including sources staged out of an archive on the device.
/// </summary>
public abstract class AbstractSyncFileOperation : FileOperation
{
    public override SyncFile FilePath { get; }

    public int? MaxThreads { get; set; }

    /// <summary>Device temp root used when pulling from an archive; cleaned when the op finishes.</summary>
    public string? ArchivePullStagingRoot { get; set; }

    /// <summary>Original archive file path for hash validation (<c>tar --to-command</c> / <c>-O</c>).</summary>
    public string? ArchiveSourcePath { get; set; }

    /// <summary>Internal archive path of the selected member (empty = archive root).</summary>
    public string? ArchiveInternalPath { get; set; }

    public bool IsArchivePull => !string.IsNullOrEmpty(ArchiveSourcePath);

    protected IEnumerable<SyncFile> Files => [FilePath, .. FilePath.AllChildren()];

    /// <summary>The selected items of a batch op, whose <see cref="FilePath"/> and <see cref="FileOperation.TargetPath"/> are their parent folders.</summary>
    public IReadOnlyList<SyncFile> BatchItems { get; private set; } = [];

    public override bool IsBatch => BatchItems.Count > 0;

    public string BatchName => Strings.Plurals.Items(BatchItems.Count);

    /// <summary>Each top-level item with where it goes - the batch items, or the op's own source and target.</summary>
    public IEnumerable<(SyncFile Source, SyncFile Target)> TransferredItems
    {
        get
        {
            if (!IsBatch)
                return [(FilePath, TargetPath)];

            return BatchItems.Select(item => (item, new SyncFile(FileHelper.ConcatPaths(TargetPath, item.FullName), item.IsDirectory ? AbstractFile.FileType.Folder : AbstractFile.FileType.File)
            {
                PathType = TargetPath.PathType,
                Size = item.Size,
            }));
        }
    }

    protected AbstractSyncFileOperation(SyncFile sourcePath, LogicalDeviceViewModel device, Dispatcher dispatcher)
        : base(sourcePath, device, dispatcher)
    {
        FilePath = sourcePath;
    }

    /// <summary>A folder standing for the parent of <paramref name="items"/>, so they sync as one tree.</summary>
    protected static SyncFile CreateBatchRoot(IReadOnlyList<SyncFile> items)
        => new(items[0].ParentPath, AbstractFile.FileType.Folder) { PathType = items[0].PathType };

    protected void SetBatchItems(IReadOnlyList<SyncFile> items)
    {
        BatchItems = items;
        FilePath.Children.AddRange(items);
    }

    /// <summary>Every file that failed in the last run, kept past the tree's cleanup so they can be retried.</summary>
    public IReadOnlyList<SyncFile> FailedFiles { get; private set; } = [];

    // Files and bytes done in the runs before a retry, which its result counts too
    private int _completedBefore;
    private long _bytesBefore;
    private int _lastCompleted;
    private long _lastBytes;

    protected bool HasCompletedBefore => _completedBefore > 0;

    /// <summary>Not for staged archive sources and targets, which are cleaned up once the op finishes.</summary>
    public bool CanRetry => FailedFiles.Count > 0
        && Status is not (OperationStatus.InProgress or OperationStatus.Waiting)
        && !IsArchivePull
        && this is not FileTransferOperation { TargetStagingRoot: not null };

    /// <summary>Adds the runs before a retry to this run's completed files and bytes.</summary>
    protected (int Completed, long Bytes) RecordRunResult(int completed, long bytes)
    {
        _lastCompleted = _completedBefore + completed;
        _lastBytes = _bytesBefore + bytes;

        return (_lastCompleted, _lastBytes);
    }

    protected void KeepFailedFiles()
        => FailedFiles = [.. Files.Where(f => !f.IsDirectory && f.LastUpdate is SyncErrorInfo)];

    /// <summary>Queues the op again, for just the files that failed in its last run.</summary>
    public void Retry()
    {
        if (!CanRetry)
            return;

        var failed = FailedFiles;
        FailedFiles = [];
        _completedBefore = _lastCompleted;
        _bytesBefore = Math.Max(0, _lastBytes - failed.Sum(f => f.Size ?? 0));

        FilePath.ClearAll();
        failed.ForEach(f => f.ProgressUpdates.Clear());

        // A single file op retries its own FilePath. A tree gets the failed files, and their folders in case they're gone.
        if (FilePath.IsDirectory)
        {
            var folders = failed.Select(f => f.ParentPath)
                                .Where(path => path != FilePath.FullPath)
                                .Distinct()
                                .Select(path => new SyncFile(path, AbstractFile.FileType.Folder) { PathType = FilePath.PathType });

            FilePath.Children.AddRange([.. folders, .. failed]);
        }

        IsValidated = false;
        IsPastOp = false;
        Status = OperationStatus.Waiting;
        Data.FileOpQ.Start();
    }

    public void SetArchivePullSource(string archivePath, string internalPath, string stagingRoot, string displayPath)
    {
        ArchiveSourcePath = archivePath;
        ArchiveInternalPath = internalPath;
        ArchivePullStagingRoot = stagingRoot;
        AltSource = new(displayPath);
    }

    protected void CleanupArchiveStaging()
    {
        if (string.IsNullOrEmpty(ArchivePullStagingRoot))
            return;

        var root = ArchivePullStagingRoot;
        ArchivePullStagingRoot = null;
        var deviceId = Device.ID;
        Task.Run(() => ArchiveExtract.CleanupStaging(deviceId, root));
    }
}
