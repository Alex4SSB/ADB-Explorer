using AdvancedSharpAdbClient;
using AdvancedSharpAdbClient.Models;

namespace ADB_Explorer.Services;

/// <summary>
/// Copies or moves files between two Android devices. Each file is staged in a Windows temp
/// folder: pulled from the source device, then pushed to the target device.
/// </summary>
public class FileTransferOperation : AbstractSyncFileOperation
{
    private static readonly long UiUpdateThrottleTicks = TimeSpan.FromMilliseconds(150).Ticks;

    private sealed class ByteCounter
    {
        private ulong _lastRaw;
        private long _carry;

        public long Current { get; private set; }

        // The sync protocol reports 32-bit byte counts that can wrap on large files.
        public long Update(ulong raw)
        {
            if (raw < _lastRaw)
                _carry += 1L << 32;

            _lastRaw = raw;
            Current = _carry + (long)raw;

            return Current;
        }
    }

    private sealed class FileProgress
    {
        public ByteCounter Pull { get; } = new();
        public ByteCounter Push { get; } = new();
    }

    private readonly string _stagingRoot = Path.Combine(Path.GetTempPath(), "ADB Explorer", "Transfer", Guid.NewGuid().ToString("N"));
    private readonly ConcurrentDictionary<string, FileProgress> _progress = new();

    private long _lastUiUpdateTicks;
    private int _activeFiles;
    private bool _isCanceled;

    public override SyncFile AndroidPath => FilePath;

    public DateTime TransferStart { get; private set; }

    /// <summary>Whether a move deleted the source, i.e. every file made it to the target.</summary>
    public bool SourceRemoved { get; private set; }

    /// <summary>Whether every file made it to the target.</summary>
    public bool IsFullyTransferred { get; private set; }

    /// <summary>Target device folder holding the files while an archive there is updated; the caller then removes a move's source.</summary>
    public string? TargetStagingRoot { get; private set; }

    public FileTransferOperation(
        SyncFile sourcePath,
        SyncFile targetPath,
        LogicalDeviceViewModel sourceDevice,
        LogicalDeviceViewModel targetDevice,
        bool isMove,
        Dispatcher dispatcher) : base(sourcePath, sourceDevice, dispatcher)
    {
        OperationName = isMove ? OperationType.Move : OperationType.Copy;
        TargetPath = targetPath;
        TargetDevice = targetDevice;
    }

    public void SetArchiveTarget(string archiveDisplayPath, string stagingRoot)
    {
        TargetStagingRoot = stagingRoot;
        AltTarget = new(archiveDisplayPath);
    }

    public void RemoveSource()
    {
        ShellFileOperation.SilentDelete(Device, FilePath.FullPath);
        SourceRemoved = true;
    }

    public override void Start()
    {
        if (Status == OperationStatus.InProgress)
            throw new Exception("Cannot start an already active operation!");

        Status = OperationStatus.InProgress;
        StatusInfo = new InProgSyncProgressViewModel();

        _isCanceled = false;
        _activeFiles = 0;
        _progress.Clear();
        Interlocked.Exchange(ref _lastUiUpdateTicks, 0);

        var token = CancelTokenSource!.Token;
        token.Register(() => _isCanceled = true);

        Task.Run(() => RunTransfer(token));
    }

    private void RunTransfer(CancellationToken token)
    {
        try
        {
            TransferStart = DateTime.Now;
            Directory.CreateDirectory(_stagingRoot);

            var folders = FolderHelper.GetBottomMostFolders(Files)
                .Select(f => FileHelper.ConcatPaths(TargetPath.FullPath, FileHelper.ExtractRelativePath(f.FullPath, FilePath.FullPath, false)));

            if (folders.Any())
                ShellFileOperation.MakeDirs(TargetDevice!, folders).GetAwaiter().GetResult();

            ParallelOptions options = new()
            {
                MaxDegreeOfParallelism = MaxThreads ?? Data.Settings.MaxSimultaneousOps,
                CancellationToken = token,
            };

            Parallel.ForEach(Files.Where(f => !f.IsDirectory).ToList(), options, TransferFile);
            token.ThrowIfCancellationRequested();

            SetResult();
        }
        catch (OperationCanceledException)
        {
            Status = OperationStatus.Canceled;
            StatusInfo = new CanceledOpProgressViewModel();
        }
        catch (Exception e)
        {
            Status = OperationStatus.Failed;
            StatusInfo = new FailedOpProgressViewModel(FileOpStatusFormatter.StatusString(typeof(SyncErrorInfo), message: e.Message, total: true));
        }
        finally
        {
            ReleaseResources();
        }
    }

