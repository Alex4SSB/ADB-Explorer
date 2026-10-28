using AdvancedSharpAdbClient;
using AdvancedSharpAdbClient.Models;
using Vanara.Windows.Shell;

namespace ADB_Explorer.Services;

public class FileSyncOperation : AbstractSyncFileOperation
{
    // Both created in Start(), not the constructor — an operation exists in a
    // "not started" state before that, where neither is meaningful yet.
    private CancellationTokenSource _cancelTokenSource = null!;
    public ObservableList<FileOpProgressInfo> ProgressUpdates = null!;

    private readonly ConcurrentDictionary<string, long> _lastReportedBytes = new();
    private readonly ConcurrentDictionary<string, ulong> _lastRawReceivedBytes = new();
    private readonly ConcurrentDictionary<string, long> _receivedBytesCarry = new();

    // Kept as running totals, so a progress update doesn't walk every file (slow with tens of thousands)
    private long _totalBytes;
    private long _bytesTransferred;
    private bool _isSingleFile;
    private readonly ConcurrentDictionary<SyncFile, double> _activeFiles = new();

    // Progress callbacks can fire thousands of times per second across all parallel
    // transfers; gate how often they reach the (dispatcher-marshaled) UI collection
    // instead of forwarding every single one, which was freezing the UI thread.
    private long _lastUiUpdateTicks;
    private static readonly long UiUpdateThrottleTicks = TimeSpan.FromMilliseconds(150).Ticks;

    private bool _useSyncV2;
    private int _autoRetries;
    private Mutex _progressMutex = null!;

    // Shell streaming: a pull per file the shell reads, keyed by device path
    private sealed class ShellPull(SyncPipe pipe, long? size, TransferLane lane, long claimed)
    {
        public SyncPipe Pipe { get; } = pipe;

        public long? Size { get; } = size;

        // Claimed as the pull is created, so the next one picked sees the lane busy
        public TransferLane Lane { get; } = lane;

        public long Claimed { get; } = claimed;

        public volatile bool IsPulling = true;

        public Task Task { get; set; } = Task.CompletedTask;
    }

    private List<string> _shellOrder = [];
    private List<TransferLane>? _shellLanes;
    private int _shellNext;
    private int _shellActive;
    private readonly Dictionary<string, ShellPull> _shellPulls = [];
    private Dictionary<string, SyncFile> _shellFiles = [];
    private readonly ConcurrentDictionary<string, byte> _shellFilesDone = new();
    private TaskCompletionSource? _shellDone;
    private int _shellFilesLeft;
    private volatile bool _isShellEnded;

    /// <summary>The files go to the shell as streams, pulled as it reads them, instead of into files.</summary>
    public bool IsShellStream { get; private set; }

    /// <summary>Set once <see cref="Start"/> has prepared what <see cref="OpenShellStream"/> needs.</summary>
    public bool IsShellStreamReady { get; private set; }

    public override SyncFile AndroidPath => FilePath.PathType is AbstractFile.FilePathType.Android
        ? FilePath
        : TargetPath;

    public VirtualFileDataObject? VFDO { get; set; }

    private DragDropEffects _dropEffects = DragDropEffects.None;
    public DragDropEffects DropEffects
    {
        get
        {
            return VFDO is null ? _dropEffects : VFDO.CurrentEffect;
        }
        set
        {
            _dropEffects = value;
        }
    }

    public ShellItem? OriginalShellItem { get; set; }

    public DateTime TransferStart { get; private set; }
    public DateTime TransferEnd { get; private set; }

    private bool _isCanceled = false;

    public FileSyncOperation(OperationType operationName, FileDescriptor sourcePath, SyncFile targetPath, LogicalDeviceViewModel device, FailedOpProgressViewModel status)
        : base(new SyncFile(new FileClass(sourcePath)), device, App.AppDispatcher)
    {
        OperationName = operationName;
        TargetPath = targetPath;

        StatusInfo = status;
        Status = OperationStatus.Failed;
        AltSource = new(Navigation.SpecialLocation.Windows);
    }

    public FileSyncOperation(
        OperationType operationName,
        SyncFile sourcePath,
        SyncFile targetPath,
        LogicalDeviceViewModel device,
        Dispatcher dispatcher) : base(sourcePath, device, dispatcher)
    {
        OperationName = operationName;
        TargetPath = targetPath;

        // Virtual files from another app (or staged copies of them) have no source folder to show
        if (operationName is OperationType.Push && IsUnderTempDragPath(sourcePath.FullPath))
            AltSource = new(Navigation.SpecialLocation.Windows);
    }

