using ADB_Explorer.Helpers;
using ADB_Explorer.Services;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;

namespace ADB_Explorer.Models
{
    public class FileOperationQueue : INotifyPropertyChanged
    {
        public Dispatcher Dispatcher { get; }

        public MyObservableCollection<FileOperation> PendingOperations { get; } = new();
        public MyObservableCollection<FileOperation> CompletedOperations { get; } = new();

        private FileOperation currentOperation = null;
        public FileOperation CurrentOperation
        {
            get
            {
                return currentOperation;
            }
            private set
            {
                if (currentOperation != value)
                {
                    currentOperation = value;
                    NotifyPropertyChanged();
                }
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

        public int TotalCount
        {
            get
            {
                return PendingOperations.Count + ((CurrentOperation != null) ? 1 : 0) + CompletedOperations.Count;
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

        public void Start()
        {
            if ((!IsActive) && (PendingOperations.Count > 0))
            {
                IsActive = true;
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
            if (CurrentOperation.Status != FileOperation.OperationStatus.Waiting)
            {
                if ((PendingOperations.Count == 0) ||
                    (StopAfterFailure && (CurrentOperation.Status == FileOperation.OperationStatus.Failed)) ||
                    (CurrentOperation.Status == FileOperation.OperationStatus.Canceled))
                {
                    MoveToCompleted();
                    IsActive = false;
                }
                else if (CurrentOperation.Status == FileOperation.OperationStatus.Completed)
                {
                    MoveToNextOperation();
                }
            }

            NotifyPropertyChanged("CurrentOperation");
        }

        private void PendingOperations_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (AutoStart && (!IsActive) && (PendingOperations.Count > 0) &&
                ((e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add) ||
                 (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)))
            {
                Dispatcher.BeginInvoke(Start);
            }

            NotifyPropertyChanged("PendingOperations");
        }

        private void CompletedOperations_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            NotifyPropertyChanged("CompletedOperations");
        }

        protected virtual void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
