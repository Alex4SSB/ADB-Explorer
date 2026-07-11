using ADB_Explorer.Controls;
using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.ViewModels;
using Wpf.Ui.Controls;
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

    // Volatile so the spin-wait on the UI thread can see updates written by background pull threads
    // without going through the dispatcher (which would deadlock while Thread.Sleep is running).
    private volatile OperationStatus status = OperationStatus.None;
    public OperationStatus Status
    {
        get => status;
        protected set
        {
            if (status == value) return;
            status = value; // Immediately visible to any thread reading the volatile field
            CancelTokenSource = value is OperationStatus.InProgress or OperationStatus.Waiting ? new() : null;

            App.SafeBeginInvoke(() =>
            {
                OnPropertyChanged(nameof(ValidationAllowed));

                LastProgress = 0;

                OnPropertyChanged(nameof(Status));
            });
        }
    }

    private FileOpProgressViewModel statusInfo = new WaitingOpProgressViewModel();
    public FileOpProgressViewModel StatusInfo
    {
        get => statusInfo;
        // BeginInvoke (fire-and-forget) so background pull threads are never blocked waiting
        // for the UI thread to process the update (which would deadlock with the spin-wait).
        set => Dispatcher.BeginInvoke(() => Set(ref statusInfo, value));
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

    public LogicalDeviceViewModel Device { get; }

    public virtual FilePath FilePath { get; }

    public virtual SyncFile TargetPath { get; protected set; }

    public AdbLocation AltSource { get; protected set; } = new(Navigation.SpecialLocation.None);

    public AdbLocation AltTarget { get; protected set; } = new(Navigation.SpecialLocation.None);

    public double LastProgress = 0.0;

    public DateTime TimeStamp { get; }

    public int MasterPid { get; set; }

    #endregion

    #region Read-only Properties

    public string Time => TabularDateFormatter.Format(TimeOnly.FromDateTime(TimeStamp), Thread.CurrentThread.CurrentCulture);

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
            // Path-based AltSource (e.g. archive member) has Location=None; still prefer it over FilePath (staging).
            if (AltSource.Location is not Navigation.SpecialLocation.None || !string.IsNullOrEmpty(AltSource.Path))
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
            if (AltTarget.Location is not Navigation.SpecialLocation.None || !string.IsNullOrEmpty(AltTarget.Path))
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

            if (StatusInfo is not null
                && (StatusInfo.IsValidationInProgress || Device.Status is not DeviceStatus.Ok))
                return false;

            // Archive pull / extract-to-device: prefer cksum -HNPL (IEEE CRC), else MD5.
            if (TryGetArchiveValidationSource(out var archivePath, out _, out _))
            {
                var androidDest = TargetPath?.PathType is AbstractFile.FilePathType.Android;
                return ArchiveHelper.SupportsHashValidation(archivePath, Device.ID, androidDest);
            }

            return ShellCommands.GetValidationHashMode(Device.ID) is not ValidationHashMode.None;
        }
    }

    /// <summary>Archive pull or copy-extract ops that can be validated against the original archive.</summary>
    public bool TryGetArchiveValidationSource(out string archivePath, out string internalPath, out bool isDirectory)
    {
        if (this is FileSyncOperation { IsArchivePull: true, ArchiveSourcePath: { } pullArchive } pullOp)
        {
            archivePath = pullArchive;
            internalPath = pullOp.ArchiveInternalPath ?? "";
            isDirectory = pullOp.FilePath.IsDirectory;
            return true;
        }

        if (this is FileExtractOperation extractOp)
        {
            archivePath = extractOp.ArchiveSourcePath;
            internalPath = extractOp.ArchiveInternalPath;
            isDirectory = extractOp.IsArchiveDirectory;
            return true;
        }

        archivePath = "";
        internalPath = "";
        isDirectory = false;
        return false;
    }

    public bool IsSourceNavigable => FilePath?.PathType is AbstractFile.FilePathType.Windows
                || (Device.Status is DeviceStatus.Ok && AltSource.IsNoneOrNavigable);

    public bool IsTargetNavigable => TargetPath?.PathType is AbstractFile.FilePathType.Windows
                || (Device.Status is DeviceStatus.Ok && AltTarget.IsNoneOrNavigable);

    #endregion

    public BaseAction SourceAction { get; private set; }
    public BaseAction TargetAction { get; private set; }

    public FileOperation(FilePath filePath, LogicalDeviceViewModel device, Dispatcher dispatcher)
    {
        TimeStamp = DateTime.Now;

        Dispatcher = dispatcher;
        Device = device;
        FilePath = filePath;

        SourceAction = new(
            () => IsSourceNavigable && !Data.FileActions.ListingInProgress,
            () => OpenLocation(false));

        TargetAction = new(
            () => IsTargetNavigable && !Data.FileActions.ListingInProgress,
            () => OpenLocation(true));
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
                NavigateDeviceLocation(new(file.ParentPath));
            }
        }
        else if (location is AdbLocation loc)
        {
            NavigateDeviceLocation(loc);
        }
        else
            throw new NotSupportedException();
    }

    private void NavigateDeviceLocation(AdbLocation location)
    {
        if (!Device.IsOpen)
            Data.DevicesObject.DeviceToOpen = Device;
        else if (Data.CurrentPage.Value != typeof(Views.Pages.ExplorerPage))
            Data.CurrentPage.Value = typeof(Views.Pages.ExplorerPage);

        Data.RuntimeSettings.LocationToNavigate = location;
    }

    public void SetValidation(bool value)
    {
        StatusInfo ??= new CompletedShellProgressViewModel();

        StatusInfo.IsValidationInProgress = value;
        OnPropertyChanged(nameof(ValidationAllowed));
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
