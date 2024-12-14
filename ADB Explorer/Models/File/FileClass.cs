using ADB_Explorer.Converters;
using ADB_Explorer.Helpers;
using ADB_Explorer.Services;
using ADB_Explorer.Services.AppInfra;

namespace ADB_Explorer.Models;

public class FileClass : FilePath, IFileStat
{
    #region Notify Properties

    private ulong? size;
    public ulong? Size
    {
        get => size;
        set => Set(ref size, value);
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

    private string linkTarget = "";
    public string LinkTarget
    {
        get => linkTarget;
        set => Set(ref linkTarget, value);
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

    private string typeName;
    public string TypeName
    {
        get => typeName;
        private set => Set(ref typeName, value);
    }

    private DateTime? modifiedTime;
    public DateTime? ModifiedTime
    {
        get => modifiedTime;
        set
        {
            if (Set(ref modifiedTime, value))
                OnPropertyChanged(nameof(ModifiedTimeString));
        }
    }

    private BitmapSource icon = null;
    public BitmapSource Icon
    {
        get => icon;
        private set => Set(ref icon, value);
    }

    private BitmapSource iconOverlay = null;
    public BitmapSource IconOverlay
    {
        get => iconOverlay;
        private set => Set(ref iconOverlay, value);
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
            if (value is not null)
                FullName = FileHelper.GetFullName(value.OriginalPath);
        }
    }

    #endregion

    private bool extensionIsGlyph = false;
    public bool ExtensionIsGlyph
    {
        get => extensionIsGlyph;
        set => Set(ref extensionIsGlyph, value);
    }

    private bool extensionIsFontIcon = false;
    public bool ExtensionIsFontIcon
    {
        get => extensionIsFontIcon;
        set => Set(ref extensionIsFontIcon, value);
    }

    public bool IsTemp { get; set; }

    public FileNameSort SortName { get; private set; }

    private string[] children = null;
    public string[] Children
    {
        get
        {
            if (children is null || !IsDirectory)
            {
                var findCmd = "find";
                if (Enum.TryParse<ShellCommands.ShellCmd>(findCmd, out var enumCmd)
                    && ShellCommands.DeviceCommands.TryGetValue(Data.CurrentADBDevice.ID, out var dict)
                    && dict.TryGetValue(enumCmd, out var deviceCmd))
                {
                    findCmd = deviceCmd;
                }

                var target = ADBService.EscapeAdbShellString(FullName);
                string[] args = [ ParentPath, "&&", findCmd, target, "-type f", "&&", findCmd, target, "-type d -empty -printf '%p/\\n'"];
                
                ADBService.ExecuteDeviceAdbShellCommand(Data.CurrentADBDevice.ID, "cd", out string stdout, out _, new(), args);

                children = stdout.Split(ADBService.LINE_SEPARATORS, StringSplitOptions.RemoveEmptyEntries);
            }

            return children;
        }
    }

    public IEnumerable<VirtualFileDataObject.FileDescriptor> Descriptors { get; private set; } = null;

    #region Read Only Properties

    public bool IsApk => AdbExplorerConst.APK_NAMES.Contains(Extension.ToUpper());

    public bool IsInstallApk => Array.IndexOf(AdbExplorerConst.INSTALL_APK, Extension.ToUpper()) > -1;

    /// <summary>
    /// Returns the extension (including the period ".") of a regular file.<br />
    /// Returns an empty string if file has no extension, or is not a regular file.
    /// </summary>
    public override string Extension => Type is FileType.File ? base.Extension : "";

    public string ShortExtension
    {
        get
        {
            return (Extension.Length > 1 && Array.IndexOf(AdbExplorerConst.UNICODE_ICONS, char.GetUnicodeCategory(Extension[1])) > -1)
                ? Extension[1..]
                : "";
        }
    }

    public string ModifiedTimeString => ModifiedTime?.ToString(CultureInfo.CurrentCulture.DateTimeFormat);

    public string SizeString => Size?.ToSize();

    #endregion

    public FileClass(
        string fileName,
        string path,
        FileType type,
        bool isLink = false,
        UInt64? size = null,
        DateTime? modifiedTime = null,
        bool isTemp = false)
        : base(path, fileName, type)
    {
        Type = type;
        Size = size;
        ModifiedTime = modifiedTime;
        IsLink = isLink;

        GetIcon();
        TypeName = GetTypeName();
        IsTemp = isTemp;
        
        SortName = new(fileName);
    }

    public FileClass(FileClass other)
        : this(other.FullName, other.FullPath, other.Type, other.IsLink, other.Size, other.ModifiedTime, other.IsTemp)
    { }

    public FileClass(FilePath other)
        : this(other.FullName, other.FullPath, other.IsDirectory ? FileType.Folder : FileType.File)
    { }

    public FileClass(ShellObject windowsPath)
        : base(windowsPath)
    {
        Type = IsDirectory ? FileType.Folder : FileType.File;
        Size = windowsPath.GetSize();
        ModifiedTime = windowsPath.GetDateModified();
        IsLink = windowsPath.IsLink;

        GetIcon();
        TypeName = GetTypeName();

        SortName = new(FullName);
    }

    public static FileClass GenerateAndroidFile(FileStat fileStat) => new FileClass
    (
        fileName: fileStat.FullName,
        path: fileStat.FullPath,
        type: fileStat.Type,
        size: fileStat.Size,
        modifiedTime: fileStat.ModifiedTime,
        isLink: fileStat.IsLink
    );

    public static FileClass FromWindowsPath(FilePath androidTargetPath, ShellObject windowsPath) =>
        new(androidTargetPath)
    {
        Size = windowsPath.GetSize(),
        ModifiedTime = windowsPath.GetDateModified(),
    };

    public override void UpdatePath(string androidPath)
    {
        base.UpdatePath(androidPath);
        UpdateType();

        SortName = new(FullName);
    }

    public static string CutTypeString(DragDropEffects cutType) => cutType switch
    {
        DragDropEffects.None => "",
        DragDropEffects.Move => "Cut",
        DragDropEffects.Copy => "Copied",
        DragDropEffects.Link => "",
        _ => throw new NotImplementedException(),
    };

    public void UpdateType()
    {
        TypeName = GetTypeName();
        GetIcon();
    }

    public void UpdateSpecialType()
    {
        if (Type is FileType.Folder)
            SpecialType = SpecialFileType.Folder;
        else if (Type is FileType.Unknown)
            SpecialType = SpecialFileType.Unknown;
        else if (Type is FileType.BrokenLink)
            SpecialType = SpecialFileType.BrokenLink;
        else
            SpecialType = SpecialFileType.None; // Regular file or some /dev

        if (IsApk)
            SpecialType |= SpecialFileType.Apk;

        if (IsLink && Type is not FileType.BrokenLink)
            SpecialType |= SpecialFileType.LinkOverlay;
    }

    private string GetTypeName()
    {
        var type = Type switch
        {
            FileType.File => GetTypeName(FullName),
            FileType.Folder => "Folder",
            FileType.Unknown => "",
            _ => GetFileTypeName(Type),
        };

        if (IsLink && Type is not FileType.BrokenLink)
            type = string.IsNullOrEmpty(type) ? "Link" : $"{type} (Link)";

        return type;
    }

    public string GetTypeName(string fileName)
    {
        if (IsApk)
            return "Android Application Package";

        if (string.IsNullOrEmpty(fileName) || (IsHidden && FullName.Count(c => c == '.') == 1))
            return "File";

        if (Extension.ToLower() == ".exe")
            return "Windows Executable";

        var name = NativeMethods.GetShellFileType(fileName);

        if (name.EndsWith("? File"))
        {
            if (ShortExtension.Length == 1)
                ExtensionIsGlyph = true;
            else
                ExtensionIsFontIcon = true;

            return $"{ShortExtension} File";
        }
        else
        {
            ExtensionIsGlyph =
            ExtensionIsFontIcon = false;

            return name;
        }
    }

    public FileSyncOperation PrepareDescriptors(VirtualFileDataObject vfdo)
    {
        SyncFile target = new(FileHelper.ConcatPaths(Data.RuntimeSettings.TempDragPath, FullName, '\\'))
            { PathType = FilePathType.Windows };

        var fileOp = FileSyncOperation.PullFile(new(this), target, Data.CurrentADBDevice, App.Current.Dispatcher);
        fileOp.PropertyChanged += PullOperation_PropertyChanged;
        fileOp.VFDO = vfdo;

        var items = Children ?? [FullName + (IsDirectory ? '/' : "")];

        // We only know the size of a single file beforehand
        long? size = IsDirectory ? null : (long?)Size;

        Descriptors = items.Select(item => new VirtualFileDataObject.FileDescriptor
        {
            Name = item.TrimEnd('/'),
            SourcePath = FullPath,
            IsDirectory = item[^1] is '/',
            Length = size,
            StreamContents = (stream) =>
            {
                if (DateTime.Now - fileOp.TimeStamp < TimeSpan.FromSeconds(2))
                    return;

                // Start the operation if it hasn't been started yet
                if (fileOp.Status is FileOperation.OperationStatus.Waiting)
                    App.Current.Dispatcher.Invoke(() => Data.FileOpQ.AddOperation(fileOp));

                // Wait for the operation to complete
                while (fileOp.Status is not FileOperation.OperationStatus.Completed)
                {
                    Thread.Sleep(100);
                }
                
                var file = FileHelper.ConcatPaths(Data.RuntimeSettings.TempDragPath, item, '\\');

                // Try 10 times to read from the file and write to the stream,
                // in case the file is still in use by ADB or hasn't appeared yet
                for (int i = 0; i < 10; i++)
                {
                    try
                    {
                        stream.Write(File.ReadAllBytes(file));
                        break;
                    }
                    catch (Exception)
                    {
                        Thread.Sleep(100);
                        continue;
                    }
                }
            }
        });

        return fileOp;
    }

    private void PullOperation_PropertyChanged(object sender, PropertyChangedEventArgs e)
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
                if (op.Device.ID == Data.CurrentADBDevice.ID
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
        }

        op.PropertyChanged -= PullOperation_PropertyChanged;
    }

    public void GetIcon()
    {
        var icons = FileToIconConverter.GetImage(this, true).ToArray();
        
        if (icons.Length > 0 && icons[0] is BitmapSource icon)
            Icon = icon;
        else
            Icon = null;

        if (icons.Length > 1 && icons[1] is BitmapSource icon2)
            IconOverlay = icon2;
        else
            IconOverlay = null;
    }

    public override string ToString()
    {
        if (TrashIndex is null)
        {
            return $"{DisplayName} \n{ModifiedTimeString} \n{TypeName} \n{SizeString}";
        }
        else
        {
            return $"{DisplayName} \n{TrashIndex.OriginalPath} \n{TrashIndex.ModifiedTimeString} \n{TypeName} \n{SizeString} \n{ModifiedTimeString}";
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
