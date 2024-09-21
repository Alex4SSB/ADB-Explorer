using ADB_Explorer.Helpers;
using ADB_Explorer.ViewModels;

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
    public string NoExtName
    {
        get
        {
            if (!IsRegularFile || HiddenOrWithoutExt(FullName))
                return FullName;
            else
                return FullName[..^Extension.Length];
        }
    }

    public string DisplayName => Data.Settings.ShowExtensions ? FullName : NoExtName;

    public ShellObject ShellObject { get; set; } = null;

    public bool IsHidden => FullName.StartsWith('.');

    /// <summary>
    /// Returns the extension (including the period ".").<br />
    /// Returns an empty string if file has no extension.
    /// </summary>
    public virtual string Extension
    {
        get
        {
            var lastDot = FullName.LastIndexOf('.');
            if (lastDot < 1)
                return "";

            var secondLast = FullName[..lastDot].LastIndexOf(".");

            if (secondLast > 0 && FullName[(secondLast + 1)..lastDot] == "tar")
                return FullName[secondLast..];

            return FullName[lastDot..];
        }
    }

    public FilePath(ShellObject windowsPath)
    {
        PathType = FilePathType.Windows;

        FullPath = windowsPath.ParsingName;
        FullName = windowsPath.Name;
        
        SpecialType = File.GetAttributes(FullPath).HasFlag(FileAttributes.Directory)
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

        SpecialType = fileType is FileType.Folder
            ? SpecialFileType.Folder
            : SpecialFileType.None;
    }

    public virtual void UpdatePath(string newPath)
    {
        FullPath = newPath;
        FullName = FileHelper.GetFullName(newPath);

        OnPropertyChanged(nameof(NoExtName));
    }

    private static bool HiddenOrWithoutExt(string fullName) => fullName.Count(c => c == '.') switch
    {
        0 => true,
        1 when fullName.StartsWith('.') => true,
        _ => false,
    };

    /// <summary>
    /// Returns the relation of the <paramref name="other"/> file to <see langword="this"/> file.<br />
    /// Example: File.RelationFrom(File.Parent) = Ancestor
    /// </summary>
    public RelationType RelationFrom(FilePath other) => Relation(other.FullPath);

    public RelationType Relation(string other)
    {
        if (other == FullPath)
            return RelationType.Self;

        if (other.StartsWith(FullPath) && other[FullPath.Length..(FullPath.Length + 1)] == "/")
            return RelationType.Descendant;

        if (FullPath.StartsWith(other))
            return RelationType.Ancestor;

        return RelationType.Unrelated;
    }

    public override string ToString()
    {
        return FullName;
    }
}
