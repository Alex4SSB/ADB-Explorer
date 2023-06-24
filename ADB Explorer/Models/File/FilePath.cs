using ADB_Explorer.ViewModels;
using static ADB_Explorer.Services.ADBService;

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
    }

    private static readonly string[] names =
        { "Socket", "File", "Block Device", "Folder", "Char Device", "FIFO", "Unknown" };
    public static string GetFileTypeName(FileType type) => names[(int)type];
}

public class FilePath : AbstractFile
{
    public FilePathType PathType { get; protected set; }

    protected bool IsRegularFile { get; set; }
    public bool IsDirectory { get; protected set; }

    private string fullPath;
    public string FullPath
    {
        get => fullPath;
        protected set => Set(ref fullPath, value);
    }

    public string ParentPath => FullPath[..LastSeparatorIndex(FullPath, PathSeparator())];

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
            if (IsDirectory || !IsRegularFile || HiddenOrWithoutExt(FullName))
                return FullName;
            else
                return FullName[..FullName.LastIndexOf('.')];
        }
    }

    public string DisplayName => Data.Settings.ShowExtensions ? FullName : NoExtName;
    
    public FilePath(ShellObject windowsPath)
    {
        PathType = FilePathType.Windows;

        FullPath = windowsPath.ParsingName;
        FullName = windowsPath.Name;
        IsDirectory = windowsPath is ShellFolder;
        IsRegularFile = !IsDirectory;
    }

    public FilePath(string androidPath,
                    string fullName = "",
                    FileType fileType = FileType.File)
    {
        PathType = FilePathType.Android;

        FullPath = androidPath;
        FullName = string.IsNullOrEmpty(fullName) ? GetFullName(androidPath) : fullName;
        IsDirectory = fileType == FileType.Folder;
        IsRegularFile = fileType == FileType.File;
    }

    public virtual void UpdatePath(string androidPath)
    {
        FullPath = androidPath;
        FullName = GetFullName(androidPath);
    }

    private string GetFullName(string fullPath) =>
        fullPath[(fullPath.LastIndexOf(PathSeparator()) + 1)..];

    private static bool HiddenOrWithoutExt(string fullName) => fullName.Count(c => c == '.') switch
    {
        0 => true,
        1 when fullName.StartsWith('.') => true,
        _ => false,
    };

    private char PathSeparator() => PathSeparator(PathType);

    private static char PathSeparator(FilePathType pathType) => pathType switch
    {
        FilePathType.Windows => '\\',
        FilePathType.Android => '/',
        _ => throw new NotSupportedException(),
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

    public static Index LastSeparatorIndex(string path, char separator = '/')
        => IndexAdjust(path.LastIndexOf(separator));

    protected static Index IndexAdjust(int originalIndex) => originalIndex switch
    {
        0 => 1,
        < 0 => ^0,
        _ => originalIndex,
    };

    public override string ToString()
    {
        return FullName;
    }
}
