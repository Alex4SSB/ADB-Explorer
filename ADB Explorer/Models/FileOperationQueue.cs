using ADB_Explorer.Helpers;
using ADB_Explorer.Services;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using static ADB_Explorer.Services.ADBService.Device;

namespace ADB_Explorer.Models
{
    public class FileOperationQueue : INotifyPropertyChanged
    {
        public Dispatcher Dispatcher { get; }

        public MyObservableCollection<FileOperation> PendingOperations { get; } = new();
        public MyObservableCollection<FileOperation> CurrentOperations { get; } = new();
        public MyObservableCollection<FileOperation> CompletedOperations { get; } = new();

        public FileOperation CurrentOperation
        {
            get
            {
                return (CurrentOperations.Count > 0) ? CurrentOperations[0] : null;
            }
            private set
            {
                CurrentOperations.Clear();

                if (value != null)
                {
                    CurrentOperations.Add(value);
                }

                NotifyPropertyChanged();
            }
        }

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

        public int TotalCount
        {
            get
            {
                return PendingOperations.Count + CurrentOperations.Count + CompletedOperations.Count;
            }
        }

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
            PendingOperations.CollectionChanged += PendingOperations_CollectionChanged;
            CompletedOperations.CollectionChanged += CompletedOperations_CollectionChanged;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public void AddOperation(FileOperation fileOp) => PendingOperations.Add(fileOp);

        private void UpdateProgress(double? currentProgress = null)
        {
            if (currentProgress != null)
            {
                currOperationLastProgress = currentProgress.Value;
            }
            
            Progress = ((double)CompletedOperations.Count + (currOperationLastProgress / 100.0)) / TotalCount; // * 100.0
        }

        public void Start()
        {
            if ((!IsActive) && (PendingOperations.Count > 0))
            {
                IsActive = true;
                UpdateProgress(0);

                if (AutoClear)
                {
                    CompletedOperations.Clear();
                }

                MoveToNextOperation();
            }
        }

        private void MoveToCompleted()
        {
            if (CurrentOperation != null)
            {
                CurrentOperation.PropertyChanged -= CurrentOperation_PropertyChanged;
                CompletedOperations.Add(CurrentOperation);
                CurrentOperation = null;
                UpdateProgress(0);
            }
        }

        private void MoveToNextOperation()
        {
            MoveToCompleted();

            if (PendingOperations.Count == 0)
            {
                return;
            }

            CurrentOperation = PendingOperations[0];
            PendingOperations.RemoveAt(0);

            CurrentOperation.PropertyChanged += CurrentOperation_PropertyChanged;

            if (CurrentOperation.Status != FileOperation.OperationStatus.Waiting)
            {
                MoveToNextOperation();
            }
            else
            {
                CurrentOperation.Start();
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
                if ((PendingOperations.Count == 0) ||
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
                (CurrentOperation.StatusInfo is AdbSyncProgressInfo status) &&
                (status.TotalPercentage is int percentage))
            {
                UpdateProgress(percentage);
            }
        }

        private void PendingOperations_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (AutoStart && (!IsActive) && (PendingOperations.Count > 0) &&
                ((e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add) ||
                 (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)))
            {
                Dispatcher.BeginInvoke(Start);
            }

            UpdateProgress();
        }

        private void CompletedOperations_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            UpdateProgress();
        }

        protected virtual void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
