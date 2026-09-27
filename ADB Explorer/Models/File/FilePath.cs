using Vanara.Windows.Shell;

namespace ADB_Explorer.Models;

public abstract class AbstractFile : ObservableObject
{
    public enum FilePathType
    {
        Android,
        Windows,
    }

    public enum RelationType
    {
        Ancestor,
        Descendant,
        Self,
        Unrelated,
    }

    public enum FileType
    {
        Socket,
        File,
        BlockDevice,
        Folder,
        CharDevice,
        FIFO,
        Unknown,
        BrokenLink,
        Gallery,
        EnterFolder,
    }

    [Flags]
    public enum SpecialFileType
    {
        Regular = 1,
        Folder = 2,
        Apk = 4,
        BrokenLink = 8,
        Unknown = 16,
        LinkOverlay = 32,
        Archive = 64,
        Gallery = 128,
        EnterFolder = 256,
    }

    public static string GetFileTypeName(FileType type) => type switch
    {
        FileType.Socket => Strings.Resources.S_FILE_SOCKET,
        FileType.File => Strings.Resources.S_MENU_FILE,
        FileType.BlockDevice => Strings.Resources.S_FILE_BLOCK,
        FileType.Folder => Strings.Resources.S_MENU_FOLDER,
        FileType.CharDevice => Strings.Resources.S_FILE_CHAR,
        FileType.FIFO => Strings.Resources.S_FILE_FIFO,
        FileType.BrokenLink => Strings.Resources.S_FILE_BROKEN_LINK,
        _ => Strings.Resources.S_FILE_UNKNOWN,
    };

    public record struct FolderTree(string Name, long? Size, double? Date)
    {
        public readonly bool IsFolder => Size is null;
    }
}

public class FilePath : AbstractFile, IBaseFile
{
    public FilePathType PathType { get; set; }

    public SpecialFileType SpecialType { get; protected set; }

    public bool IsRegularFile => SpecialType.HasFlag(SpecialFileType.Regular);

    public bool IsDirectory => SpecialType.HasFlag(SpecialFileType.Folder);

    private string _fullPath = "";
    public string FullPath
    {
        get => _fullPath;
        protected set => SetProperty(ref _fullPath, value);
    }

    public string ParentPath => FileHelper.GetParentPath(FullPath);

    private string _fullName = "";
    public string FullName
    {
        get => _fullName;
        protected set => SetProperty(ref _fullName, value);
    }
    public string NoExtName => IsRegularFile ? FullName[..^Extension.Length] : FullName;
    public bool NameIsRtl => TextHelper.ContainsRtl(NoExtName);
    public bool ExtensionIsRtl => TextHelper.ContainsRtl(Extension);
    public FlowDirection NameFlowDirection => NameIsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    public string DisplayName
    {
        get
        {
            var noExtName = NoExtName;
            return Data.Settings.ShowExtensions ? $"{noExtName}{Extension}" : noExtName;
        }
    }

    public bool IsRtlName => TextHelper.ContainsRtl(FullName);

    // Only set for FilePaths constructed from a Windows-side ShellItem; absent for Android paths.
    public ShellItem? ShellItem { get; set; }

    public bool IsHidden => FullName.StartsWith('.');

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    /// <summary>
    /// Returns the extension (including the period ".").<br />
    /// Returns an empty string if file has no extension.
    /// </summary>
    public virtual string Extension => FileHelper.GetExtension(FullName);

    public long? ShellLsSize
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
                OnShellLsSizeChanged(value);
        }
    } = null;

    protected virtual void OnShellLsSizeChanged(long? value) { }

    public void UpdateSizeFromShell(CancellationToken cancellationToken)
    {
        if (PathType is not FilePathType.Android)
            return;

        if (Data.ActiveDevice is null || !ShellCommands.StatExists(Data.ActiveDevice.ID))
            return;

        var res = AdbService.ExecuteDeviceAdbShellCommand(Data.ActiveDevice.ID,
                                                          "stat",
                                                          out string stdout,
                                                          out _,
                                                          cancellationToken,
                                                          "-c",
                                                          "%s",
                                                          AdbService.EscapeAdbShellString(FullPath));

        if (res != 0 || string.IsNullOrEmpty(stdout)
            || !long.TryParse(stdout.Trim(), out long size))
        {
            ShellLsSize = -1;
            return;
        }

        ShellLsSize = size;
    }

    public FilePath(ShellItem windowsPath)
    {
        ArgumentNullException.ThrowIfNull(windowsPath);

        ShellItem = windowsPath;
        PathType = FilePathType.Windows;

        try
        {
            FullPath = windowsPath.ParsingName ?? "";
            FullName = windowsPath.GetDisplayName(ShellItemDisplayString.ParentRelativeParsing)
                ?? FileHelper.GetFullName(FullPath);

            SpecialType = windowsPath.IsNonArchiveFolder()
                ? SpecialFileType.Folder
                : SpecialFileType.Regular;
        }
        catch
        {
            FullPath ??= windowsPath.ParsingName ?? "";
            FullName ??= FileHelper.GetFullName(FullPath);
            SpecialType = SpecialFileType.Regular;
        }
    }

    public FilePath(string androidPath,
                    string fullName = "",
                    FileType fileType = FileType.File)
    {
        PathType = FilePathType.Android;

        FullPath = androidPath;
        FullName = string.IsNullOrEmpty(fullName) ? FileHelper.GetFullName(androidPath) : fullName;

        SpecialType = fileType switch
        {
            FileType.Folder => SpecialFileType.Folder,
            FileType.Unknown => SpecialFileType.Unknown,
            FileType.BrokenLink => SpecialFileType.BrokenLink,
            FileType.Gallery => SpecialFileType.Gallery,
            FileType.EnterFolder => SpecialFileType.EnterFolder,
            _ => SpecialFileType.Regular,
        };

        if (fileType is FileType.File)
        {
            var ext = FileHelper.GetExtension(FullName).ToUpper();
            if (AdbExplorerConst.APK_NAMES.Contains(ext))
                SpecialType = SpecialFileType.Apk;

            if (AdbExplorerConst.ARCHIVE_NAMES.Contains(ext))
                SpecialType |= SpecialFileType.Archive;
        }
    }

    public virtual void UpdatePath(string newPath)
    {
        FullPath = newPath;
        FullName = FileHelper.GetFullName(newPath);

        OnPropertyChanged(nameof(NoExtName));
        OnPropertyChanged(nameof(DisplayName));
    }

    /// <summary>
    /// Returns the relation of the <paramref name="other"/> file to <see langword="this"/> file.<br />
    /// Example: File.RelationFrom(File.Parent) = Ancestor
    /// </summary>
    public RelationType RelationFrom(FilePath other) => Relation(other.FullPath);

    public RelationType Relation(string other) => FileHelper.RelationFrom(FullPath, other);

    public override string ToString()
    {
        return FullName;
    }
}