    private void TransferFile(SyncFile item)
    {
        var progress = _progress.GetOrAdd(item.FullPath, _ => new());
        var stagedPath = Path.Combine(_stagingRoot, Guid.NewGuid().ToString("N"));

        var targetPath = FilePath.IsDirectory
            ? FileHelper.ConcatPaths(TargetPath, FileHelper.ExtractRelativePath(item.FullPath, FilePath.FullPath))
            : TargetPath.FullPath;

        Interlocked.Increment(ref _activeFiles);

        try
        {
            using (SyncService source = new(Device.DeviceData))
            using (var stream = new FileStream(stagedPath, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                Log($"pull {item.FullPath} -> {stagedPath} ({Device.Name})");
                source.Pull(item.FullPath, stream, args => ReportProgress(item, progress.Pull, true, args), Device.SupportsSyncV2, in _isCanceled);
            }

            using (SyncService target = new(TargetDevice!.DeviceData))
            using (var stream = new FileStream(stagedPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Log($"push {stagedPath} -> {targetPath} ({TargetDevice.Name})");

                var fileMode = UnixFileStatus.AllPermissions | UnixFileStatus.Regular;
                target.Push(stream, targetPath, fileMode, item.DateModified ?? DateTime.Now, args => ReportProgress(item, progress.Push, false, args), TargetDevice.SupportsSyncV2, in _isCanceled);
            }

            item.AddUpdates(new AdbSyncProgressInfo(item.FullPath, null, 100, item.Size));
        }
        catch (Exception e)
        {
            item.AddUpdates(new SyncErrorInfo(item.FullPath, e.Message));
        }
        finally
        {
            Interlocked.Decrement(ref _activeFiles);

            try
            {
                File.Delete(stagedPath);
            }
            catch
            {
            }
        }
    }

    private void ReportProgress(SyncFile item, ByteCounter counter, bool isPull, SyncProgressChangedEventArgs args)
    {
        var previous = counter.Current;
        var delta = counter.Update(args.ReceivedBytesSize) - previous;

        if (delta > 0)
        {
            if (isPull)
                SyncTransferTracker.AddPullBytes(delta);
            else
                SyncTransferTracker.AddPushBytes(delta);
        }

        if (isPull && item.Size is null && args.TotalBytesToReceive > 0)
            item.Size = (long)args.TotalBytesToReceive;

        // The last chunk of a phase is always shown, or the progress stops short of where the phase ended.
        var isPhaseDone = args.TotalBytesToReceive > 0 && args.ReceivedBytesSize >= args.TotalBytesToReceive;
        var nowTicks = DateTime.UtcNow.Ticks;
        if (!isPhaseDone && nowTicks - Interlocked.Read(ref _lastUiUpdateTicks) < UiUpdateThrottleTicks)
            return;

        Interlocked.Exchange(ref _lastUiUpdateTicks, nowTicks);
        UpdateProgress(item);
    }

    // Both phases count toward progress, so the byte total is twice the size of the files.
    private void UpdateProgress(SyncFile current)
    {
        var totalBytes = Files.Sum(f => f.Size ?? 0) * 2;
        var transferred = _progress.Values.Sum(p => p.Pull.Current + p.Push.Current);

        double? totalPercentage = null;
        if (totalBytes > 0)
            totalPercentage = Math.Min(100, transferred * 100.0 / totalBytes);

        if (_progress.TryGetValue(current.FullPath, out var fileProgress) && current.Size is > 0)
        {
            var filePercentage = Math.Min(100, (fileProgress.Pull.Current + fileProgress.Push.Current) * 100.0 / (current.Size.Value * 2));
            current.AddUpdates(new AdbSyncProgressInfo(current.FullPath, null, filePercentage, fileProgress.Pull.Current + fileProgress.Push.Current));
        }

        var currentName = current.FullPath;
        if (_activeFiles > 1)
            currentName = string.Format(Strings.Resources.S_FILES_PLURAL, _activeFiles);

        AdbSyncProgressInfo info = new(currentName, totalPercentage, null, transferred);
        StatusInfo = new InProgSyncProgressViewModel(info, TransferStart, totalBytes, transferred);
    }

    private static bool IsFileCompleted(SyncFile file)
        => file.LastUpdate is not SyncErrorInfo && (file.CurrentPercentage >= 100 || file.Size is 0);

    private void SetResult()
    {
        var files = Files.Where(f => !f.IsDirectory).ToList();
        var filesCount = files.Count;
        var completed = files.Count(IsFileCompleted);

        // Nothing but (empty) folders, which were already created on the target.
        if (filesCount == 0)
        {
            filesCount = 1;
            completed = 1;
        }

        if (completed == 0)
        {
            var message = files.Select(f => f.LastUpdate).OfType<SyncErrorInfo>().LastOrDefault()?.Message ?? "";

            Status = OperationStatus.Failed;
            StatusInfo = new FailedOpProgressViewModel(FileOpStatusFormatter.StatusString(typeof(SyncErrorInfo), message: message, total: true));

            return;
        }

        // The source is only removed when every file made it across.
        var skipped = filesCount - completed;
        IsFullyTransferred = skipped == 0;
        if (OperationName is OperationType.Move && IsFullyTransferred && TargetStagingRoot is null)
            RemoveSource();

        var totalSeconds = Math.Max(0, (DateTime.Now - TransferStart).TotalSeconds);
        AdbSyncStatsInfo stats = new(FilePath.FullPath, Files.Sum(f => f.Size ?? 0), totalSeconds, completed, skipped);

        Status = OperationStatus.Completed;
        StatusInfo = new CompletedSyncProgressViewModel(stats);
    }

    private void ReleaseResources()
    {
        // Only the files that failed are kept, so the details view can show why.
        var faulty = Files.Where(f => !f.IsDirectory && f.LastUpdate is SyncErrorInfo)
                          .Take(20)
                          .Select(f => (File: f, Update: f.LastUpdate))
                          .ToList();

        FilePath.ClearAll();

        foreach (var (file, update) in faulty)
        {
            file.ProgressUpdates.Add(update);
        }

        FilePath.Children.AddRange(faulty.Select(f => f.File));

        CleanupArchiveStaging();

        try
        {
            Directory.Delete(_stagingRoot, true);
        }
        catch
        {
        }
    }

    private static void Log(string message)
    {
        if (Data.Settings.EnableLog && !Data.IsLogPaused)
            Data.CommandLog.Add(new($"@AdvancedSharpAdbClient: {message}"));
    }

    public override void ClearChildren()
        => FilePath.ClearAll();

    public override void AddUpdates(IEnumerable<FileOpProgressInfo> newUpdates)
        => FilePath.AddUpdates(newUpdates, this);

    public override void AddUpdates(params FileOpProgressInfo[] newUpdates)
        => FilePath.AddUpdates(newUpdates, this);
}
