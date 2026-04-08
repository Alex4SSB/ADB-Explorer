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

    [ObservableProperty]
    private long? _size;

    partial void OnSizeChanged(long? value)
    {
        _folderViewModel?.OnSizeChanged();
        _iconViewModel?.OnSizeChanged();
    }

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
    private string _linkTarget = "";

    private bool _isIconPlaceholder;
    public bool IsIconPlaceholder
    {
        get => _isIconPlaceholder;
        set => Set(ref _isIconPlaceholder, value);
    }

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

    private DateTime? modifiedTime;
    public DateTime? ModifiedTime
    {
        get => modifiedTime;
        set
        {
            if (Set(ref modifiedTime, value))
            {
                _folderViewModel?.OnModifiedTimeChanged();
                _iconViewModel?.OnModifiedTimeChanged();
            }
        }
    }

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
    private BitmapSource? _iconOverlay = null;

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
    private ThumbnailService.Thumbnail? CacheThumbnail
    {
        get
        {
            if (_cacheThumbnail is null && Data.DevicesObject.Current.Type 
                is DeviceType.Local 
                or DeviceType.Remote)
            {
                if (!ThumbnailService.IsInitialized(Data.DevicesObject.Current.LogicalID))
                {
                    Task.Run(() => ThumbnailService.ForceLoad(Data.DevicesObject.Current));
                }
                else
                {
                    _cacheThumbnail = ThumbnailService.LoadThumbnail(Data.DevicesObject.Current, FullPath, ThumbnailService.ThumbnailSize.Drag, false);
                }
            }

            return _cacheThumbnail;
        }
    }

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

    private DragDropEffects cutState = DragDropEffects.None;
    public DragDropEffects CutState
    {
        get => cutState;
        set => Set(ref cutState, value);
    }

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
        : FileHelper.GetFolderTree([FullPath]);

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
        bool isTemp = false)
        : base(path, fileName, type)
    {
        Type = type;
        Size = size;
        ModifiedTime = modifiedTime;
        IsLink = isLink;
        IsTemp = isTemp;
        
        SortName = new(FullName);

        // Use a weak event pattern to prevent Settings from rooting this object
        WeakEventManager<INotifyPropertyChanged, PropertyChangedEventArgs>.AddHandler(
            Data.Settings, nameof(Data.Settings.PropertyChanged), OnSettingsPropertyChanged);
    }

    public FileClass(FileClass other)
        : this(other.FullName, other.FullPath, other.Type, other.IsLink, other.Size, other.ModifiedTime, other.IsTemp)
    { }

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
        Size = IsDirectory ? null : windowsPath.FileInfo.Length;
        ModifiedTime = windowsPath.FileInfo?.LastWriteTime;
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
        size: fileStat.Size,
        modifiedTime: fileStat.ModifiedTime,
        isLink: fileStat.IsLink
    );

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
            _ => SpecialFileType.Regular
        };

        if (IsApk)
            SpecialType |= SpecialFileType.Apk;

        if (IsLink && Type is not FileType.BrokenLink)
            SpecialType |= SpecialFileType.LinkOverlay;
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
        fileOp.PropertyChanged += PullOperation_PropertyChanged;
        fileOp.VFDO = vfdo;

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
                    return null;

#if !DEPLOY
                DebugLog.PrintLine($"Total uninitiated operations: {operations.Count()}");
#endif

                // Add all uninitiated operations to the queue.
                // For all consecutive files this list will be empty.
                foreach (var op in operations)
                {
                    App.SafeInvoke(() => Data.FileOpQ.AddOperation(op));
                }

                // Wait for the operation to complete
                while (fileOp.Status is not FileOperation.OperationStatus.Completed)
                {
                    Thread.Sleep(100);
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

    private static void PullOperation_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        var op = sender as FileSyncOperation;

        if (e.PropertyName != nameof(FileOperation.Status)
            || op.Status is FileOperation.OperationStatus.Waiting
            or FileOperation.OperationStatus.InProgress)
            return;

        if (op.Status is FileOperation.OperationStatus.Completed)
        {
            if (op.VFDO.CurrentEffect.HasFlag(DragDropEffects.Move))
            {
                // Delete file from device
                ShellFileOperation.SilentDelete(op.Device, op.FilePath.FullPath);

                // Remove file in UI if present
                if (op.Device.ID == Data.DevicesObject.Current.ID
                    && op.FilePath.ParentPath == Data.CurrentPath)
                {
                    op.Dispatcher.Invoke(() =>
                    {
                        Data.DirList.FileList.RemoveAll(f => f.FullPath == op.FilePath.FullPath);
                        FileActionLogic.UpdateFileActions();
                    });
                }

                if (op.VFDO.Operations.All(op => op.Status is FileOperation.OperationStatus.Completed))
                    Data.CopyPaste.Clear();
            }

            op.VFDO = null;
        }

        op.PropertyChanged -= PullOperation_PropertyChanged;
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
