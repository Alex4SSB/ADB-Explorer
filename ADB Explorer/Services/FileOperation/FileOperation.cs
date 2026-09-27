using Wpf.Ui.Controls;
using static ADB_Explorer.Services.FileAction;

namespace ADB_Explorer.Services;

public abstract class FileOperation : ObservableObject
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
        Compress,
        Extract,
    }

    #region Notifiable Properties

    private OperationType _operationType;
    public OperationType OperationName
    {
        get => _operationType;
        protected set
        {
            if (SetProperty(ref _operationType, value))
            {
                OnPropertyChanged(nameof(OpIcon));
            }
        }
    }

    // Volatile so the spin-wait on the UI thread can see updates written by background pull threads
    // without going through the dispatcher (which would deadlock while Thread.Sleep is running).
    private volatile OperationStatus _status = OperationStatus.None;
    public OperationStatus Status
    {
        get => _status;
        protected set
        {
            if (_status == value) return;

            // Recorded before the volatile write below so it's safely visible to any thread that
            // observes the new Status (e.g. to know when a finished operation's grace period, for
            // things like a "just completed" snackbar, is up).
            FinishedAt = value is OperationStatus.InProgress or OperationStatus.Waiting or OperationStatus.None
                ? null
                : DateTime.UtcNow;

            _status = value; // Immediately visible to any thread reading the volatile field
            CancelTokenSource = value is OperationStatus.InProgress or OperationStatus.Waiting ? new() : null;

            App.SafeBeginInvoke(() =>
            {

                LastProgress = 0;

                OnPropertyChanged(nameof(Status));
            });
        }
    }

    /// <summary>
    /// When this operation last left the <see cref="OperationStatus.InProgress"/> state (i.e. when
    /// it completed, failed, or was canceled). <see langword="null"/> while waiting or in progress.
    /// </summary>
    public DateTime? FinishedAt { get; private set; }

    /// <summary>
    /// Whether this operation should appear in the file-operation snackbar: while actively running,
    /// or for a short grace period after it finishes.
    /// </summary>
    public bool IsSnackbarVisible(TimeSpan gracePeriod) =>
        Status is OperationStatus.InProgress
        || (FinishedAt is { } finishedAt && DateTime.UtcNow - finishedAt < gracePeriod);

    private FileOpProgressViewModel _statusInfo = new WaitingOpProgressViewModel();
    public FileOpProgressViewModel StatusInfo
    {
        get => _statusInfo;
        // BeginInvoke (fire-and-forget) so background pull threads are never blocked waiting
        // for the UI thread to process the update (which would deadlock with the spin-wait).
        set => Dispatcher.BeginInvoke(() =>
        {
            if (!ReferenceEquals(_statusInfo, value) && _statusInfo is IDisposable disposable)
                disposable.Dispose();

            SetProperty(ref _statusInfo, value);
        });
    }

    private bool _isPastOp = false;
    public bool IsPastOp
    {
        get => _isPastOp;
        set => SetProperty(ref _isPastOp, value);
    }

    private bool _isValidated = false;
    public bool IsValidated
    {
        get => _isValidated;
        set => SetProperty(ref _isValidated, value);
    }

    #endregion

    #region Base Properties

    public CancellationTokenSource? CancelTokenSource;

    public Dispatcher Dispatcher { get; }

    public LogicalDeviceViewModel Device { get; }

    private LogicalDeviceViewModel? _targetDevice;
    /// <summary>
    /// The device receiving the files when it differs from <see cref="Device"/> (device to device transfers).
    /// </summary>
    public LogicalDeviceViewModel? TargetDevice
    {
        get => _targetDevice;
        protected set
        {
            _targetDevice = value;

            if (value is not null)
                PropertyChangedEventManager.AddHandler(value, OnDeviceNameChanged, nameof(LogicalDeviceViewModel.Name));
        }
    }

    public virtual FilePath FilePath { get; }

    public virtual SyncFile TargetPath { get; protected set; } = null!;

    public AdbLocation AltSource { get; protected set; } = new(Navigation.SpecialLocation.None);

    public AdbLocation AltTarget { get; protected set; } = new(Navigation.SpecialLocation.None);

    public double LastProgress = 0.0;

    public DateTime TimeStamp { get; }

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
    public string TypeOnDevice => TargetDevice is null
        ? $"{OperationName}@{Device?.ID}"
        : $"{OperationName}@{Device?.ID}>{TargetDevice.ID}";

    /// <summary>
    /// The device the operation is performed on, or "source → target" for a device to device transfer.
    /// </summary>
    public string DeviceName => TargetDevice is null
        ? Device.Name
        : $"{Device.Name} → {TargetDevice.Name}";

    public ObservableList<SyncFile> Children => AndroidPath?.Children;

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
        OperationType.Compress => Strings.Resources.S_ACTION_COMPRESS,
        OperationType.Extract => Strings.Resources.S_ACTION_EXTRACT,
        _ => throw new NotSupportedException(),
    };

    public virtual FrameworkElement OpIcon => OperationName switch
    {
        OperationType.Pull => CreateOpIcon(new PullIcon()),
        OperationType.Push => CreateOpIcon(new PushIcon()),
        OperationType.Recycle => new RecycleIcon(),
        OperationType.Move => new FontIcon() { Glyph = "\uE8DE" },
        OperationType.Delete => CreateOpIcon(new DeleteIcon()),
        OperationType.Copy => new CopyIcon(),
        OperationType.Restore => CreateOpIcon(new RestoreIcon()),
        OperationType.Update => new FontIcon() { Glyph = AppActions.Icons[FileActionType.UpdateModified] },
        OperationType.Install => null, // gets overridden
        OperationType.Rename => new RenameAIcon(),
        OperationType.Compress => CreateOpIcon(new ZipIcon()),
        OperationType.Extract => CreateOpIcon(new FolderArrowRightIcon()),
        _ => throw new NotSupportedException(),
    };

    private const double OpIconSize = 16;

    protected static UserControl CreateOpIcon(UserControl icon)
    {
        if (icon is ScaledPathIcon scaledPathIcon)
            scaledPathIcon.Size = OpIconSize;

        icon.Width = OpIconSize;
        icon.Height = OpIconSize;
        // Transparent fill so the whole icon rect is a tooltip / hit-test target.
        icon.Background ??= Brushes.Transparent;
        return icon;
    }

    public bool ValidationAllowed
    {
        get
        {
            if (OperationName is not (OperationType.Push or OperationType.Pull or OperationType.Copy or OperationType.Extract))
                return false;

            if (Status is not OperationStatus.Completed)
                return false;

            if (StatusInfo is not null
                && (StatusInfo.IsValidationInProgress || Device.Status is not DeviceStatus.Ok))
                return false;

            // The archive is unpacked into a staging folder that is gone by the time the operation completes.
            if (this is FileTransferOperation { TargetStagingRoot: not null })
                return false;

            // Archive pull / extract-to-device: prefer cksum -HNPL (IEEE CRC), else MD5.
            if (TryGetArchiveValidationSource(out var archivePath, out _, out _))
            {
                var androidDest = TargetPath?.PathType is AbstractFile.FilePathType.Android;
                if (!ArchiveHelper.SupportsHashValidation(archivePath, Device.ID, androidDest))
                    return false;

                if (TargetDevice is null)
                    return true;

                var archiveMode = ArchiveHelper.UsesCrc32Validation(archivePath, Device.ID, androidDest)
                    ? ValidationHashMode.Crc32
                    : ValidationHashMode.Md5;

                return TargetDevice.Status is DeviceStatus.Ok && ShellCommands.SupportsHashMode(TargetDevice.ID, archiveMode);
            }

            if (TargetDevice is not null)
            {
                return TargetDevice.Status is DeviceStatus.Ok
                    && ShellCommands.GetSharedValidationHashMode(Device.ID, TargetDevice.ID) is not ValidationHashMode.None;
            }

            return ShellCommands.GetValidationHashMode(Device.ID) is not ValidationHashMode.None;
        }
    }

    /// <summary>Archive pull or copy-extract ops that can be validated against the original archive.</summary>
    public bool TryGetArchiveValidationSource(out string archivePath, out string internalPath, out bool isDirectory)
    {
        if (this is AbstractSyncFileOperation { IsArchivePull: true, ArchiveSourcePath: { } pullArchive } pullOp)
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
                || ((TargetDevice ?? Device).Status is DeviceStatus.Ok && AltTarget.IsNoneOrNavigable);

    #endregion

    public BaseAction SourceAction { get; private set; }
    public BaseAction TargetAction { get; private set; }

    public FileOperation(FilePath filePath, LogicalDeviceViewModel device, Dispatcher dispatcher)
    {
        TimeStamp = DateTime.Now;

        Dispatcher = dispatcher;
        Device = device;
        FilePath = filePath;

        if (device is not null)
            PropertyChangedEventManager.AddHandler(device, OnDeviceNameChanged, nameof(LogicalDeviceViewModel.Name));

        SourceAction = new(
            () => IsSourceNavigable && !Data.FileActions.ListingInProgress,
            () => OpenLocation(false));

        TargetAction = new(
            () => IsTargetNavigable && !Data.FileActions.ListingInProgress,
            () => OpenLocation(true));
    }

    private void OnDeviceNameChanged(object? sender, PropertyChangedEventArgs e)
        => OnPropertyChanged(nameof(DeviceName));

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
                NavigateDeviceLocation(new(file.ParentPath), target);
            }
        }
        else if (location is AdbLocation loc)
        {
            NavigateDeviceLocation(loc, target);
        }
        else
            throw new NotSupportedException();
    }

    private void NavigateDeviceLocation(AdbLocation location, bool target)
    {
        var device = target ? TargetDevice ?? Device : Device;

        if (!device.IsOpen)
            Data.DevicesObject.DeviceToOpen = device;
        else if (Data.CurrentPage.Value != typeof(Views.Pages.ExplorerPage))
            Data.CurrentPage.Value = typeof(Views.Pages.ExplorerPage);

        Data.RequestNavigation(location);
    }

    public void SetValidation(bool value)
    {
        StatusInfo ??= new CompletedShellProgressViewModel();

        StatusInfo.IsValidationInProgress = value;
    }

    public abstract void Start();

    /// <summary>Runs <paramref name="onFinished"/> once, when the operation completes, fails or is canceled.</summary>
    public void WhenFinished(Action<OperationStatus> onFinished)
    {
        void Handler(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is not nameof(Status)
                || Status is OperationStatus.None or OperationStatus.Waiting or OperationStatus.InProgress)
                return;

            PropertyChanged -= Handler;
            onFinished(Status);
        }

        PropertyChanged += Handler;
    }

    /// <summary>Runs <paramref name="onCompleted"/> once, if the operation completes successfully.</summary>
    public void WhenCompleted(Action onCompleted) => WhenFinished(status =>
    {
        if (status is OperationStatus.Completed)
            onCompleted();
    });

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

        CancelTokenSource!.Cancel();
    }
}
