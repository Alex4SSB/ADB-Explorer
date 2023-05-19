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

    #region Notifiable Properties

    private OperationType operationType;
    public OperationType OperationName
    {
        get => operationType;
        protected set
        {
            if (Set(ref operationType, value))
            {
                OnPropertyChanged(nameof(OpIcon));
            }
        }
    }

    private OperationStatus status;
    public OperationStatus Status
    {
        get => status;
        protected set
        {
            Dispatcher.Invoke(() => Set(ref status, value));
        }
    }

    private FileOpProgressViewModel statusInfo = new WaitingOpProgressViewModel();
    public FileOpProgressViewModel StatusInfo
    {
        get => statusInfo;
        protected set => Dispatcher.Invoke(() => Set(ref statusInfo, value));
    }

    #endregion

    #region Base Properties

    public Dispatcher Dispatcher { get; }

    public ADBService.AdbDevice Device { get; }

    public FilePath FilePath { get; }

    public FilePath TargetPath { get; protected set; }

    #endregion

    #region Read-only Properties

    public virtual string Tooltip => $"{OperationName}";

    public virtual FrameworkElement OpIcon => OperationName switch
    {
        OperationType.Pull => new PullIcon(),
        OperationType.Push => new PushIcon(),
        OperationType.Recycle => new RecycleIcon(),
        OperationType.Move => new FontIcon() { Glyph = "\uE8DE" },
        OperationType.Delete => new FontIcon() { Glyph = "\uE74D" },
        OperationType.Copy => new FontIcon() { Glyph = "\uE8C8" },
        OperationType.Restore => new FontIcon() { Glyph = "\uE845" },
        OperationType.Update => new FontIcon() { Glyph = "\uE787" },
        OperationType.Install => null, // gets overridden
        _ => throw new NotSupportedException(),
    };

    #endregion

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