    private static bool IsUnderTempDragPath(string path)
    {
        var tempDrag = Data.RuntimeSettings?.TempDragPath.TrimEnd('\\');

        return !string.IsNullOrEmpty(tempDrag)
            && (path.Equals(tempDrag, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(tempDrag + '\\', StringComparison.OrdinalIgnoreCase));
    }

    public override void Start()
    {
        if (Status == OperationStatus.InProgress)
        {
            throw new Exception("Cannot start an already active operation!");
        }

        IsShellStreamReady = false;
        Status = OperationStatus.InProgress;
        StatusInfo = new InProgSyncProgressViewModel();
        _cancelTokenSource = new CancellationTokenSource();
        _isCanceled = false;
        _autoRetries = Data.Settings.AutoRetryFailedItems;

        ProgressUpdates = [];
        ProgressUpdates.CollectionChanged += ProgressUpdates_CollectionChanged;
        _lastReportedBytes.Clear();
        _lastRawReceivedBytes.Clear();
        _receivedBytesCarry.Clear();
        _activeFiles.Clear();
        Interlocked.Exchange(ref _lastUiUpdateTicks, 0);
        Interlocked.Exchange(ref _bytesTransferred, 0);

        var files = Files.ToList();
        Interlocked.Exchange(ref _totalBytes, files.Sum(f => f.Size ?? 0));
        _isSingleFile = files.Count == 1;

        if (OperationName is OperationType.Push
            && !FilePath.IsVirtual
            && !File.Exists(FilePath.FullPath) && !Directory.Exists(FilePath.FullPath))
        {
            Status = OperationStatus.Failed;
            StatusInfo = new FailedOpProgressViewModel(FileOpStatusFormatter.StatusString(typeof(SyncErrorInfo), message: Strings.Resources.S_SYNC_FILE_NOT_FOUND, total: true));

            return;
        }

        UnixFileStatus fileMode = UnixFileStatus.AllPermissions | UnixFileStatus.Regular;

        _progressMutex = new();

        if (IsShellStream)
            PrepareShellStream(files);

        var task = Task.Run(async () =>
        {
            TransferStart = DateTime.Now;

            // The shell drives the pulls, and the op ends with the last of them
            if (IsShellStream)
            {
                await _shellDone!.Task;
                return;
            }

            if (OperationName is OperationType.Push)
            {
                var paths = FolderHelper.GetBottomMostFolders(Files)
                    .Select(f => FileHelper.ConcatPaths(TargetPath.FullPath, FileHelper.ExtractRelativePath(f.FullPath, FilePath.FullPath, false)));

                if (paths.Any())
                    await ShellFileOperation.MakeDirs(Device, paths);
            }
            else
            {
                foreach (var dir in FolderHelper.GetBottomMostFolders(Files))
                {
                    var targetDirPath = FileHelper.ConcatPaths(TargetPath, FileHelper.ExtractRelativePath(dir.FullPath, FilePath.FullPath, false));
                    Directory.CreateDirectory(targetDirPath);

                    if (Data.Settings.EnableLog && !Data.IsLogPaused)
                        Data.CommandLog.Add(new($"@Windows: mkdir {targetDirPath}"));
                }
            }

            MaxThreads ??= Data.Settings.MaxSimultaneousOps;
            _useSyncV2 = Device.SupportsSyncV2;

            var lanes = GetTransferLanes(MaxThreads.Value);
            var queue = new TransferQueue(Files.Where(f => !f.IsDirectory));
            ParallelOptions options = new()
            {
                MaxDegreeOfParallelism = MaxThreads.Value
            };

            // A retired lane leaves its share to the other, unless none is left to take it.
            bool IsDraining(TransferLane lane) => !lane.IsRetired || lanes.All(l => l.IsRetired);

            TransferLane? OtherLane(TransferLane lane) => lanes.FirstOrDefault(l => l != lane && !l.IsRetired);

            // Workers of this op still on each lane
            var workers = new int[lanes.Count];

            void DrainLane(TransferLane lane)
            {
                var index = lanes.IndexOf(lane);
                var left = false;
                Interlocked.Increment(ref workers[index]);

                try
                {
                    while (!_isCanceled && IsDraining(lane))
                    {
                        var take = queue.TryTake(lane, OtherLane(lane), out var item);
                        if (take is TakeResult.Empty)
                            return;

                        // Not this lane's turn yet - the rates and in-flight bytes it was judged by keep changing.
                        // The lane's last worker stays to poll; the rest leave rather than hold pool threads.
                        if (take is TakeResult.Wait)
                        {
                            if (Interlocked.Decrement(ref workers[index]) > 0)
                            {
                                left = true;
                                return;
                            }

                            Interlocked.Increment(ref workers[index]);
                            Thread.Sleep(LaneRetryDelay);
                            continue;
                        }

                        TransferFile(item!, lane, OtherLane(lane), _progressMutex, fileMode);
                    }
                }
                finally
                {
                    if (!left)
                        Interlocked.Decrement(ref workers[index]);
                }
            }

            // With two lanes, a thread each, so one lane's workers can't starve the other's start.
            // Each worker opens its own connection per file, so transfers run in parallel on every lane.
            var creation = lanes.Count > 1 ? TaskCreationOptions.LongRunning : TaskCreationOptions.None;
            try
            {
                await Task.WhenAll(lanes.Select(lane => Task.Factory.StartNew(
                    () => Parallel.For(0, MaxThreads.Value, options, _ => DrainLane(lane)),
                    creation)));
            }
            finally
            {
                lanes.ForEach(lane => lane.Release());
            }

        });

        task.ContinueWith((t) =>
        {
            ProgressUpdates.CollectionChanged -= ProgressUpdates_CollectionChanged;
        });

        task.ContinueWith((t) =>
        {
            var files = Files.Where(f => !f.IsDirectory);
            int filesCount = files.Count();
            var completed = files.Count(IsSyncFileCompleted);
            if (filesCount == 0 && Files.First().IsDirectory)
            {
                filesCount = 1;
                completed = 1;
            }

            // A retry whose file failed again still has what the runs before it completed
            if (filesCount == 1 && completed == 0 && !HasCompletedBefore)
            {
                string message = FilePath.LastUpdate is SyncErrorInfo errorInfo
                    ? errorInfo.Message
                    : ProgressUpdates.OfType<SyncErrorInfo>().LastOrDefault()?.Message;

                Status = OperationStatus.Failed;
                StatusInfo = new FailedOpProgressViewModel(FileOpStatusFormatter.StatusString(typeof(SyncErrorInfo), message: message, total: true));
            }
            else
            {
                Status = OperationStatus.Completed;

                var totalSeconds = TransferEnd.Subtract(TransferStart).TotalSeconds;
                if (totalSeconds < 0)
                    totalSeconds = 0;

                var (completedTotal, bytesTotal) = RecordRunResult(completed, Interlocked.Read(ref _totalBytes));
                AdbSyncStatsInfo adbInfo = new(FilePath.FullPath, bytesTotal, totalSeconds, completedTotal, filesCount - completed);
                StatusInfo = new CompletedSyncProgressViewModel(adbInfo);
            }

            ReleaseTransferResources();

        }, TaskContinuationOptions.OnlyOnRanToCompletion);

        task.ContinueWith((t) =>
        {
            Status = OperationStatus.Canceled;
            StatusInfo = new CanceledOpProgressViewModel();

            ReleaseTransferResources();
        }, TaskContinuationOptions.OnlyOnCanceled);

        task.ContinueWith((t) =>
        {
            string message = string.IsNullOrEmpty(t.Exception.InnerException.Message)
                ? ProgressUpdates.OfType<SyncErrorInfo>().Last().Message
                : t.Exception.InnerException.Message;

            Status = OperationStatus.Failed;
            StatusInfo = new FailedOpProgressViewModel(FileOpStatusFormatter.StatusString(typeof(SyncErrorInfo), message: message, total: true));

            ReleaseTransferResources();
        }, TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>A transport files are synced over, which end of the <see cref="TransferQueue"/> it drains, and how fast.</summary>
    private sealed class TransferLane(DeviceData deviceData, bool takesLargest, string? name = null, double nominalRate = 0)
    {
        private static readonly Dictionary<string, TransferLane> SharedLanes = [];

        public DeviceData DeviceData { get; } = deviceData;

        public bool TakesLargest { get; } = takesLargest;

        /// <summary>Only set when files are split across lanes; shown in the command log.</summary>
        public string? Name { get; } = name;

        /// <summary>Consecutive files that failed here but then synced over the other lane.</summary>
        public int Strikes;

        public bool IsRetired => Volatile.Read(ref Strikes) >= 3;

        private readonly Stopwatch _busy = new();
        private string? _sharedKey;
        private int _users;
        private long _claimedBytes;
        private long _doneBytes;

        /// <summary>
        /// Operations running at once on the same transport and direction (e.g. one per selected file) share its lane,
        /// so each one balances against what the others have in flight too. Only a new lane reads its nominal rate.
        /// </summary>
        public static TransferLane Acquire(DeviceData deviceData, OperationType direction, bool takesLargest, string name, Func<double> nominalRate)
        {
            var key = $"{deviceData.Serial}|{direction}|{name}";

            lock (SharedLanes)
            {
                if (!SharedLanes.TryGetValue(key, out var lane))
                    SharedLanes[key] = lane = new(deviceData, takesLargest, name, nominalRate()) { _sharedKey = key };

                lane._users++;

                return lane;
            }
        }

        public void Release()
        {
            if (_sharedKey is null)
                return;

            lock (SharedLanes)
            {
                if (--_users == 0)
                    SharedLanes.Remove(_sharedKey);
            }
        }

        public void Claim(long bytes)
        {
            Interlocked.Add(ref _claimedBytes, bytes);
            UpdateBusy();
        }

        public void Done(long bytes)
        {
            Interlocked.Add(ref _doneBytes, bytes);
            UpdateBusy();
        }

        /// <summary>Never negative, though a file of unknown size reports bytes before its claim is settled.</summary>
        public long InFlightBytes => Math.Max(0, Interlocked.Read(ref _claimedBytes) - Interlocked.Read(ref _doneBytes));

        // Times only while files are in flight, so a lane left idle doesn't look slower.
        private void UpdateBusy()
        {
            // Most progress calls don't change the state, so they skip the lock
            if (_busy.IsRunning == InFlightBytes > 0)
                return;

            lock (_busy)
            {
                if (InFlightBytes > 0)
                    _busy.Start();
                else
                    _busy.Stop();
            }
        }

        /// <summary>
        /// Bytes per second: the link's nominal rate (by USB version / Wi-Fi link speed) until the lane
        /// has been busy a couple of seconds, then what it has measured so far, updated as it goes.
        /// </summary>
        public double Rate()
        {
            var done = Interlocked.Read(ref _doneBytes);
            double busy;
            lock (_busy)
                busy = _busy.Elapsed.TotalSeconds;

            if (busy < 2 || done == 0)
                return nominalRate;

            return done / busy;
        }
    }

    private enum TakeResult { Taken, Wait, Empty }

    private static readonly TimeSpan LaneRetryDelay = TimeSpan.FromMilliseconds(200);

    /// <summary>Files by size. With two lanes, one takes the smallest and the other the largest, meeting wherever their speeds balance.</summary>
    private sealed class TransferQueue
    {
        private readonly SyncFile[] _files;
        // Shared by all queues, since ops sharing lanes must each see the others' claims before deciding.
        private static readonly Lock ClaimLock = new();
        private int _first;
        private int _last;
        private long _remainingBytes;

        public TransferQueue(IEnumerable<SyncFile> files)
        {
            _files = [.. files.OrderBy(f => f.Size ?? 0)];
            _last = _files.Length - 1;
            _remainingBytes = _files.Sum(f => f.Size ?? 0);
        }

        /// <summary>While <paramref name="other"/> works too, a lane waits instead of claiming a file it shouldn't take yet.</summary>
        public TakeResult TryTake(TransferLane lane, TransferLane? other, out SyncFile? file)
        {
            lock (ClaimLock)
            {
                file = null;
                if (_first > _last)
                    return TakeResult.Empty;

                var candidate = lane.TakesLargest ? _files[_last] : _files[_first];
                var size = candidate.Size ?? 0;

                if (other is not null && !ShouldTake(lane, other, size))
                    return TakeResult.Wait;

                if (lane.TakesLargest)
                    _last--;
                else
                    _first++;

                _remainingBytes -= size;
                lane.Claim(size);
                file = candidate;

                return TakeResult.Taken;
            }
        }

        /// <summary>
        /// Take it if this lane still finishes by when an even split of all the work would end, or sooner than
        /// <paramref name="other"/> could finish it even if it took it next. Once both lanes are idle, one always qualifies.
        /// </summary>
        private bool ShouldTake(TransferLane lane, TransferLane other, long size)
        {
            var rate = lane.Rate();
            var otherRate = other.Rate();
            var finish = (lane.InFlightBytes + size) / rate;
            var evenFinish = (lane.InFlightBytes + other.InFlightBytes + _remainingBytes) / (rate + otherRate);

            return finish <= evenFinish || finish <= (other.InFlightBytes + size) / otherRate;
        }
    }

    /// <summary>
    /// On a device connected over both USB and Wi-Fi, USB takes the small files (lower latency per file)
    /// and Wi-Fi the large ones, at the same time. Otherwise everything goes over the preferred transport.
    /// </summary>
    private List<TransferLane> GetTransferLanes(int maxThreads)
    {
        var online = Device.Connections.Where(c => c.Status is DeviceStatus.Ok).ToList();
        var usb = online.FirstOrDefault(c => c.Type is DeviceType.Local);
        var wifi = online.Where(c => c.IsWifi).MinBy(c => c.Rank);

        // A single-threaded op (e.g. thumbnails) stays sequential. Largest first, so no worker is left with a big file at the end.
        if (usb is null || wifi is null || maxThreads < 2)
            return [new(Device.DeviceData, true)];

        return
        [
            TransferLane.Acquire(usb.DeviceData, OperationName, false, "USB", () => NominalUsbRate(Device.UsbVersion)),
            TransferLane.Acquire(wifi.DeviceData, OperationName, true, "Wi-Fi", CurrentWifiRate),
        ];
    }

    /// <summary>Re-reads the Wi-Fi rates first, as they shift with the signal.</summary>
    private double CurrentWifiRate()
    {
        Device.RefreshWifiSpeeds();

        // A push is bound by the rate the device receives at, a pull by the rate it sends at
        var wifiMbps = OperationName is OperationType.Push ? Device.WifiRxMbps : Device.WifiTxMbps;

        return NominalWifiRate(wifiMbps);
    }

    /// <summary>Typical sync throughput in bytes per second, until a lane measures its own.</summary>
    private static double NominalUsbRate(string? version) => version switch
    {
        "1.1" => 1 * 1_048_576.0,
        "3.0" => 300 * 1_048_576.0,
        "3.1" => 600 * 1_048_576.0,
        _ => 40 * 1_048_576.0,
    };

    /// <summary>adb sync gets roughly 60% of the Wi-Fi link rate.</summary>
    private static double NominalWifiRate(int? linkMbps) => (linkMbps ?? 150) * 1_000_000.0 / 8 * 0.6;

    private void TransferFile(SyncFile item, TransferLane lane, TransferLane? other, Mutex mutex, UnixFileStatus fileMode)
    {
        for (var retriesLeft = _autoRetries; ; retriesLeft--)
        {
            var (error, failedLane, failedAt) = TryTransferFile(item, lane, other, mutex, fileMode);
            if (error is null)
                return;

            // A lane that dropped already left the file to the other one; only a live lane's failure is retried
            if (_isCanceled || retriesLeft <= 0 || !AdbService.IsTransportOnline(failedLane.DeviceData, failedAt))
            {
                AddUpdates([new SyncErrorInfo(item.FullPath, error.Message)]);
                return;
            }

            if (failedLane != lane)
            {
                other = lane;
                lane = failedLane;
            }

            lane.Claim(item.Size ?? 0);
        }
    }

    /// <returns>The error, the lane and the <see cref="Stopwatch"/> timestamp it happened on - or a null error once the file synced.</returns>
    private (Exception? Error, TransferLane Lane, long FailedAt) TryTransferFile(SyncFile item, TransferLane lane, TransferLane? other, Mutex mutex, UnixFileStatus fileMode)
    {
        try
        {
            SyncFileOver(lane, item, mutex, fileMode);
            Interlocked.Exchange(ref lane.Strikes, 0);

            return (null, lane, 0);
        }
        catch (Exception e) when (!_isCanceled && other is not null)
        {
            var failedAt = Stopwatch.GetTimestamp();

            // While the lane is still there, the fault is the file's - left to the auto retries
            if (AdbService.IsTransportOnline(lane.DeviceData, failedAt))
                return (e, lane, failedAt);

            other.Claim(item.Size ?? 0);

            // The lane was lost, so the file moves to the other one
            try
            {
                SyncFileOver(other, item, mutex, fileMode);
                Interlocked.Increment(ref lane.Strikes);

                return (null, other, 0);
            }
            catch (Exception otherError)
            {
                return (otherError, other, Stopwatch.GetTimestamp());
            }
        }
        catch (Exception e)
        {
            return (e, lane, Stopwatch.GetTimestamp());
        }
    }

    private void SyncFileOver(TransferLane lane, SyncFile item, Mutex mutex, UnixFileStatus fileMode)
    {
        var claimed = item.Size ?? 0;
        long synced = 0;

        void SyncProgressCallback(SyncProgressChangedEventArgs eventArgs)
        {
            var delta = AddUpdates(item, eventArgs, mutex);
            synced += delta;
            lane.Done(delta);
        }

        try
        {
            SyncFileOver(lane, item, SyncProgressCallback, fileMode);
        }
        catch
        {
            // A failed file's partial bytes leave the total, and a retry starts it over
            ResetProgress(item);
            throw;
        }
        finally
        {
            _activeFiles.TryRemove(item, out _);

            // A shared lane outlives this op, so settle the claim to what synced: a file of unknown size
            // was claimed as 0, and a failed one won't sync the rest.
            lane.Claim(synced - claimed);
        }
    }

    private void SyncFileOver(TransferLane lane, SyncFile item, Action<SyncProgressChangedEventArgs> progressCallback, UnixFileStatus fileMode)
    {
        var targetPath = FilePath.IsDirectory
                ? FileHelper.ConcatPaths(TargetPath, FileHelper.ExtractRelativePath(item.FullPath, FilePath.FullPath))
                : TargetPath.FullPath;

        var via = lane.Name is null ? "" : $" ({lane.Name})";

        // A new connection per file; its ctor / Open can throw (e.g. device offline), which the caller reports.
        using SyncService service = new(lane.DeviceData);

        if (OperationName is OperationType.Push)
        {
            if (Data.Settings.EnableLog && !Data.IsLogPaused)
                Data.CommandLog.Add(new($"@AdvancedSharpAdbClient: push {item.FullPath} -> {targetPath}{via}"));

            var lastWriteTime = item.DateModified ?? DateTime.Now;

            // target = [Android parent folder]\[relative path from Windows parent folder to current item]
            using var stream = item.OpenRead();
            service.Push(stream, targetPath, fileMode, lastWriteTime, progressCallback, _useSyncV2, in _isCanceled);
        }
        else
        {
            if (Data.Settings.EnableLog && !Data.IsLogPaused)
                Data.CommandLog.Add(new($"@AdvancedSharpAdbClient: pull {item.FullPath} -> {targetPath}{via}"));

            if (!_useSyncV2)
            {
                var knownSize = item.Size ?? 0;
                ResolvePullFileSize(item);
                Interlocked.Add(ref _totalBytes, (item.Size ?? 0) - knownSize);
            }

            // target = [Windows parent folder]\[relative path from Android parent folder to current item]
            using var stream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.Read);
            service.Pull(item.FullPath, stream, progressCallback, _useSyncV2, in _isCanceled);

            // On the handle we already hold, rather than opening the file again for it
            if (item.DateModified is not null)
            {
                stream.Flush();
                File.SetLastWriteTime(stream.SafeFileHandle, item.DateModified.Value);
            }
        }
    }

    /// <summary>A retry starts the file over, so its byte counters must too.</summary>
    private void ResetProgress(SyncFile item)
    {
        if (_lastReportedBytes.TryGetValue(item.FullPath, out var reported))
            Interlocked.Add(ref _bytesTransferred, -reported);

        _lastReportedBytes[item.FullPath] = 0;
        _lastRawReceivedBytes[item.FullPath] = 0;
        _receivedBytesCarry[item.FullPath] = 0;
    }

    /// <returns>The bytes transferred since the previous update of this file.</returns>
    private long AddUpdates(SyncFile item, SyncProgressChangedEventArgs eventArgs, Mutex mutex)
    {
        if (item.Size is null && _useSyncV2 && eventArgs.TotalBytesToReceive > 0)
        {
            item.Size = (long)eventArgs.TotalBytesToReceive;
            Interlocked.Add(ref _totalBytes, item.Size.Value);
        }

        var currentBytes = CorrectReceivedBytes(item.FullPath, eventArgs.ReceivedBytesSize);
        var previousBytes = _lastReportedBytes.GetOrAdd(item.FullPath, 0L);
        _lastReportedBytes[item.FullPath] = currentBytes;
        var deltaBytes = currentBytes - previousBytes;
        Interlocked.Add(ref _bytesTransferred, deltaBytes);

        if (deltaBytes > 0)
        {
            if (OperationName is OperationType.Pull)
                SyncTransferTracker.AddPullBytes(deltaBytes);
            else
                SyncTransferTracker.AddPushBytes(deltaBytes);
        }

        double? filePercentage = null;
        if (item.Size is > 0)
            filePercentage = Math.Min(100, currentBytes * 100.0 / item.Size.Value);
        else if (item.Size is 0)
            // Empty files have nothing to transfer; avoid leaving percentage null (counted as skipped).
            filePercentage = 100.0;

        if (filePercentage is > 0 and < 100)
            _activeFiles[item] = filePercentage.Value;
        else
            _activeFiles.TryRemove(item, out _);

        var progressInfo = new AdbSyncProgressInfo(item.FullPath, null, filePercentage, currentBytes);

        // The file's own progress, for its row and its completed state
        item.AddUpdates(progressInfo);

        TransferEnd = DateTime.Now;

        var nowTicks = DateTime.UtcNow.Ticks;
        if (nowTicks - Interlocked.Read(ref _lastUiUpdateTicks) < UiUpdateThrottleTicks)
            return Math.Max(deltaBytes, 0);

        Interlocked.Exchange(ref _lastUiUpdateTicks, nowTicks);

        mutex.WaitOne();
        ProgressUpdates.Add(progressInfo);
        mutex.ReleaseMutex();

        return Math.Max(deltaBytes, 0);
    }

    /// <summary>
    /// Empty files never reach 100% via byte ratio (denominator is 0); treat size 0 without error as complete.
    /// </summary>
    private static bool IsSyncFileCompleted(SyncFile file)
    {
        if (file.LastUpdate is SyncErrorInfo)
            return false;

        if (file.CurrentPercentage >= 100)
            return true;

        return file.Size is 0;
    }

    private long CorrectReceivedBytes(string path, ulong rawReceived)
    {
        var lastRaw = _lastRawReceivedBytes.GetOrAdd(path, 0);
        var carry = _receivedBytesCarry.GetOrAdd(path, 0L);

        if (rawReceived < lastRaw)
            _receivedBytesCarry[path] = carry += 1L << 32;

        _lastRawReceivedBytes[path] = rawReceived;
        return carry + (long)rawReceived;
    }

    private static void ResolvePullFileSize(SyncFile item)
    {
        if (item.ShellLsSize is > 0)
        {
            item.Size = item.ShellLsSize;
            return;
        }

        if (Data.ActiveDevice is null || !ShellCommands.StatExists(Data.ActiveDevice.ID))
            return;

        var file = Data.DirList?.FileList?.FirstOrDefault(f => f.FullPath == item.FullPath)
            ?? new FileClass(item);

        file.UpdateSizeFromShell(CancellationToken.None);

        if (file.ShellLsSize is > 0)
            item.Size = item.ShellLsSize = file.ShellLsSize;
    }

    private void ProgressUpdates_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (Status is not OperationStatus.InProgress)
            return;

        if (ProgressUpdates.LastOrDefault() is AdbSyncProgressInfo currProgress and not null)
        {
            var transferred = Interlocked.Read(ref _bytesTransferred);
            var totalBytes = Interlocked.Read(ref _totalBytes);
            currProgress.TotalPercentage = (double)transferred / totalBytes * 100;

            AdbSyncProgressInfo info = currProgress;
            var active = _activeFiles.Values.ToArray();

            // Total percentage is displayed for single file
            if (_isSingleFile)
                info = new(currProgress.AndroidPath, currProgress.TotalPercentage, null, currProgress.CurrentFileBytesTransferred);
            else if (active.Length > 1)
            {
                info = new(Strings.Plurals.Files(active.Length),
                           currProgress.TotalPercentage,
                           (int)active.Average(),
                           currProgress.TotalBytesTransferred);
            }

            StatusInfo = new InProgSyncProgressViewModel(info, TransferStart, totalBytes, transferred);
        }
    }

    /// <param name="order">The files' device paths, in the order the shell reads them.</param>
    public void StreamToShell(IEnumerable<string> order)
    {
        IsShellStream = true;
        _shellOrder = [.. order];
        AltTarget = new(Navigation.SpecialLocation.Windows);
    }

    private void PrepareShellStream(List<SyncFile> files)
    {
        _useSyncV2 = Device.SupportsSyncV2;
        _shellFiles = [];
        foreach (var file in files.Where(f => !f.IsDirectory))
            _shellFiles.TryAdd(file.FullPath, file);

        _shellFilesDone.Clear();
        _shellFilesLeft = _shellFiles.Count;
        _shellDone = new(TaskCreationOptions.RunContinuationsAsynchronously);

        // Folders only (the shell creates those), or the shell finished before the op got to start
        if (_shellFilesLeft == 0 || _isShellEnded)
            _shellDone.TrySetResult();

        IsShellStreamReady = true;
    }

    /// <summary>
    /// The stream the shell reads <paramref name="devicePath"/> from, its pull started along with the next files' once it's read.
    /// Once the op is over (e.g. the clipboard is pasted again), the files still stream, just untracked.
    /// </summary>
    public HANDLE OpenShellStream(string devicePath, long? size)
    {
        var pull = GetShellPull(devicePath, size, startOver: true);

        // A stream opened but never read (e.g. by a clipboard reader, not a paste) doesn't fill memory with the files after it
        return new DevicePullStream(pull.Pipe, size, () => ReadAheadFrom(devicePath)).ToComPointer();
    }

    private void ReadAheadFrom(string devicePath)
    {
        var index = _shellOrder.IndexOf(devicePath);
        if (index < 0)
            return;

        lock (_shellPulls)
            _shellNext = Math.Max(_shellNext, index + 1);

        PumpShellPulls();
    }

    /// <summary>
    /// Starts pulls of the files the shell reads next, in its order, as many at once as the op syncs.
    /// What they pull ahead of it is spilled to disk, so it finds them buffered.
    /// </summary>
    /// <remarks>
    /// Each lane keeps a few files pulling. Others start only if they fit whole in a pipe's own memory (small files),
    /// as spilling them all would only move the files' order onto the disk.
    /// As with a regular pull, the lane that takes the largest files starts the largest one first, wherever the shell reads it.
    /// </remarks>
    private void PumpShellPulls()
    {
        if (Status is not OperationStatus.InProgress)
            return;

        var limit = MaxThreads ?? Data.Settings.MaxSimultaneousOps;
        lock (_shellPulls)
        {
            var lanes = (_shellLanes ??= GetTransferLanes(limit)).Count;
            StartLargestShellPull();

            var unread = _shellPulls.Values.Count(p => !p.Pipe.Drained.IsCompleted);

            while (_shellActive < limit && _shellNext < _shellOrder.Count)
            {
                var next = _shellOrder[_shellNext];
                if (_shellPulls.ContainsKey(next))
                {
                    // Started out of order, as the largest
                    _shellNext++;
                    continue;
                }

                var size = _shellFiles.GetValueOrDefault(next)?.Size;
                var keepsLaneBusy = unread < lanes || (_shellActive < lanes * ShellPullsPerLane && SyncPipe.HasSpillRoom(size ?? 0));
                if (!keepsLaneBusy && size is not <= SyncPipe.Capacity)
                    break;

                _shellNext++;
                GetShellPull(next, size, startOver: false);
                unread++;
            }
        }
    }

    /// <summary>Starts the largest file not started yet on the idle lane that takes the largest files, if it can spill it.</summary>
    private void StartLargestShellPull()
    {
        if (_shellLanes is not { Count: > 1 } lanes
            || lanes.FirstOrDefault(l => l.TakesLargest && !l.IsRetired && PullsOn(l) == 0) is not { } lane)
        {
            return;
        }

        string? largest = null;
        long largestSize = 0;
        for (var i = _shellNext; i < _shellOrder.Count; i++)
        {
            var path = _shellOrder[i];
            if (!_shellPulls.ContainsKey(path) && _shellFiles.GetValueOrDefault(path)?.Size is { } size && size > largestSize)
                (largest, largestSize) = (path, size);
        }

        if (largest is not null && SyncPipe.HasSpillRoom(largestSize))
            GetShellPull(largest, largestSize, startOver: false, lane);
    }

    /// <param name="startOver">A file the shell already read from (or gave up on, or that failed unread) is pulled again.</param>
    /// <param name="lane">The lane to pull it over, rather than the one picked for it.</param>
    private ShellPull GetShellPull(string devicePath, long? size, bool startOver, TransferLane? lane = null)
    {
        lock (_shellPulls)
        {
            if (_shellPulls.TryGetValue(devicePath, out var current)
                && (!startOver || (current.Pipe.BytesRead == 0 && !current.Pipe.IsReaderClosed && !current.Pipe.IsFailed)))
            {
                return current;
            }

            current?.Pipe.CloseReader();

            var claimed = _shellFiles.GetValueOrDefault(devicePath)?.Size ?? 0;
            lane ??= PickShellLane(claimed);
            lane.Claim(claimed);

            var pull = new ShellPull(new SyncPipe(size, canSpill: true), size, lane, claimed);
            _shellActive++;
            pull.Task = Task.Factory.StartNew(() => PullToShell(devicePath, pull), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            _shellPulls[devicePath] = pull;

            // A file is done once the shell has read it all, not once it's all buffered,
            // and the memory it was counted for may let the next files start
            pull.Pipe.Drained.ContinueWith(_ =>
            {
                OnShellFileDone(devicePath, pull);
                PumpShellPulls();
            }, TaskScheduler.Default);

            return pull;
        }
    }

    // A single adb sync connection doesn't fill a lane; a few at once do
    private const int ShellPullsPerLane = 3;

    private int PullsOn(TransferLane lane)
    {
        lock (_shellPulls)
            return _shellPulls.Values.Count(p => p.IsPulling && p.Lane == lane);
    }

    /// <summary>The lane that would finish a file of <paramref name="size"/> first.</summary>
    private TransferLane PickShellLane(long size)
    {
        List<TransferLane> lanes;
        lock (_shellPulls)
            lanes = _shellLanes ??= GetTransferLanes(MaxThreads ?? Data.Settings.MaxSimultaneousOps);

        if (lanes.Count == 1)
            return lanes[0];

        var active = lanes.Where(l => !l.IsRetired).ToList();
        if (active.Count == 0)
            active = lanes;

        // A lane with room for another pull first: sharing a full one slows the file the shell may be waiting for,
        // however fast that lane seems. Of those, the lane not kept for the largest files
        var open = active.Where(l => PullsOn(l) < ShellPullsPerLane).ToList();
        if (open.Count > 0)
            active = open.Any(l => !l.TakesLargest) ? [.. open.Where(l => !l.TakesLargest)] : open;

        return active.MinBy(l => (l.InFlightBytes + size) / l.Rate())!;
    }

    private void PullToShell(string devicePath, ShellPull pull)
    {
        // Progress goes to the op only while it runs
        bool IsTracked() => Status is OperationStatus.InProgress && _shellFiles.ContainsKey(devicePath);

        var item = _shellFiles.GetValueOrDefault(devicePath);
        var claimed = pull.Claimed;
        var lane = pull.Lane;
        long reported = 0;
        Exception? error = null;

        try
        {
            if (Data.Settings.EnableLog && !Data.IsLogPaused)
                Data.CommandLog.Add(new($"@AdvancedSharpAdbClient: pull {devicePath} -> shell"));

            if (item is not null && IsTracked())
            {
                ResetProgress(item);

                if (!_useSyncV2)
                {
                    var knownSize = item.Size ?? 0;
                    ResolvePullFileSize(item);
                    Interlocked.Add(ref _totalBytes, (item.Size ?? 0) - knownSize);
                }
            }

            using SyncService service = new(lane.DeviceData);
            service.Pull(devicePath, pull.Pipe.Writer, args =>
            {
                var written = pull.Pipe.BytesWritten;
                lane.Done(written - reported);
                reported = written;

                if (item is not null && IsTracked())
                    AddUpdates(item, args, _progressMutex);
            }, _useSyncV2, in _isCanceled);

            // A canceled pull just stops, which would end the shell's file early instead of failing it
            if (_isCanceled)
                throw new OperationCanceledException();
        }
        catch (Exception e)
        {
            error = e;
        }
        finally
        {
            pull.Pipe.Complete(error);
            pull.IsPulling = false;
            lane.Done(Math.Max(0, claimed - reported));

            lock (_shellPulls)
                _shellActive--;
        }

        PumpShellPulls();

        lock (_shellPulls)
        {
            // A pull replaced by a newer one for the same file doesn't count
            if (item is null || !IsTracked() || _shellPulls.GetValueOrDefault(devicePath) != pull)
                return;
        }

        if (error is not null)
            AddUpdates([new SyncErrorInfo(item.FullPath, error.Message)]);
        else if (pull.Pipe.BytesWritten == 0)
            OnShellFileDone(devicePath, pull); // The shell may create an empty file without reading it
    }

    private void OnShellFileDone(string devicePath, ShellPull pull)
    {
        lock (_shellPulls)
        {
            if (Status is not OperationStatus.InProgress || !_shellFiles.ContainsKey(devicePath) || _shellPulls.GetValueOrDefault(devicePath) != pull)
                return;
        }

        if (_shellFilesDone.TryAdd(devicePath, 0) && Interlocked.Decrement(ref _shellFilesLeft) == 0)
            _shellDone?.TrySetResult();
    }

    /// <summary>The shell is done with the streams, as it reported to the data object.</summary>
    public void EndShellStream(bool succeeded)
    {
        if (!IsShellStream)
            return;

        _isShellEnded = true;

        if (succeeded)
        {
            // Files the shell skipped (e.g. on a name conflict) count as skipped
            _shellDone?.TrySetResult();
            return;
        }

        AbortShellPulls(unreadOnly: false);
        _shellDone?.TrySetCanceled();
    }

    /// <summary>Fails the shell's reads and frees the pulls and what they buffered, so none waits on a stream nobody reads anymore.</summary>
    private void AbortShellPulls(bool unreadOnly)
    {
        List<ShellPull> pulls;
        lock (_shellPulls)
            pulls = [.. _shellPulls.Values];

        var error = new OperationCanceledException();
        foreach (var pull in pulls.Where(p => !unreadOnly || p.Pipe.BytesRead == 0))
        {
            pull.Pipe.Complete(error);
            pull.Pipe.CloseReader();
        }
    }

    public override void Cancel()
    {
        if (Status != OperationStatus.InProgress)
        {
            throw new Exception("Cannot cancel a deactivated operation!");
        }

        _isCanceled = true;
        _cancelTokenSource.Cancel();

        if (IsShellStream)
        {
            AbortShellPulls(unreadOnly: false);
            _shellDone?.TrySetCanceled();
        }
    }

    public override void ClearChildren()
    {
        FilePath.ClearAll();
        ProgressUpdates?.Clear();
    }

    private void ReleaseTransferResources()
    {
        KeepFailedFiles();

        if (StatusInfo is CompletedSyncProgressViewModel completed && completed.FilesSkipped > 0)
        {
            var faultyFiles = Files.Where(f => !f.IsDirectory && f.LastUpdate is SyncErrorInfo)
                                   .Take(20)
                                   .Select(f => (File: f, Update: f.LastUpdate))
                                   .ToList();

            FilePath.ClearAll();

            foreach (var (file, update) in faultyFiles)
            {
                file.ProgressUpdates.Add(update);
            }

            FilePath.Children.AddRange(faultyFiles.Select(f => f.File));
        }
        else
        {
            FilePath.ClearAll();
        }

        ProgressUpdates?.Clear();

        // Read-ahead pulls of files the shell skipped, and what they buffered
        if (IsShellStream)
        {
            AbortShellPulls(unreadOnly: true);
            _shellLanes?.ForEach(lane => lane.Release());
        }

        if (OriginalShellItem is IDisposable disposable)
        {
            disposable.Dispose();
            OriginalShellItem = null;
        }

        CleanupArchiveStaging();
    }

    public override void AddUpdates(IEnumerable<FileOpProgressInfo> newUpdates)
        => FilePath.AddUpdates(newUpdates, this);

    public override void AddUpdates(params FileOpProgressInfo[] newUpdates)
        => FilePath.AddUpdates(newUpdates, this);

    public static FileSyncOperation PullFile(SyncFile sourcePath, SyncFile targetPath, LogicalDeviceViewModel device, Dispatcher dispatcher)
        => new(OperationType.Pull, sourcePath, targetPath, device, dispatcher);

    public static FileSyncOperation PushFile(SyncFile sourcePath, SyncFile targetPath, LogicalDeviceViewModel device, Dispatcher dispatcher)
        => new(OperationType.Push, sourcePath, targetPath, device, dispatcher);

    /// <summary>One op for <paramref name="items"/> sharing a parent folder, synced into <paramref name="targetFolder"/>.</summary>
    public static FileSyncOperation SyncBatch(OperationType operationName, IReadOnlyList<SyncFile> items, SyncFile targetFolder, LogicalDeviceViewModel device, Dispatcher dispatcher)
    {
        var op = new FileSyncOperation(operationName, CreateBatchRoot(items), targetFolder, device, dispatcher);
        op.SetBatchItems(items);

        return op;
    }

    public static FileSyncOperation CreateTestPullOp(LogicalDeviceViewModel device, int percentage)
    {
        var name = "0f80e01bafd61313cc788efd8ce1ef653e312ee3"[..new Random().Next(5, 33)];
        var source = new SyncFile($"/sdcard/{name}.mp4");
        var target = new SyncFile(@"C:\Temp\test_file.txt");

        var op = new FileSyncOperation(OperationType.Pull, source, target, device, App.AppDispatcher);

        var progressInfo = new AdbSyncProgressInfo(source.FullPath, percentage, percentage, null);
        op.Status = OperationStatus.InProgress;
        op.StatusInfo = new InProgSyncProgressViewModel(progressInfo, DateTime.Now, null, null);
        op._cancelTokenSource = new();

        return op;
    }
}
