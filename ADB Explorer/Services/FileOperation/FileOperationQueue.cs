using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Services;

public class FileOperationQueue : ViewModelBase
{
    #region Full properties

    private int currentOperationIndex = 0;
    public int CurrentOperationIndex
    {
        get => currentOperationIndex;
        private set
        {
            if (Set(ref currentOperationIndex, value))
            {
                OnPropertyChanged(nameof(CurrentOperation));
                UpdateProgress();
            }
        }
    }

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

    private bool autoStart = true;
    public bool AutoStart
    {
        get => autoStart;
        set => Set(ref autoStart, value);
    }

    private bool stopAfterFailure = false;
    public bool StopAfterFailure
    {
        get => stopAfterFailure;
        set => Set(ref stopAfterFailure, value);
    }

    private double progress = 0;
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

    public static string[] NotifyProperties => new[] { nameof(IsActive), nameof(AnyFailedOperations), nameof(Progress) };

    public bool HasIncompleteOperations => CurrentOperation is not null || Operations.Any(op => op.Status == FileOperation.OperationStatus.Waiting);

    public FileOperation CurrentOperation => (CurrentOperationIndex < TotalCount) ? Operations[CurrentOperationIndex] : null;

    public int TotalCount => Operations.Count;

    public string StringProgress => $"{Operations.Count(op => op.Status is FileOperation.OperationStatus.Completed)} / {TotalCount}";

    public bool AnyFailedOperations => Operations.Any(op => op.Status is FileOperation.OperationStatus.Failed);

    #endregion

    private double currOperationLastProgress = 0;
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

            if (AutoStart)
            {
                Start();
            }
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

            if (CurrentOperation == fileOp)
            {
                CurrentOperation.Cancel();
                return;
            }

            for (int i = 0; i < Operations.Count; i++)
            {
                if (Operations[i] == fileOp)
                {
                    Operations.RemoveAt(i);
                    if (i <= CurrentOperationIndex)
                    {
                        CurrentOperationIndex--;
                    }

                    break;
                }
            }

        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    public void MoveOperationsToPast(bool includeAll = false)
    {
        try
        {
            mutex.WaitOne();

            Func<FileOperation, bool> predicate = op =>
                includeAll || op.Status is not FileOperation.OperationStatus.Waiting
                                       and not FileOperation.OperationStatus.InProgress;

            var completed = Operations.Where(predicate).ToList();
            
            foreach (var op in completed)
            {
                PastOperations.Insert(0, op);
            }

            Operations.RemoveAll(completed);
            CurrentOperationIndex = 0;
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

            PastOperations.RemoveAll();
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

            while (CurrentOperationIndex > 0)
            {
                Operations.RemoveAt(0);
                --CurrentOperationIndex;
            }
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

            while (TotalCount > (CurrentOperationIndex + 1))
            {
                Operations.RemoveAt(TotalCount - 1);
            }
        }
        finally 
        {
            mutex.ReleaseMutex();
        }
    }

    private void UpdateProgress(double? currentProgress = null)
    {
        if (currentProgress != null)
        {
            currOperationLastProgress = currentProgress.Value;
        }
        
        Progress = ((double)CurrentOperationIndex + (currOperationLastProgress / 100.0)) / TotalCount;

        FileOpRingVisibility();
    }

    public void Start()
    {
        if (IsActive || (TotalCount == 0))
        {
            return;
        }

        IsActive = true;
        UpdateProgress(0);

        MoveOperationsToPast();

        MoveToNextOperation();
    }

    public void Stop()
    {
        if (CurrentOperation is null)
            return;

        var isPush = CurrentOperation.OperationName is FileOperation.OperationType.Push;
        CurrentOperation.Cancel();
        
        if (isPush && !App.Current.Dispatcher.HasShutdownStarted)
            Data.RuntimeSettings.Refresh = true;
    }

    public void Clear()
    {
        Stop();
        ClearCompleted();
        ClearPending();
    }

    private void MoveToCompleted()
    {
        try
        {
            mutex.WaitOne();

            if ((CurrentOperation != null) && (CurrentOperation.Status != FileOperation.OperationStatus.Waiting))
            {
                CurrentOperation.PropertyChanged -= CurrentOperation_PropertyChanged;
                ++CurrentOperationIndex;
                UpdateProgress(0);
            }

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
        MoveToCompleted();

        try
        {
            mutex.WaitOne();

            if (CurrentOperationIndex == TotalCount)
            {
                return;
            }

            CurrentOperation.PropertyChanged += CurrentOperation_PropertyChanged;
            CurrentOperation.Start();
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    private void CurrentOperation_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (CurrentOperation != sender)
        {
            return;
        }

        if ((e.PropertyName == "Status") &&
            (CurrentOperation.Status != FileOperation.OperationStatus.Waiting) &&
            (CurrentOperation.Status != FileOperation.OperationStatus.InProgress))
        {
            if ((CurrentOperationIndex == (TotalCount - 1)) ||
                (StopAfterFailure && (CurrentOperation.Status == FileOperation.OperationStatus.Failed)) ||
                (CurrentOperation.Status == FileOperation.OperationStatus.Canceled))
            {
                MoveToCompleted();
                IsActive = false;
            }
            else
            {
                MoveToNextOperation();
            }
        }

        if ((e.PropertyName == "StatusInfo") &&
            (CurrentOperation.Status == FileOperation.OperationStatus.InProgress) &&
            (CurrentOperation.StatusInfo is InProgSyncProgressViewModel status) &&
            (status.TotalPercentage is int percentage))
        {
            UpdateProgress(percentage);
        }
    }

    private void FileOpRingVisibility()
    {
        Data.FileActions.IsFileOpRingVisible = IsActive && !AnyFailedOperations && Progress > 0 && TotalCount > 0;
    }

    private void Operations_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateProgress();

        OnPropertyChanged(nameof(TotalCount));
        FileOpRingVisibility();
    }
}
