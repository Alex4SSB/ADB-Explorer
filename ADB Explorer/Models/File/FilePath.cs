using ADB_Explorer.Helpers;
using ADB_Explorer.ViewModels;
using Vanara.Windows.Shell;

namespace ADB_Explorer.Models;

public abstract class AbstractFile : ViewModelBase
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
        Socket = 0,
        File = 1,
        BlockDevice = 2,
        Folder = 3,
        CharDevice = 4,
        FIFO = 5,
        Unknown = 6,
        BrokenLink = 7,
    }

    [Flags]
    public enum SpecialFileType
    {
        None = 1,
        Folder = 2,
        Apk = 4,
        BrokenLink = 8,
        Unknown = 16,
        LinkOverlay = 32,
    }

    private static readonly string[] names =
        { "Socket", "File", "Block Device", "Folder", "Char Device", "FIFO", "Unknown", "Broken Link" };

    public static string GetFileTypeName(FileType type) => names[(int)type];
}

public class FilePath : AbstractFile, IBaseFile
{
    public FilePathType PathType { get; set; }

    public SpecialFileType SpecialType { get; protected set; }

    public bool IsRegularFile => SpecialType.HasFlag(SpecialFileType.None);

    public bool IsDirectory => SpecialType.HasFlag(SpecialFileType.Folder);

    private string fullPath;
    public string FullPath
    {
        get => fullPath;
        protected set => Set(ref fullPath, value);
    }

    public string ParentPath => FileHelper.GetParentPath(FullPath);

    private string fullName;
    public string FullName
    {
        get => fullName;
        protected set => Set(ref fullName, value);
    }
    public string NoExtName => IsRegularFile ? FullName[..^Extension.Length] : FullName;

    public string DisplayName
    {
        get
        {
            var noExtName = NoExtName;
            // Add RTL mark to end of RTL file names with LTR extensions.
            // This prevents numbers and punctuation from breaking the RTL ordering.
            if (TextHelper.ContainsRtl(NoExtName)
                && NoExtName[^1] != TextHelper.RTL_MARK
                && !TextHelper.ContainsRtl(Extension))
            {
                noExtName = $"{NoExtName}{TextHelper.RTL_MARK}";
            }
            return Data.Settings.ShowExtensions ? $"{noExtName}{Extension}" : noExtName;
        }
    }

    public bool IsRtlName => TextHelper.ContainsRtl(FullName);

    public ShellItem ShellItem { get; set; }

    public bool IsHidden => FullName.StartsWith('.');

    /// <summary>
    /// Returns the extension (including the period ".").<br />
    /// Returns an empty string if file has no extension.
    /// </summary>
    public virtual string Extension => FileHelper.GetExtension(FullName);

    public FilePath(ShellItem windowsPath)
    {
        ShellItem = windowsPath;
        PathType = FilePathType.Windows;

        FullPath = windowsPath.ParsingName;
        FullName = windowsPath.GetDisplayName(ShellItemDisplayString.ParentRelativeParsing);

        SpecialType = windowsPath.IsFolder
            ? SpecialFileType.Folder
            : SpecialFileType.None;
    }

    public FilePath(string androidPath,
                    string fullName = "",
                    FileType fileType = FileType.File)
    {
        PathType = FilePathType.Android;

        FullPath = androidPath;
        FullName = string.IsNullOrEmpty(fullName) ? FileHelper.GetFullName(androidPath) : fullName;

        if (fileType is FileType.Folder)
            SpecialType = SpecialFileType.Folder;
        else
        {
            SpecialType = AdbExplorerConst.APK_NAMES.Contains(FileHelper.GetExtension(FullName).ToUpper())
                ? SpecialFileType.Apk
                : SpecialFileType.None;
        }

        Data.Settings.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(Data.Settings.ShowExtensions))
            {
                OnPropertyChanged(nameof(DisplayName));
            }
        };
    }

    public virtual void UpdatePath(string newPath)
    {
        FullPath = newPath;
        FullName = FileHelper.GetFullName(newPath);

        OnPropertyChanged(nameof(NoExtName));
        OnPropertyChanged(nameof(Extension));
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
