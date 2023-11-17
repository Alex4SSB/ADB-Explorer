﻿using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Services;

public class FileOperationQueue : ViewModelBase
{
    #region Full properties

    private bool isActive = false;
    public bool IsActive
    {
        get => isActive;
        set
        {
            if (Set(ref isActive, value))
                FileOpRingVisibility();
        }
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

    public ObservableList<FileOperation> Operations { get; } = new();

    public ObservableList<FileOperation> PastOperations { get; } = new();

    public bool CurrentChanged { get => false; set => OnPropertyChanged(); }

    public static string[] NotifyProperties => new[] { nameof(IsActive), nameof(AnyFailedOperations), nameof(Progress) };

    public bool HasIncompleteOperations => Operations.Any(op => op.Status
        is FileOperation.OperationStatus.Waiting
        or FileOperation.OperationStatus.InProgress);

    public int TotalCount => Operations.Count;

    public string StringProgress => $"{Operations.Count(op => op.Status is FileOperation.OperationStatus.Completed)} / {TotalCount}";

    public bool AnyFailedOperations => Operations.Any(op => op.Status is FileOperation.OperationStatus.Failed);

    #endregion

    private readonly Mutex mutex = new Mutex();

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

            var completed = Operations.Where(predicate).ToList();
            
            foreach (var op in completed)
            {
                PastOperations.Insert(0, op);
            }

            Operations.RemoveAll(completed);
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    public void ClearPast()
    {
        try
        {
            mutex.WaitOne();

            PastOperations.Clear();
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    public void ClearCompleted()
    {
        try
        {
            mutex.WaitOne();

            Operations.RemoveAll(op => op.Status
                is not FileOperation.OperationStatus.Waiting
                and not FileOperation.OperationStatus.InProgress);
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    public void ClearPending()
    {
        try
        {
            mutex.WaitOne();

            Operations.RemoveAll(op => op.Status is FileOperation.OperationStatus.Waiting);
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
        if (TotalCount < 1)
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

    public void Clear()
    {
        Stop();
        ClearCompleted();
        ClearPending();
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
                var groups = pending.GroupBy(op => op.TypeOnDevice);
                foreach (var item in groups)
                {
                    if (Operations.Any(op => op.Status is FileOperation.OperationStatus.InProgress && op.TypeOnDevice == item.Key))
                        continue;

                    item.First().PropertyChanged += CurrentOperation_PropertyChanged;
                    item.First().Start();

                    CurrentChanged = true;
                }
            }
            else if (!Operations.Any(op => op.Status is FileOperation.OperationStatus.InProgress))
            {
                IsActive = false;
            }
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    private void CurrentOperation_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        var op = (FileOperation)sender;
        
        if (e.PropertyName is nameof(FileOperation.Status) && op.Status
            is not FileOperation.OperationStatus.Waiting
            and not FileOperation.OperationStatus.InProgress)
        {
            MoveToCompleted(op);
            MoveToNextOperation();
        }

        if (e.PropertyName is nameof(FileOperation.StatusInfo)
            && op.Status is FileOperation.OperationStatus.InProgress
            && op.StatusInfo is InProgSyncProgressViewModel status
            && status.TotalPercentage is int percentage)
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
    }
}
