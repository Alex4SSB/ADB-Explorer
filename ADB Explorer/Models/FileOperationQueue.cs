using ADB_Explorer.Helpers;
using ADB_Explorer.Services;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Threading;

namespace ADB_Explorer.Models
{
    public class FileOperationQueue : INotifyPropertyChanged
    {
        public Dispatcher Dispatcher { get; }

        public ObservableList<FileOperation> Operations { get; } = new();

        public int CurrentOperationIndex { get; private set; } = 0;

        public FileOperation CurrentOperation => (CurrentOperationIndex < TotalCount) ? Operations[CurrentOperationIndex] : null;

        private bool isActive = false;

        public bool IsActive {
            get
            {
                return isActive;
            }
            private set
            {
                if (isActive != value)
                {
                    isActive = value;
                    NotifyPropertyChanged();
                }
            }
        }

        private bool autoStart = true;

        public bool AutoStart
        {
            get
            {
                return autoStart;
            }
            set
            {
                if (autoStart != value)
                {
                    autoStart = value;
                    NotifyPropertyChanged();
                }
            }
        }

        private bool stopAfterFailure = false;

        public bool StopAfterFailure
        {
            get
            {
                return stopAfterFailure;
            }
            set
            {
                if (stopAfterFailure != value)
                {
                    stopAfterFailure = value;
                    NotifyPropertyChanged();
                }
            }
        }

        private bool autoClear = true;
        public bool AutoClear
        {
            get
            {
                return autoClear;
            }
            set
            {
                if (autoClear != value)
                {
                    autoClear = value;
                    NotifyPropertyChanged();
                }
            }
        }

        public int TotalCount => Operations.Count;

        private double currOperationLastProgress = 0;
        private double progress = 0;
        public double Progress
        {
            get
            {
                return progress;
            }
            private set
            {
                if (progress != value)
                {
                    progress = value;
                    NotifyPropertyChanged();
                }
            }
        }

        public FileOperationQueue(Dispatcher dispatcher)
        {
            Dispatcher = dispatcher;
            Operations.CollectionChanged += Operations_CollectionChanged;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private Mutex mutex = new Mutex();

        public void AddOperation(FileOperation fileOp)
        {
            try
            {
                mutex.WaitOne();

                Operations.Add(fileOp);

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

                while (TotalCount > CurrentOperationIndex)
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
        }

        public void Start()
        {
            if (IsActive || (TotalCount == 0))
            {
                return;
            }

            IsActive = true;
            UpdateProgress(0);

            if (AutoClear)
            {
                ClearCompleted();
            }

            MoveToNextOperation();
        }

        public void Stop() => CurrentOperation?.Cancel();

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
                if ((CurrentOperationIndex == TotalCount) ||
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
                (CurrentOperation.StatusInfo is FileSyncOperation.InProgressInfo status) &&
                (status.TotalPercentage is int percentage))
            {
                UpdateProgress(percentage);
            }
        }

        private void Operations_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            UpdateProgress();
        }

        protected virtual void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
