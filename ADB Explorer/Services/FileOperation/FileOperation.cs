using ADB_Explorer.Controls;
using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.ViewModels;
using static ADB_Explorer.Services.FileAction;

namespace ADB_Explorer.Services;

public abstract class FileOperation : ViewModelBase
{
    public enum OperationStatus
    {
        InProgress,
        Waiting,
        Completed,
        Failed,
        Canceled,
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
        Rename,
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
            Dispatcher.Invoke(() =>
            {
                if (Set(ref status, value))
                {
                    CancelTokenSource = value is OperationStatus.InProgress ? new() : null;

                    OnPropertyChanged(nameof(ValidationAllowed));

                    LastProgress = 0;
                }
            });
        }
    }

    private FileOpProgressViewModel statusInfo = new WaitingOpProgressViewModel();
    public FileOpProgressViewModel StatusInfo
    {
        get => statusInfo;
        set => Dispatcher.Invoke(() => Set(ref statusInfo, value));
    }

    private bool isPastOp = false;
    public bool IsPastOp
    {
        get => isPastOp;
        set => Set(ref isPastOp, value);
    }

    #endregion

    #region Base Properties

    public CancellationTokenSource CancelTokenSource;

    public Dispatcher Dispatcher { get; }

    public ADBService.AdbDevice Device { get; }

    public virtual FilePath FilePath { get; }

    public virtual SyncFile TargetPath { get; protected set; }

    public double LastProgress = 0.0;

    #endregion

    #region Read-only Properties

    public string TypeOnDevice => $"{OperationName}@{Device.ID}";

    public ObservableList<SyncFile> Children => AndroidPath.Children;

    public string SourcePathString
    {
        get
        {
            if (FilePath is null)
                return "";

            if (FilePath.ParentPath == AdbExplorerConst.RECYCLE_PATH)
                return "Recycle Bin";
            else if (OperationName is OperationType.Rename)
                return FileHelper.ConcatPaths(FilePath.ParentPath, FilePath.DisplayName);
            else
                return FilePath.ParentPath;
        }
    }

    public string TargetPathString
    {
        get
        {
            if (TargetPath is null)
                return "";

            return OperationName switch
            {
                OperationType.Delete => "Permanent Deletion",
                OperationType.Recycle => "Recycle Bin",
                _ => TargetPath.ParentPath,
            };
        }
    }

    public abstract SyncFile AndroidPath { get; }

    public virtual string Tooltip => $"{OperationName}";

    public virtual FrameworkElement OpIcon => OperationName switch
    {
        OperationType.Pull => new PullIcon(),
        OperationType.Push => new PushIcon(),
        OperationType.Recycle => new RecycleIcon(),
        OperationType.Move => new FontIcon() { Glyph = "\uE8DE" },
        OperationType.Delete => new FontIcon() { Glyph = AppActions.Icons[FileActionType.Delete] },
        OperationType.Copy => new FontIcon() { Glyph = AppActions.Icons[FileActionType.Copy] },
        OperationType.Restore => new FontIcon() { Glyph = AppActions.Icons[FileActionType.Restore] },
        OperationType.Update => new FontIcon() { Glyph = AppActions.Icons[FileActionType.UpdateModified] },
        OperationType.Install => null, // gets overridden
        OperationType.Rename => new FontIcon() { Glyph = AppActions.Icons[FileActionType.Rename] },
        _ => throw new NotSupportedException(),
    };

    public bool ValidationAllowed
    {
        get
        {
            if (OperationName is not (OperationType.Push or OperationType.Pull or OperationType.Copy))
                return false;

            if (Status is not OperationStatus.Completed)
                return false;

            return !StatusInfo.IsValidationInProgress 
                && Device.Device.Status is AbstractDevice.DeviceStatus.Ok;
        }
    }

    #endregion

    public BaseAction SourceAction { get; private set; }
    public BaseAction TargetAction { get; private set; }

    public FileOperation(FilePath filePath, ADBService.AdbDevice adbDevice, Dispatcher dispatcher)
    {
        Dispatcher = dispatcher;
        Device = adbDevice;
        FilePath = filePath;
        Status = OperationStatus.Waiting;

        SourceAction = new(
            () => FilePath.PathType is AbstractFile.FilePathType.Windows || Device.Status is AbstractDevice.DeviceStatus.Ok,
            () => OpenLocation(FilePath));

        TargetAction = new(
            () => TargetPath.PathType is AbstractFile.FilePathType.Windows || (Device.Status is AbstractDevice.DeviceStatus.Ok && OperationName is not OperationType.Delete),
            () => OpenLocation(TargetPath));
    }

    private void OpenLocation(FilePath file)
    {
        if (file.PathType is AbstractFile.FilePathType.Windows)
            Process.Start("explorer.exe", file.ParentPath);
        else
        {
            if (!Device.Device.IsOpen)
                Data.RuntimeSettings.DeviceToOpen = Device.Device;

            Data.RuntimeSettings.LocationToNavigate = file.ParentPath;
        }
    }

    public void SetValidation(bool value)
    {
        StatusInfo.IsValidationInProgress = value;
        OnPropertyChanged(nameof(ValidationAllowed));

        if (Data.FileActions.SelectedFileOps.Value.Contains(this))
            Data.RuntimeSettings.RefreshFileOpControls = true;
    }

    public abstract void Start();

    public abstract void ClearChildren();

    public abstract void AddUpdates(IEnumerable<FileOpProgressInfo> newUpdates);

    public abstract void AddUpdates(params FileOpProgressInfo[] newUpdates);

    public virtual void Cancel()
    {
        if (Status != OperationStatus.InProgress)
        {
            throw new Exception("Cannot cancel a deactivated operation!");
        }

        CancelTokenSource.Cancel();
    }
}
