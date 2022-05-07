using ADB_Explorer.Models;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Threading;

namespace ADB_Explorer.Services
{
    public abstract class FileOperation : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public enum OperationStatus
        {
            Waiting,
            InProgress,
            Completed,
            Canceled,
            Failed
        }

        public enum OperationType
        {
            Push,
            Pull,
            Move,
            Delete,
            Recycle,
            Copy
        }

        public OperationType OperationName { get; protected set; }

        public Dispatcher Dispatcher { get; }

        public ADBService.AdbDevice Device { get; }
        public FilePath FilePath { get; }

        private OperationStatus status;
        public OperationStatus Status
        {
            get
            {
                return status;
            }
            protected set
            {
                if (Dispatcher != null && !Dispatcher.CheckAccess())
                {
                    Dispatcher.Invoke(() => Status = value);
                    return;
                }

                status = value;
                NotifyPropertyChanged();
            }
        }

        private object statusInfo;
        public object StatusInfo
        {
            get
            {
                return statusInfo;
            }
            protected set
            {
                if (Dispatcher != null && !Dispatcher.CheckAccess())
                {
                    Dispatcher.Invoke(() => StatusInfo = value);
                    return;
                }

                statusInfo = value;
                NotifyPropertyChanged();
            }
        }

        public FilePath TargetPath { get; set; }

        public FileOperation(Dispatcher dispatcher, ADBService.AdbDevice adbDevice, FilePath filePath)
        {
            Dispatcher = dispatcher;
            Device = adbDevice;
            FilePath = filePath;
            Status = OperationStatus.Waiting;
        }

        public abstract void Start();
        public abstract void Cancel();

        protected virtual void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
