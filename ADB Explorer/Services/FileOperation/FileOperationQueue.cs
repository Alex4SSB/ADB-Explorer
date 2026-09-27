namespace ADB_Explorer.Services;

public class FileOperationQueue : ObservableObject
{
    #region Full properties

    private bool _isActive;
    public bool IsActive
    {
        get => _isActive; 
        set => SetProperty(ref _isActive, value);
    }

    private bool _isAutoPlayStopped = false;
    public bool IsAutoPlayStopped
    {
        get => _isAutoPlayStopped;
        set
        {
            if (SetProperty(ref _isAutoPlayStopped, value))
            {
                if (_isAutoPlayStopped)
                    Stop();
                else
                    Start();
            }
        }
    }

    private double _progress = 0.0;
    public double Progress
    {
        get => _progress;
        set
        {
            if (SetProperty(ref _progress, value))
            {
                OnPropertyChanged(nameof(AnyFailedOperations));
            }
        }
    }

    #endregion

    #region Read only properties

    public ObservableList<FileOperation> Operations { get; } = [];

    public static string[] NotifyProperties => [nameof(IsActive), nameof(AnyFailedOperations), nameof(Progress)];

    public bool HasIncompleteOperations => Operations.Any(op => op.Status
        is FileOperation.OperationStatus.Waiting
        or FileOperation.OperationStatus.InProgress);

    public int TotalCount => Operations.Count(op => !op.IsPastOp);

    public string StringProgress => $"{Operations.Count(op => op.Status is FileOperation.OperationStatus.Completed)} / {TotalCount}";

    public bool AnyFailedOperations => Operations.Any(op => !op.IsPastOp && op.Status is FileOperation.OperationStatus.Failed);

    #endregion

    private readonly Mutex _mutex = new();

