using ADB_Explorer.Converters;
using ADB_Explorer.Helpers;
using ADB_Explorer.Services;
using ADB_Explorer.Services.AppInfra;
using ADB_Explorer.ViewModels;
using Vanara.PInvoke;
using Vanara.Windows.Shell;

namespace ADB_Explorer.Models;

public partial class FileClass : FilePath, IFileStat, IBrowserItem
{
    #region Notify Properties

    public string ParsedFullPath => Data.CurrentDrive?.LinkTargetPath is null
        ? FullPath
        : FullPath.Replace(Data.CurrentDrive.Path, Data.CurrentDrive.LinkTargetPath);

    [ObservableProperty]
    public partial long? Size { get; set; }

    partial void OnSizeChanged(long? value)
    {
        _folderViewModel?.OnSizeChanged();
        _iconViewModel?.OnSizeChanged();
    }

    protected override void OnShellLsSizeChanged(long? value)
    {
        if (value > -1)
            App.SafeInvoke(() => OnSizeChanged(value));
    }

    [ObservableProperty]
    public partial UnixFileMode? Permissions { get; set; }

    [ObservableProperty]
    public partial int? OwnerUid { get; set; }

    [ObservableProperty]
    public partial int? OwnerGid { get; set; }

    [ObservableProperty]
    public partial AccessMask? ProbedAccess { get; set; }

    [ObservableProperty]
    public partial AccessMask EffectiveAccess { get; set; }

    public bool CanWriteLocation => EffectiveAccess.HasFlag(AccessMask.Write);

    private bool isLink;
    public bool IsLink
    {
        get => isLink;
        set
        {
            if (Set(ref isLink, value))
                UpdateSpecialType();
        }
    }

    [ObservableProperty]
    public partial string LinkTarget { get; set; } = "";

    [ObservableProperty]
    public partial bool IsIconPlaceholder { get; set; }
    
    private FileType type;
    public FileType Type
    {
        get => type;
        set
        {
            if (Set(ref type, value))
                UpdateSpecialType();
        }
    }

    [ObservableProperty]
    public partial string? User { get; set; }

    [ObservableProperty]
    public partial string? Group { get; set; }

    [ObservableProperty]
    public partial DateTimeOffset? LastAccessTime { get; set; }

    [ObservableProperty]
    public partial DateTimeOffset? CreationTime { get; set; }

