using ADB_Explorer.Helpers;
using ADB_Explorer.Services;
using System.ComponentModel;

namespace ADB_Explorer.Models
{
    public class FileOperationQueue : INotifyPropertyChanged
    {
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
                    PropertyChanged(this, new PropertyChangedEventArgs("CurrentOperation"));
                }
            }
        }

        public MyObservableCollection<FileOperation> PendingOperations { get; private set; } = new();
        public MyObservableCollection<FileOperation> CompletedOperations { get; private set; } = new();

        public bool IsActive { get; private set; } = false;
        public bool AutoStart { get; set; } = true;
        public bool StopAfterFailure { get; set; } = false;

        public int TotalCount
        {
            get
            {
                return PendingOperations.Count + ((CurrentOperation != null) ? 1 : 0) + CompletedOperations.Count;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public void AddOperation(FileOperation fileOp)
        {
            PendingOperations.Add(fileOp);
            
            if (AutoStart)
            {
                Start();
            }
        }

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

            CurrentOperation = PendingOperations[0];
            PendingOperations.RemoveAt(0);

            CurrentOperation.PropertyChanged += CurrentOperation_PropertyChanged;
            CurrentOperation.Start();
        }

        private void CurrentOperation_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if ((CurrentOperation.Status == FileOperation.OperationStatus.Failed) ||
                (CurrentOperation.Status == FileOperation.OperationStatus.Completed))
            {
                if ((PendingOperations.Count == 0) ||
                    (StopAfterFailure && (CurrentOperation.Status == FileOperation.OperationStatus.Failed)))
                {
                    MoveToCompleted();
                    IsActive = false;
                }
                else
                {
                    MoveToNextOperation();
                }
            }
        }
    }
}