    public FileOperationQueue()
    {
        Operations.CollectionChanged += Operations_CollectionChanged;
        Data.Settings.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName is nameof(AppSettings.StopPollingOnSync))
            {
                Data.RuntimeSettings.IsPollingStopped = Data.Settings.StopPollingOnSync
                    && Operations.Any(op => op is FileSyncOperation or FileTransferOperation && op.Status is FileOperation.OperationStatus.InProgress);
            }
        };
    }

    public void AddOperation(FileOperation fileOp)
    {
        try
        {
            _mutex.WaitOne();

            Operations.Add(fileOp);

            Start();
        } 
        finally
        {
            _mutex.ReleaseMutex();
        }
    }

    public void AddOperations(IEnumerable<FileOperation> operations)
    {
        try
        {
            _mutex.WaitOne();

            Operations.AddRange(operations);

            Start();
        }
        finally
        {
            _mutex.ReleaseMutex();
        }
    }

    public void RemoveOperation(FileOperation fileOp)
    {
        try
        {
            _mutex.WaitOne();

            if (fileOp.Status is FileOperation.OperationStatus.InProgress)
            {
                fileOp.Cancel();
                return;
            }

            Operations.Remove(fileOp);
        }
        finally
        {
            _mutex.ReleaseMutex();
        }
    }

    public void MoveOperationsToPast(bool includeAll = false, DeviceViewModel? device = null)
    {
        try
        {
            _mutex.WaitOne();

            Func<FileOperation, bool> predicate = op => {
                if (device is not null && op.Device.ID != device.ID && op.TargetDevice?.ID != device.ID)
                    return false;

                return includeAll || op.Status
                    is not FileOperation.OperationStatus.Waiting
                    and not FileOperation.OperationStatus.InProgress;
            };

            foreach (var op in Operations.Where(predicate))
            {
                op.IsPastOp = true;
            }
        }
        finally
        {
            _mutex.ReleaseMutex();
        }
    }

    private void UpdateProgress()
    {
        var pending = Operations.Where(op => op.Status is FileOperation.OperationStatus.Waiting);
        var running = Operations.Where(op => op.Status is FileOperation.OperationStatus.InProgress);

        double done = TotalCount - pending.Count() - running.Count();
        double current = running.Sum(op => op.LastProgress) / 100.0;

        Progress = (done + current) / TotalCount;
    }

    public void Start()
    {
        if (TotalCount < 1 || IsAutoPlayStopped)
            return;

        if (!IsActive)
        {
            IsActive = true;
            MoveOperationsToPast();
        }

        MoveToNextOperation();

        UpdateProgress();
    }

    public void Stop()
    {
        var runningOps = Operations.Where(op => op.Status is FileOperation.OperationStatus.InProgress);
        var isPush = runningOps.Any(op => op.OperationName is FileOperation.OperationType.Push);

        foreach (var item in runningOps)
        {
            item.Cancel();
        }
        IsActive = false;
        
        if (isPush && !App.IsShuttingDown)
            App.SafeBeginInvoke(FileActionLogic.Refresh);
    }

    private void MoveToCompleted(FileOperation op)
    {
        try
        {
            _mutex.WaitOne();

            op.PropertyChanged -= CurrentOperation_PropertyChanged;
            UpdateProgress();

        }
        finally
        { 
            _mutex.ReleaseMutex();
        }
    }

    private void MoveToNextOperation()
    {
        try
        {
            _mutex.WaitOne();

            var pending = Operations.Where(op => op.Status is FileOperation.OperationStatus.Waiting);
            if (pending.Any())
            {
                // Group by operation type and device
                var groups = pending.GroupBy(op => op.TypeOnDevice);
                foreach (var item in groups)
                {
                    var maxOps = Data.Settings.MaxSimultaneousOps;
                    var inProgressCount = Operations.Count(op => op.Status
                        is FileOperation.OperationStatus.InProgress
                        && op.TypeOnDevice == item.Key);
                    var availableSlots = maxOps - inProgressCount;
                    if (availableSlots <= 0)
                        continue;

                    // AdvancedAdbSharp allows (and boosts performance with) simultaneous sync operations
                    List<FileOperation> operations;
                    if (item.First().OperationName
                        is FileOperation.OperationType.Push
                        or FileOperation.OperationType.Pull)
                    {
                        operations = [.. item.Take(availableSlots)];
                    }
                    else
                    {
                        operations = [item.First()];
                    }

                    foreach (var op in operations)
                    {
                        op.PropertyChanged += CurrentOperation_PropertyChanged;
                        op.Start();
                    }

                }
            }
            else if (!Operations.Any(op => op.Status is FileOperation.OperationStatus.InProgress))
            {
                IsActive = false;
            }
        }
        finally
        {
            _mutex.ReleaseMutex();
        }
    }

    private void CheckForRescan(FileOperation fileOp)
    {
        // A device to device transfer writes to its target device.
        var device = fileOp.TargetDevice ?? fileOp.Device;
        var target = fileOp.TargetPath.ParentPath;
        if (device.AndroidVersion < AdbExplorerConst.MIN_MEDIA_SCAN_ANDROID_VER
            || Operations.Any(op =>
                op.TypeOnDevice == fileOp.TypeOnDevice
                && op.TargetPath.ParentPath == target
                && op.Status is FileOperation.OperationStatus.Waiting or FileOperation.OperationStatus.InProgress))
        {
            return;
        }

        AdbService.ForceMediaScan(device);
    }

    private void CurrentOperation_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not FileOperation op)
            return;


        if (e.PropertyName is nameof(FileOperation.Status))
        {
            Data.RuntimeSettings.IsPollingStopped = Data.Settings.StopPollingOnSync
                && Operations.Any(op => op is FileSyncOperation or FileTransferOperation && op.Status is FileOperation.OperationStatus.InProgress);

            if (op.Status
                is not FileOperation.OperationStatus.Waiting
                and not FileOperation.OperationStatus.InProgress)
            {
                MoveToCompleted(op);

                if (!IsAutoPlayStopped)
                    MoveToNextOperation();

                if ((op.OperationName is FileOperation.OperationType.Push || op is FileTransferOperation)
                    && Data.Settings.RescanOnPush)
                    Task.Run(() => CheckForRescan(op));
            }
        }

        if (e.PropertyName is nameof(FileOperation.StatusInfo)
            && op.Status is FileOperation.OperationStatus.InProgress
            && op.StatusInfo is InProgSyncProgressViewModel { TotalPercentage: double percentage })
        {
            op.LastProgress = percentage;
            UpdateProgress();
        }
    }

    private void Operations_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateProgress();

        if (e.Action is not NotifyCollectionChangedAction.Reset && e.NewItems is null)
            return;

        foreach (FileOperation item in Operations.Where(op => op.Status is FileOperation.OperationStatus.None))
        {
            item.BeginWaiting();
        }
    }
}
