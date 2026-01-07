using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Services;

public class FileOperationQueue : ViewModelBase
{
    #region Full properties

    private bool isActive;
    public bool IsActive
    {
        get => isActive;
        set
        {
            if (Set(ref isActive, value))
                FileOpRingVisibility();
        }
    }

    private bool isAutoPlayOn = true;
    public bool IsAutoPlayOn
    {
        get => isAutoPlayOn;
        set => Set(ref isAutoPlayOn, value);
    }

    private double progress = 0.0;
    public double Progress
    {
        get => progress;
        set
        {
            if (Set(ref progress, value))
            {
                OnPropertyChanged(nameof(AnyFailedOperations));
                OnPropertyChanged(nameof(StringProgress));
                FileOpRingVisibility();
            }
        }
    }

    #endregion

    #region Read only properties

    public ObservableList<FileOperation> Operations { get; } = [];

    public bool CurrentChanged { get => false; set => OnPropertyChanged(); }

    public static string[] NotifyProperties => [nameof(IsActive), nameof(AnyFailedOperations), nameof(Progress)];

    public bool HasIncompleteOperations => Operations.Any(op => op.Status
        is FileOperation.OperationStatus.Waiting
        or FileOperation.OperationStatus.InProgress);

    public int TotalCount => Operations.Count(op => !op.IsPastOp);

    public string StringProgress => $"{Operations.Count(op => op.Status is FileOperation.OperationStatus.Completed)} / {TotalCount}";

    public bool AnyFailedOperations => Operations.Any(op => !op.IsPastOp && op.Status is FileOperation.OperationStatus.Failed);

    #endregion

    private readonly Mutex mutex = new();

    public FileOperationQueue()
    {
        Operations.CollectionChanged += Operations_CollectionChanged;
    }

    public void AddOperation(FileOperation fileOp)
    {
        try
        {
            mutex.WaitOne();

            Operations.Add(fileOp);
            OnPropertyChanged(nameof(HasIncompleteOperations));

            Start();
        } 
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    public void AddOperations(IEnumerable<FileOperation> operations)
    {
        try
        {
            mutex.WaitOne();

            Operations.AddRange(operations);
            OnPropertyChanged(nameof(HasIncompleteOperations));

            Start();
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    public void RemoveOperation(FileOperation fileOp)
    {
        try
        {
            mutex.WaitOne();

            if (fileOp.Status is FileOperation.OperationStatus.InProgress)
            {
                fileOp.Cancel();
                return;
            }

            Operations.Remove(fileOp);
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    public void MoveOperationsToPast(bool includeAll = false, DeviceViewModel device = null)
    {
        try
        {
            mutex.WaitOne();

            Func<FileOperation, bool> predicate = op => {
                if (device is not null && op.Device.ID != device.ID)
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
            mutex.ReleaseMutex();
        }
    }

    private void UpdateProgress()
    {
        var pending = Operations.Where(op => op.Status is FileOperation.OperationStatus.Waiting);
        var running = Operations.Where(op => op.Status is FileOperation.OperationStatus.InProgress);

        double done = TotalCount - pending.Count() - running.Count();
        double current = running.Sum(op => op.LastProgress) / 100.0;

        Progress = (done + current) / TotalCount;

        FileOpRingVisibility();
    }

    public void Start()
    {
        if (TotalCount < 1 || !IsAutoPlayOn)
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
        
        if (isPush && !App.Current.Dispatcher.HasShutdownStarted)
            Data.RuntimeSettings.Refresh = true;
    }

    private void MoveToCompleted(FileOperation op)
    {
        try
        {
            mutex.WaitOne();

            op.PropertyChanged -= CurrentOperation_PropertyChanged;
            UpdateProgress();

            OnPropertyChanged(nameof(HasIncompleteOperations));
            FileOpRingVisibility();
        }
        finally
        { 
            mutex.ReleaseMutex();
        }
    }

    private void MoveToNextOperation()
    {
        try
        {
            mutex.WaitOne();

            var pending = Operations.Where(op => op.Status is FileOperation.OperationStatus.Waiting);
            if (pending.Any())
            {
                // Group by operation type and device
                var groups = pending.GroupBy(op => op.TypeOnDevice);
                foreach (var item in groups)
                {
                    List<FileOperation> operations = [item.First()];
                    if (!Data.Settings.AllowMultiOp)
                    {
                        // Skip if there's already an operation in progress on the same device and of the same type
                        if (Operations.Any(op => op.Status
                            is FileOperation.OperationStatus.InProgress
                            && op.TypeOnDevice == item.Key))
                        {
                            continue;
                        }
                    }
                    else
                    {
                        // AdvancedAdbSharp allows (and boosts performance with) simultaneous sync operations
                        if (item.First().OperationName
                            is FileOperation.OperationType.Push
                            or FileOperation.OperationType.Pull)
                        {
                            operations = [.. item];
                        }
                    }

                    foreach (var op in operations)
                    {
                        op.PropertyChanged += CurrentOperation_PropertyChanged;
                        op.Start();
                    }

                    CurrentChanged = true;
                }
            }
            else if (!Operations.Any(op => op.Status is FileOperation.OperationStatus.InProgress))
            {
                IsActive = false;
                CurrentChanged = true;
            }
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    private void CheckForRescan(FileOperation fileOp)
    {
        var target = fileOp.TargetPath.ParentPath;
        if (fileOp.Device.Device.AndroidVersion < AdbExplorerConst.MIN_MEDIA_SCAN_ANDROID_VER
            || Operations.Any(op =>
                op.TypeOnDevice == fileOp.TypeOnDevice
                && op.TargetPath.ParentPath == target
                && op.Status is FileOperation.OperationStatus.Waiting or FileOperation.OperationStatus.InProgress))
        {
            return;
        }

        ADBService.AdbDevice.ForceMediaScan(fileOp.Device.Device);
    }

    private void CurrentOperation_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        var op = (FileOperation)sender;
        
        if (e.PropertyName is nameof(FileOperation.Status))
        {
            Data.RuntimeSettings.IsPollingStopped = Data.Settings.StopPollingOnSync
                && Operations.Any(op => op is FileSyncOperation && op.Status is FileOperation.OperationStatus.InProgress);

            if (op.Status
                is not FileOperation.OperationStatus.Waiting
                and not FileOperation.OperationStatus.InProgress)
            {
                MoveToCompleted(op);

                if (IsAutoPlayOn)
                    MoveToNextOperation();

                if (op.OperationName is FileOperation.OperationType.Push
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

    private void FileOpRingVisibility()
    {
        Data.FileActions.IsFileOpRingVisible = IsActive && !AnyFailedOperations && Progress > 0 && TotalCount > 0;
    }

    private void Operations_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(TotalCount));
        UpdateProgress();

        if (e.Action is not NotifyCollectionChangedAction.Reset && e.NewItems is null)
            return;

        foreach (FileOperation item in Operations.Where(op => op.Status is FileOperation.OperationStatus.None))
        {
            item.BeginWaiting();
        }
    }
}
