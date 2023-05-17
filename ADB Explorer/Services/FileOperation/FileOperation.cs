using ADB_Explorer.Controls;
using ADB_Explorer.Models;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Services;

public abstract class FileOperation : ViewModelBase
{
    public enum OperationStatus
    {
        Waiting,
        InProgress,
        Completed,
        Canceled,
        Failed,
    }

    public enum OperationType
    {
        Push,
        Pull,
        Move,
        Delete,
        Recycle,
        Copy,
        Restore,
        Install,
        Update,
    }

    private OperationType operationType;
    public OperationType OperationName
    {
        get => operationType;
        protected set
        {
            if (Set(ref operationType, value))
            {
                OnPropertyChanged(nameof(OpIcon));
                OnPropertyChanged(nameof(CompletedStatsVisible));
                OnPropertyChanged(nameof(FinishedIconVisible));
            }
        }
    }

    public virtual string Tooltip => $"{OperationName}";

    public Dispatcher Dispatcher { get; }

    public ADBService.AdbDevice Device { get; }
    public FilePath FilePath { get; }

    private OperationStatus status;
    public OperationStatus Status
    {
        get => status;
        protected set
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                if (Set(ref status, value))
                {
                    OnPropertyChanged(nameof(CompletedStatsVisible));
                    OnPropertyChanged(nameof(FinishedIconVisible));
                }
            });
        }
    }

    public bool CompletedStatsVisible => Status is OperationStatus.Completed
                                         && OperationName is OperationType.Push or OperationType.Pull;

    public bool FinishedIconVisible => ((OperationName is not OperationType.Push and not OperationType.Pull) || Status is OperationStatus.Canceled or OperationStatus.Failed)
                                       && Status is not OperationStatus.InProgress and not OperationStatus.Waiting;

    public virtual object OpIcon => OperationName switch
    {
        OperationType.Pull => new PullIcon(),
        OperationType.Push => new PushIcon(),
        OperationType.Recycle => new RecycleIcon(),
        OperationType.Move => new FontIcon() { Glyph = "\uE8DE" },
        OperationType.Delete => new FontIcon() { Glyph = "\uE74D" },
        OperationType.Copy => new FontIcon() { Glyph = "\uE8C8" },
        OperationType.Restore => new FontIcon() { Glyph = "\uE845" },
        OperationType.Update => new FontIcon() { Glyph = "\uE787" },
        OperationType.Install => null,
        _ => throw new NotSupportedException(),
    };

    private object statusInfo;
    public object StatusInfo
    {
        get => statusInfo;
        protected set => Dispatcher.Invoke(() => Set(ref statusInfo, value));
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
}
