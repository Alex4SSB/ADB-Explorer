using ADB_Explorer.Converters;
using ADB_Explorer.Helpers;
using ADB_Explorer.Services;

namespace ADB_Explorer.Models;

public class FileClass : FilePath, IFileStat
{
    public enum CutType
    {
        None,
        Cut,
        Copy,
        Link,
    }

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
        set => Set(ref isLink, value);
    }

    private FileType type;
    public FileType Type
    {
        get => type;
        set
        {
            if (Set(ref type, value))
                IsDirectory = value is FileType.Folder;
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

    private object icon;
    public object Icon
    {
        get => icon;
        private set => Set(ref icon, value);
    }

    private CutType cutState = CutType.None;
    public CutType CutState
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
                FullName = value.OriginalPath.Split('/')[^1];
        }
    }

    #endregion

    public bool ExtensionIsGlyph { get; set; }

    public bool ExtensionIsFontIcon { get; set; }

    public bool IsTemp { get; set; }

    public FileNameSort SortName { get; private set; }

    private string[] children = null;
    public string[] Children
    {
        get
        {
            if (children is null || !IsDirectory)
            {
                ADBService.ExecuteDeviceAdbShellCommand(Data.CurrentADBDevice.ID,
                                                        "cd",
                                                        out string stdout,
                                                        out _,
                                                        new(),
                                                        ParentPath,
                                                        "&&",
                                                        "find",
                                                        ADBService.EscapeAdbShellString(FullName),
                                                        "-type",
                                                        "f");

                children = stdout.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
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

        icon = IconHelper.GetIcon(this);
        typeName = GetTypeName();
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

        icon = IconHelper.GetIcon(this);
        typeName = GetTypeName();

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

        SortName = new(FullName);
    }

    public static string CutTypeString(CutType cutType) => cutType switch
    {
        CutType.None => "",
        CutType.Cut => "Cut",
        CutType.Copy => "Copied",
        CutType.Link => "",
        _ => throw new NotImplementedException(),
    };

    public void UpdateType()
    {
        TypeName = GetTypeName();
        Icon = IconHelper.GetIcon(this);
        IsRegularFile = Type is FileType.File;
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

        var name = ShellInfoManager.GetShellFileType(fileName);

        if (name.EndsWith("? File"))
        {
            if (ShortExtension.Length == 1)
                ExtensionIsGlyph = true;
            else
                ExtensionIsFontIcon = true;

            return $"{ShortExtension} File";
        }
        else
            return name;
    }

    public void PrepareDescriptors()
    {
        SyncFile target = new(FileHelper.ConcatPaths(Data.RuntimeSettings.TempDragPath, FullName, '\\'));
        var fileOp = FileSyncOperation.PullFile(new(this), target, Data.CurrentADBDevice, App.Current.Dispatcher);
        
        // When a folder isn't empty, there's no need creating a file descriptor for it, since all folders are automatcally created
        var items = IsDirectory && Children.Length > 0 ? Children : new[] { FullName };

        // Set directory flag only for an empty folder
        bool isDir = IsDirectory && Children.Length < 1;

        // We only know the size of a single file beforehand
        long? size = IsDirectory ? null : (long?)Size;

        Descriptors = items.Select(item => new VirtualFileDataObject.FileDescriptor()
        {
            Name = item,
            IsDirectory = isDir,
            Length = size,
            StreamContents = (stream) =>
            {
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

public class FileNameSort : IComparable
{
    public string Name { get; }

    public FileNameSort(string name)
    {
        Name = name;
    }

    public override string ToString()
    {
        return Name;
    }

    public int CompareTo(object obj)
    {
        if (obj is not FileNameSort other)
            return 0;

        return ShellInfoManager.StringCompareLogical(Name, other.Name);
    }
}
