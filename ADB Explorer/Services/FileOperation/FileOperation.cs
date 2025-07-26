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
        None,
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
        //Archive,
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

    private OperationStatus status = OperationStatus.None;
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

    private bool isValidated = false;
    public bool IsValidated
    {
        get => isValidated;
        set => Set(ref isValidated, value);
    }

    #endregion

    #region Base Properties

    public CancellationTokenSource CancelTokenSource;

    public Dispatcher Dispatcher { get; }

    public ADBService.AdbDevice Device { get; }

    public virtual FilePath FilePath { get; }

    public virtual SyncFile TargetPath { get; protected set; }

    public AdbLocation AltSource { get; protected set; } = new(Navigation.SpecialLocation.None);

    public AdbLocation AltTarget { get; protected set; } = new(Navigation.SpecialLocation.None);

    public double LastProgress = 0.0;

    public DateTime TimeStamp { get; }

    public int MasterPid { get; set; }

    #endregion

    #region Read-only Properties

    public string Time => TabularDateFormatter.Format(TimeStamp, Thread.CurrentThread.CurrentCulture);

    public FileOpFilter.FilterType Filter
    {
        get
        {
            if (IsValidated) return FileOpFilter.FilterType.Validated;
            if (IsPastOp) return FileOpFilter.FilterType.Previous;

            return Status switch
            {
                OperationStatus.InProgress => FileOpFilter.FilterType.Running,
                OperationStatus.Waiting or OperationStatus.None => FileOpFilter.FilterType.Pending,
                OperationStatus.Completed => FileOpFilter.FilterType.Completed,
                OperationStatus.Failed => FileOpFilter.FilterType.Failed,
                OperationStatus.Canceled => FileOpFilter.FilterType.Canceled,
                _ => throw new NotSupportedException(),
            };
        }
    }

    /// <summary>
    /// The type of operation and the device ID it is being performed on.
    /// </summary>
    public string TypeOnDevice => $"{OperationName}@{Device.ID}";

    public ObservableList<SyncFile> Children => AndroidPath.Children;

    public string SourcePathString
    {
        get
        {
            if (AltSource.Location is not Navigation.SpecialLocation.None)
                return AltSource.DisplayName;

            if (FilePath is null)
                return "";

            if (OperationName is OperationType.Rename)
                return FileHelper.ConcatPaths(FilePath.ParentPath, FilePath.DisplayName);
            
            // Display the original path instead of the temp folder for virtual items
            if (this is FileSyncOperation sync && sync.OriginalShellItem is not null)
            {
                var originalPath = sync.OriginalShellItem.GetDisplayName(Vanara.Windows.Shell.ShellItemDisplayString.DesktopAbsoluteEditing);
                if (originalPath is not null)
                {
                    originalPath = FileHelper.GetParentPath(originalPath);
                    if (originalPath.StartsWith("This PC"))
                        originalPath = originalPath[(originalPath.IndexOf('\\') + 1)..];

                    return originalPath;
                }
            }

            return FilePath.ParentPath;
        }
    }

    public string TargetPathString
    {
        get
        {
            if (AltTarget.Location is not Navigation.SpecialLocation.None)
                return AltTarget.DisplayName;

            if (TargetPath is null)
                return "";

            return TargetPath.ParentPath;
        }
    }

    public abstract SyncFile AndroidPath { get; }

    public virtual string Tooltip => OperationName switch
    {
        OperationType.Pull => Strings.Resources.S_PULL_ACTION,
        OperationType.Push => Strings.Resources.S_BUTTON_PUSH,
        OperationType.Move => Strings.Resources.S_ACTION_MOVE,
        OperationType.Delete => Strings.Resources.S_DELETE_ACTION,
        OperationType.Recycle => Strings.Resources.S_ACTION_RECYCLE,
        OperationType.Copy => Strings.Resources.S_MENU_COPY,
        OperationType.Restore => Strings.Resources.S_RESTORE_ACTION,
        OperationType.Install => Strings.Resources.S_MENU_INSTALL,
        OperationType.Update => Strings.Resources.S_ACTION_UPDATE,
        OperationType.Rename => Strings.Resources.S_MENU_RENAME,
        _ => throw new NotSupportedException(),
    };

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

            return StatusInfo is null 
                || (!StatusInfo.IsValidationInProgress 
                && Device.Device.Status is AbstractDevice.DeviceStatus.Ok);
        }
    }

    public bool IsSourceNavigable => FilePath?.PathType is AbstractFile.FilePathType.Windows
                || (Device.Status is AbstractDevice.DeviceStatus.Ok && AltSource.IsNoneOrNavigable);

    public bool IsTargetNavigable => TargetPath?.PathType is AbstractFile.FilePathType.Windows
                || (Device.Status is AbstractDevice.DeviceStatus.Ok && AltTarget.IsNoneOrNavigable);

    #endregion

    public BaseAction SourceAction { get; private set; }
    public BaseAction TargetAction { get; private set; }

    public FileOperation(FilePath filePath, ADBService.AdbDevice adbDevice, Dispatcher dispatcher)
    {
        TimeStamp = DateTime.Now;

        Dispatcher = dispatcher;
        Device = adbDevice;
        FilePath = filePath;

        SourceAction = new(
            () => IsSourceNavigable,
            () => OpenLocation(false));

        TargetAction = new(
            () => IsTargetNavigable,
            () => OpenLocation(true));

        Device.Device.PropertyChanged += Device_PropertyChanged;
    }

    private void Device_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Device.Status))
        {
            Data.RuntimeSettings.SortFileOps = true;
        }
    }

    private void OpenLocation(bool target)
    {
        object location;
        if (target)
            location = AltTarget.IsNavigable ? AltTarget : TargetPath;
        else
            location = AltSource.IsNavigable ? AltSource : FilePath;

        if (location is FilePath file)
        {
            if (file.PathType is AbstractFile.FilePathType.Windows)
            {
                if (this is FileSyncOperation sync && sync.OriginalShellItem is not null)
                    sync.OriginalShellItem.ViewInExplorer();
                else
                    Process.Start("explorer.exe", file.ParentPath);
            }
            else
            {
                if (!Device.Device.IsOpen)
                    Data.RuntimeSettings.DeviceToOpen = Device.Device;

                Data.RuntimeSettings.LocationToNavigate = new(file.ParentPath);
            }
        }
        else if (location is AdbLocation loc)
        {
            if (!Device.Device.IsOpen)
                Data.RuntimeSettings.DeviceToOpen = Device.Device;

            Data.RuntimeSettings.LocationToNavigate = loc;
        }
        else
            throw new NotSupportedException();
    }

    public void SetValidation(bool value)
    {
        if (StatusInfo is null)
            StatusInfo = new CompletedShellProgressViewModel();

        StatusInfo.IsValidationInProgress = value;
        OnPropertyChanged(nameof(ValidationAllowed));

        if (Data.FileActions.SelectedFileOps.Value.Contains(this))
            Data.RuntimeSettings.RefreshFileOpControls = true;
    }

    public abstract void Start();

    /// <summary>
    /// Changes the operation status from None to Waiting.
    /// </summary>
    public void BeginWaiting()
    {
        if (Status is OperationStatus.None)
            Status = OperationStatus.Waiting;
    }

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