    public DateTime? ModifiedTime
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                _folderViewModel?.OnModifiedTimeChanged();
                _iconViewModel?.OnModifiedTimeChanged();
            }
        }
    } = null;

    public DateTimeOffset? ModifiedTimeWithOffset
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                _folderViewModel?.OnModifiedTimeChanged();
                _iconViewModel?.OnModifiedTimeChanged();
            }
        }
    } = null;

    public double? UnixTime => ModifiedTime.ToUnixTime();

    private BitmapSource? _icon = null;
    public BitmapSource Icon
    {
        get
        {
            if (_icon is null)
                GetIcon();

            return _icon;
        }
    }

    [ObservableProperty]
    public partial BitmapSource? IconOverlay { get; set; } = null;

    public BitmapSource DragImage
    {
        get
        {
            return Data.Settings.ThumbsMode > AppSettings.ThumbnailMode.IconViewOnly
                ? LargeIcon
                : LargeFileIcon;
        }
    }

    public BitmapSource LargeIcon => CacheThumbnail?.Image ?? LargeFileIcon;

    private BitmapSource LargeFileIcon => FileToIconConverter.GetImage(this, 120).First();

    private ThumbnailService.Thumbnail? _cacheThumbnail = null;
    public ThumbnailService.Thumbnail? CacheThumbnail
    {
        get
        {
            if (Data.Settings.ThumbsMode is AppSettings.ThumbnailMode.Off)
                return null;

            if (_cacheThumbnail is null && Data.DevicesObject.Current?.Type 
                is DeviceType.Local 
                or DeviceType.Remote
                or DeviceType.Service)
            {
                if (!ThumbnailService.IsInitialized(Data.DevicesObject.Current.SerialNumber))
                {
                    Task.Run(() => ThumbnailService.ForceLoad(Data.DevicesObject.Current));
                }
                else
                {
                    _cacheThumbnail = ThumbnailService.LoadThumbnail(Data.DevicesObject.Current, ParsedFullPath, ThumbnailService.ThumbnailSize.Drag, false);
                }
            }
            
            return _cacheThumbnail;
        }
    }

    public BitmapSource FileIcon32 => FileToIconConverter.GetImage(this, 32).First();

    private FileIconViewModel? _iconViewModel;
    public FileIconViewModel IconViewModel 
        => _iconViewModel ??= new FileIconViewModel(this);

    public void DisposeIconViewModel()
    {
        _iconViewModel?.Dispose();
        _iconViewModel = null;
        _cacheThumbnail = null;
    }

    public void InvalidateIconViewModelThumbnail() => _iconViewModel?.InvalidateThumbnail();

    private FolderViewModel? _folderViewModel;
    public FolderViewModel FolderViewModel
        => _folderViewModel ??= new FolderViewModel(this);

    public void DisposeFolderViewModel()
    {
        _folderViewModel?.Dispose();
        _folderViewModel = null;
    }

    public FileViewModelBase ActiveViewModel => FolderViewModel.IsInEditMode 
        ? FolderViewModel 
        : IconViewModel;

    [ObservableProperty]
    public partial DragDropEffects CutState { get; set; }

    private TrashIndexer trashIndex;
    public TrashIndexer TrashIndex
    {
        get => trashIndex;
        set
        {
            Set(ref trashIndex, value);
            if (value is not null && value.OriginalPath is not null)
                FullName = FileHelper.GetFullName(value.OriginalPath);
        }
    }

    #endregion

    public bool IsTemp { get; set; }

    public FileNameSort SortName { get; private set; }

    public FolderTree[]? Children => !IsDirectory
        ? null 
        : FileHelper.GetFolderTree([FullPath], cancellationToken: Data.DeviceCts.Token);

    public IEnumerable<FileDescriptor> Descriptors { get; private set; }

    #region Read Only Properties

    public bool IsApk => AdbExplorerConst.APK_NAMES.Contains(Extension.ToUpper());

    public bool IsInstallApk => Array.IndexOf(AdbExplorerConst.INSTALL_APK, Extension.ToUpper()) > -1;

    /// <summary>
    /// Returns the extension (including the period ".") of a regular file.<br />
    /// Returns an empty string if file has no extension, or is not a regular file.
    /// </summary>
    public override string Extension => Type is FileType.File ? base.Extension : "";

    #endregion

    public FileClass(FolderTree treeItem)
        : this("",
               treeItem.Name,
               treeItem.IsFolder ? FileType.Folder : FileType.File,
               size: treeItem.Size,
               modifiedTime: treeItem.Date.FromUnixTime())
    { }

    public FileClass(
        string fileName,
        string path,
        FileType type,
        bool isLink = false,
        long? size = null,
        DateTime? modifiedTime = null,
        bool isTemp = false,
        UnixFileMode? permissions = null)
        : base(path, fileName, type)
    {
        Type = type;
        Size = size;
        ModifiedTime = modifiedTime;
        IsLink = isLink;
        IsTemp = isTemp;
        Permissions = permissions;
        
        SortName = new(FullName);

        // Use a weak event pattern to prevent Settings from rooting this object
        WeakEventManager<INotifyPropertyChanged, PropertyChangedEventArgs>.AddHandler(
            Data.Settings, nameof(Data.Settings.PropertyChanged), OnSettingsPropertyChanged);
    }

    public FileClass(FileClass other)
        : this(other.FullName, other.FullPath, other.Type, other.IsLink, other.Size, other.ModifiedTime, other.IsTemp, other.Permissions)
    {
        User = other.User;
        Group = other.Group;
        OwnerUid = other.OwnerUid;
        OwnerGid = other.OwnerGid;
        ProbedAccess = other.ProbedAccess;
        EffectiveAccess = other.EffectiveAccess;
        LastAccessTime = other.LastAccessTime;
        CreationTime = other.CreationTime;
        ModifiedTimeWithOffset = other.ModifiedTimeWithOffset;
        LinkTarget = other.LinkTarget;
    }

    public FileClass(FilePath other)
        : this(other.FullName, other.FullPath, other.IsDirectory ? FileType.Folder : FileType.File)
    { }

    public FileClass(SyncFile other)
        : this(other.FullName,
               other.FullPath,
               other.IsDirectory ? FileType.Folder : FileType.File,
               other.SpecialType.HasFlag(SpecialFileType.LinkOverlay),
               other.Size,
               other.DateModified)
    { }

    public FileClass(ShellItem windowsPath)
        : base(windowsPath)
    {
        Type = IsDirectory ? FileType.Folder : FileType.File;
        IsLink = windowsPath.IsLink;

        SortName = new(FullName);
    }

    public FileClass(FileDescriptor fileDescriptor)
        : base(fileDescriptor.SourcePath, fileDescriptor.Name, fileDescriptor.IsDirectory ? FileType.Folder : FileType.File)
    {
        Size = fileDescriptor.Length;
        ModifiedTime = fileDescriptor.ChangeTimeUtc;
        Type = fileDescriptor.IsDirectory ? FileType.Folder : FileType.File;
    }

    public static FileClass GenerateAndroidFile(FileStat fileStat) => new(
        fileName: fileStat.FullName,
        path: fileStat.FullPath,
        type: fileStat.Type,
        isLink: fileStat.IsLink,
        size: fileStat.Size,
        modifiedTime: fileStat.ModifiedTime,
        permissions: fileStat.Permissions
    );

    public static FileClass BuildCurrentLocation(
        string path,
        LocationInfo? info,
        FileClass? source,
        ShellIdentity? identity,
        DriveRestrictions restrictions)
    {
        FileClass location;
        if (source is not null && (source.FullPath == path || source.LinkTarget == path))
        {
            location = source;
        }
        else
        {
            location = new FileClass(
                FileHelper.GetFullName(path),
                path,
                FileType.Folder,
                permissions: info?.Permissions ?? source?.Permissions);
        }

        ApplyLocationInfo(location, info, identity, restrictions);
        return location;
    }

    public static void ApplyLocationInfo(
        FileClass location,
        LocationInfo? info,
        ShellIdentity? identity,
        DriveRestrictions restrictions)
    {
        if (info is null)
        {
            if (location.Permissions is UnixFileMode mode
                && location.OwnerUid is int ownerUid
                && location.OwnerGid is int ownerGid
                && identity is not null)
            {
                location.EffectiveAccess = ShellAccessHelper.ApplyRestrictions(
                    ShellAccessHelper.ResolveEffective(mode, ownerUid, ownerGid, identity),
                    restrictions);
            }

            return;
        }

        location.User = info.Value.User ?? location.User;
        location.Group = info.Value.Group ?? location.Group;
        location.OwnerUid = info.Value.OwnerUid ?? location.OwnerUid;
        location.OwnerGid = info.Value.OwnerGid ?? location.OwnerGid;
        location.Permissions = info.Value.Permissions ?? location.Permissions;
        location.ProbedAccess = info.Value.ProbedAccess;
        location.LastAccessTime = info.Value.AccessTime ?? location.LastAccessTime;
        location.CreationTime = info.Value.CreationTime ?? location.CreationTime;
        location.ModifiedTimeWithOffset = info.Value.ModifiedTime ?? location.ModifiedTimeWithOffset;
        location.ModifiedTime = info.Value.ModifiedTime?.DateTime.ToLocalTime() ?? location.ModifiedTime;
        location.EffectiveAccess = ShellAccessHelper.ResolveLocationAccess(location.FullPath, info, identity, restrictions);
    }

    private void OnSettingsPropertyChanged(object sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(Data.Settings.ShowExtensions))
        {
            OnPropertyChanged(nameof(DisplayName));
        }
    }

    public override void UpdatePath(string androidPath)
    {
        base.UpdatePath(androidPath);
        UpdateType();

        SortName = new(FullName);
    }

    public void UpdateType()
    {
        _folderViewModel?.UpdateType();

        _iconViewModel?.UpdateType();

        GetIcon();
    }

    public void UpdateSpecialType()
    {
        SpecialType = Type switch
        {
            FileType.Folder => SpecialFileType.Folder,
            FileType.Unknown => SpecialFileType.Unknown,
            FileType.BrokenLink => SpecialFileType.BrokenLink,
            FileType.MultipleFiles => SpecialFileType.MultipleFiles,
            FileType.Drive => SpecialFileType.Drive,
            FileType.EmptyTrash => SpecialFileType.EmptyTrash,
            FileType.FullTrash => SpecialFileType.FullTrash,
            FileType.Phone => SpecialFileType.Phone,
            FileType.Gallery => SpecialFileType.Gallery,
            FileType.EnterFolder => SpecialFileType.EnterFolder,
            _ => SpecialFileType.Regular
        };

        if (IsApk)
            SpecialType |= SpecialFileType.Apk;

        if (IsLink && Type is not FileType.BrokenLink)
            SpecialType |= SpecialFileType.LinkOverlay;
    }

    public void UpdateExtraInfo(CancellationToken cancellationToken)
    {
        var info = ADBService.GetFileExtraInfo(Data.DevicesObject.Current, FullPath, cancellationToken);
        if (info is null)
            return;

        App.SafeInvoke(() =>
        {
            User = info?.User;
            Group = info?.Group;
            LastAccessTime = info?.AccessTime;
            CreationTime = info?.CreationTime;
            ModifiedTimeWithOffset = info?.ModifiedTime;
        });
    }

    public SyncFile GetSyncFile() => new(this, Children);

    public FileSyncOperation PrepareDescriptors(VirtualFileDataObject vfdo, bool includeContent = true)
    {
        var name = Data.FileActions.IsAppDrive
            ? Data.SelectedPackages.FirstOrDefault(pkg => pkg.Path == FullPath)?.Name + ".apk"
            : FullName;

        SyncFile target = new(FileHelper.ConcatPaths(Data.RuntimeSettings.TempDragPath, name, '\\'))
            { PathType = FilePathType.Windows };

        var children = Children;

        var fileOp = FileSyncOperation.PullFile(new(this, children), target, Data.DevicesObject.Current, App.AppDispatcher);
        vfdo.OperationCompleted += VFDO_OperationCompleted;

        FolderTree[] items = [new(name, Size, UnixTime)];
        if (includeContent && children is not null)
        {
            items = [.. items, .. children];
        }

        Descriptors = items.Select(item => new FileDescriptor
        {
            Name = FileHelper.ExtractRelativePath(item.Name, ParentPath),
            SourcePath = FullPath,
            IsDirectory = item.IsFolder,
            Length = item.Size,
            ChangeTimeUtc = item.Date.FromUnixTime(),
            Stream = () =>
            {
                var isActive = App.AppDispatcher?.Invoke(() => App.Current?.MainWindow?.IsActive == true) == true;
                var operations = vfdo.Operations.Where(op => op.Status is FileOperation.OperationStatus.None);

                // When a VFDO that does not contain folders is sent to the clipboard, the shell immediately requests the file contents.
                // To prevent this, we refuse to give data when the app is focused.
                // When a legitimate request for data is made, the app can't be focused during the first file, but it can become focused again for the next files.
                if ((Data.CopyPaste.IsClipboard && isActive && operations.Any())
                    || !includeContent)
                {
                    return null;
                }

#if !DEPLOY
                DebugLog.PrintLine($"Total uninitiated operations: {operations.Count()}");
#endif

                // Add all uninitiated operations to the queue.
                // For all consecutive files this list will be empty.
                foreach (var op in operations)
                {
                    App.Current.Dispatcher.Invoke(() => Data.FileOpQ.AddOperation(op));
                }

                // Wait for the operation to complete.
                // When called on the UI thread (clipboard path), pump the dispatcher on each iteration
                // so that queued BeginInvoke items (StatusInfo progress updates) are processed and
                // visible in the UI during the transfer. Thread.Sleep alone would block the dispatcher
                // queue, causing all progress notifications to arrive only after the transfer finishes.
                while (fileOp.Status is FileOperation.OperationStatus.InProgress or FileOperation.OperationStatus.Waiting)
                {
                    if (App.AppDispatcher?.CheckAccess() == true)
                    {
                        // Push a short-lived nested message pump that exits at Background priority
                        // (i.e., after all pending Normal-priority BeginInvoke items have run).
                        var frame = new DispatcherFrame();
                        App.AppDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
                        Dispatcher.PushFrame(frame);
                        Thread.Sleep(16); // brief yield (~60 fps) before next pump to avoid busy-spinning
                    }
                    else
                    {
                        Thread.Sleep(100);
                    }
                }

                var file = FileHelper.ConcatPaths(Data.RuntimeSettings.TempDragPath, FileHelper.ExtractRelativePath(item.Name, ParentPath), '\\');

                // Try 10 times to read from the file and write to the stream,
                // in case the file is still in use by ADB or hasn't appeared yet
                for (int i = 0; i < 10; i++)
                {
                    try
                    {
                        var stream = NativeMethods.GetComStreamFromFile(file);

                        if (stream is not null)
                            return stream;
                    }
                    catch (Exception e)
                    {
#if !DEPLOY
                        DebugLog.PrintLine($"Failed to open stream on {file}: {e.Message}");
#endif

                        Thread.Sleep(100);
                        continue;
                    }
                }

                return null;
            }
        });

        return fileOp;
    }

    private void VFDO_OperationCompleted(object sender, NativeMethods.HResult hResult)
    {
        if (sender is not VirtualFileDataObject vfdo)
            return;

        if (hResult is NativeMethods.HResult.Ok)
        {
            if (vfdo.CurrentEffect.HasFlag(DragDropEffects.Move))
            {
                var device = vfdo.Operations.First().Device;

                // Delete file from device
                ShellFileOperation.SilentDelete(device, FullPath);

                // Remove file in UI if present
                if (device.ID == Data.DevicesObject.Current.ID
                    && ParentPath == Data.CurrentPath)
                {
                    App.SafeInvoke(() =>
                    {
                        Data.DirList.FileList.RemoveAll(f => f.FullPath == FullPath);
                        FileActionLogic.UpdateFileActions();
                    });
                }

                if (vfdo.Operations.All(op => op.Status is FileOperation.OperationStatus.Completed))
                    Data.CopyPaste.Clear();
            }
        }

        vfdo.OperationCompleted -= VFDO_OperationCompleted!;
    }

    public void GetIcon()
    {
        var icons = FileToIconConverter.GetImage(this, 16).ToArray();

        if (icons.Length > 0 && icons[0] is BitmapSource icon)
            _icon = icon;
        else
            _icon = null;

        OnPropertyChanged(nameof(Icon));

        if (icons.Length > 1 && icons[1] is BitmapSource icon2)
            IconOverlay = icon2;
        else
            IconOverlay = null;
    }

    public override string ToString()
    {
        var timeStr = TabularDateFormatter.Format(ModifiedTime, Thread.CurrentThread.CurrentCulture);
        var sizeStr = IsDirectory ? "" : Size?.BytesToSize(true);
        var typeStr = _folderViewModel?.TypeName ?? $"{Type}";

        if (TrashIndex is null)
        {
            return $"{DisplayName} \n{timeStr} \n{typeStr} \n{sizeStr}";
        }
        else
        {
            return $"{DisplayName} \n{TrashIndex.OriginalPath} \n{TrashIndex.ModifiedTimeString} \n{typeStr} \n{sizeStr} \n{timeStr}";
        }
    }

    protected override bool Set<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
    {
        if (propertyName is nameof(FullName) or nameof(Type) or nameof(IsLink))
        {
            UpdateType();
        }

        return base.Set(ref storage, value, propertyName);
    }

    public static explicit operator SyncFile(FileClass self)
        => new(self.FullPath, self.Type);
}

public class FileNameSort(string name) : IComparable
{
    public string Name { get; } = name;

    public override string ToString()
    {
        return Name;
    }

    public int CompareTo(object obj)
    {
        if (obj is not FileNameSort other)
            return 0;

        return NativeMethods.StringCompareLogical(Name, other.Name);
    }
}
