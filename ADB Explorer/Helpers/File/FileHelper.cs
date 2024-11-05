using ADB_Explorer.Converters;
using ADB_Explorer.Models;
using ADB_Explorer.Resources;
using ADB_Explorer.Services;
using static ADB_Explorer.Models.AbstractFile;

namespace ADB_Explorer.Helpers;

public static class FileHelper
{
    public static FileClass ListerFileManipulator(FileClass item)
    {
        if (Data.CopyPaste.Files.Length > 0
            && Data.CopyPaste.IsSelfClipboard
            && Data.CopyPaste.ParentFolder == Data.DirList.CurrentPath
            && Data.CopyPaste.Files.FirstOrDefault(f => f == item.FullPath) is not null)
        {
            item.CutState = Data.CopyPaste.PasteState;
        }

        if (Data.FileActions.IsRecycleBin)
        {
            var query = Data.RecycleIndex.Where(index => index.RecycleName == item.FullName);
            if (query.Any())
            {
                item.TrashIndex = query.First();
                item.UpdateType();
            }
        }

        return item;
    }

    public static Predicate<object> HideFiles() => file =>
    {
        if (file is not FileClass fileClass)
            return false;

        if (fileClass.IsHidden)
            return false;

        return !IsHiddenRecycleItem(fileClass);
    };

    public static Predicate<object> PkgFilter() => pkg =>
    {
        if (pkg is not Package)
            return false;
        
        return string.IsNullOrEmpty(Data.FileActions.ExplorerFilter)
            || pkg.ToString().Contains(Data.FileActions.ExplorerFilter, StringComparison.OrdinalIgnoreCase);
    };

    public static bool IsHiddenRecycleItem(FileClass file)
    {
        if (AdbExplorerConst.POSSIBLE_RECYCLE_PATHS.Contains(file.FullPath) || file.Extension == AdbExplorerConst.RECYCLE_INDEX_SUFFIX)
            return true;
        
        if (!string.IsNullOrEmpty(Data.FileActions.ExplorerFilter) && !file.ToString().Contains(Data.FileActions.ExplorerFilter, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    public static void RenameFile(FileClass file, string newName)
    {
        var newPath = ConcatPaths(file.ParentPath, newName);
        if (!Data.Settings.ShowExtensions)
            newPath += file.Extension;

        ShellFileOperation.Rename(file, newPath, Data.CurrentADBDevice);
    }

    public static string DisplayName(TextBox textBox) => DisplayName(textBox.DataContext as FilePath);

    public static string DisplayName(FilePath file) => Data.Settings.ShowExtensions ? file?.FullName : file?.NoExtName;

    public static FileClass GetFromCell(DataGridCellInfo cell) => CellConverter.GetDataGridCell(cell).DataContext as FileClass;

    public static string ConcatPaths(FilePath path1, string path2) => 
        ConcatPaths(path1.FullPath, path2, path1.PathType is FilePathType.Android ? '/' : '\\');

    public static string ConcatPaths(string path1, string path2, char separator = '/')
    {
        return $"{path1.TrimEnd(separator)}{separator}{path2.TrimStart(separator)}";
    }

    public static string ExtractRelativePath(string fullPath, string parent)
    {
        if (fullPath == parent)
            return GetFullName(fullPath);

        var index = fullPath.IndexOf(parent);
        
        var result = index < 0
            ? fullPath
            : fullPath[parent.Length..];

        return result.TrimStart('/', '\\');
    }

    public static string GetParentPath(string fullPath) =>
        fullPath[..LastSeparatorIndex(fullPath)];

    public static string GetFullName(string fullPath)
    {
        var separator = GetSeparator(fullPath);
        if (fullPath.IndexOf(separator) == fullPath.Length - 1)
            return fullPath;

        fullPath = fullPath.TrimEnd(separator);
        var index = LastSeparatorIndex(fullPath);

        if (index.IsFromEnd)
            return fullPath;

        return fullPath[(index.Value + 1)..];
    }

    public static char GetSeparator(string path)
    {
        if (path.Contains('/'))
            return '/';
        else if (path.Contains('\\'))
            return '\\';
        else
            return '\0';
    }

    public static Index LastSeparatorIndex(string path)
        => IndexAdjust(path.LastIndexOf(GetSeparator(path)));

    public static Index NextSeparatorIndex(string parentPath, string childPath)
        => IndexAdjust(childPath.IndexOf(GetSeparator(childPath), parentPath.Length + 1));

    public static Index IndexAdjust(int originalIndex) => originalIndex switch
    {
        0 => 1,
        < 0 => ^0,
        _ => originalIndex,
    };

    public static ulong? GetSize(this ShellObject shellObject)
        => shellObject.Properties.System.Size.Value;

    public static DateTime? GetDateModified(this ShellObject shellObject)
        => shellObject.Properties.System.DateModified.Value;

    public static ulong TotalSize(IEnumerable<FileClass> files)
    {
        if (files.Any(f => f.Type is not FileType.File || f.IsLink))
            return 0;

        return (ulong)files.Select(f => (decimal)f.Size.GetValueOrDefault(0)).Sum();
    }

    /// <summary>
    /// Returns the extension (including the period ".").<br />
    /// Returns an empty string if file has no extension.
    /// </summary>
    public static string GetExtension(string fullName)
    {
        var lastDot = fullName.LastIndexOf('.');
        if (lastDot < 1)
            return "";

        var secondLast = fullName[..lastDot].LastIndexOf('.');

        if (secondLast > 0 && fullName[(secondLast + 1)..lastDot] == "tar")
            return fullName[secondLast..];

        return fullName[lastDot..];
    }

    public static string DuplicateFile(ObservableList<FileClass> fileList, string fullName, CutType cutType = CutType.None)
        => DuplicateFile(fileList.Select(f => f.FullName), fullName, cutType);

    public static string DuplicateFile(IEnumerable<string> fileList, string fullName, CutType cutType = CutType.None)
    {
        // None - new file
        var copySuffix = cutType is CutType.None ? "" : " - Copy";
        var extension = GetExtension(fullName);
        var noExtName = fullName[..^extension.Length];

        // Has the exact same name before the suffix, optionally has the suffix which is optionally iterated, and has the exact same extension
        Regex re = new($"^{noExtName}(?:{copySuffix}(?: (?<iter>\\d+))?)?{extension.Replace(".", "\\.")}$");

        var items = from i in fileList
                    let match = re.Match(i)
                    where match.Success
                    select match.Groups["iter"].Value;

        return $"{noExtName}{ExistingIndexes(items, copySuffix)}{extension}";
    }

    public static string ExistingIndexes(IEnumerable<string> suffixes, string copySuffix = "")
    {
        var indexes = (from i in suffixes
                       where int.TryParse(i, out _)
                       select int.Parse(i)).ToList();
        if (suffixes.Any(s => s == ""))
            indexes.Add(0);

        indexes.Sort();
        if (indexes.Count == 0 || indexes[0] != 0)
            return "";

        var result = "";
        for (int i = 0; i < indexes.Count; i++)
        {
            if (indexes[i] > i)
            {
                result = $"{copySuffix} {i}";
                break;
            }
        }
        if (result == "")
            result = $"{copySuffix} {indexes.Count}";

        return result;
    }

    public enum RenameTarget
    {
        Unix,
        FUSE,
        Windows,
    }

    static readonly Func<string, bool> FileNamePredicateWindows = (name) => 
        !AdbExplorerConst.INVALID_WINDOWS_FILENAMES.Contains(name)
        && !name.Any(c => AdbExplorerConst.INVALID_NTFS_CHARS.Any(chr => chr == c))
        && name.Length > 0
        && name[^1] is not ' ' and not '.'
        && name[0] is not ' ';

    static readonly Func<string, bool> FileNamePredicateFuse = (name) =>
        !name.Any(c => AdbExplorerConst.INVALID_NTFS_CHARS.Contains(c))
        && name.Length > 0
        && name is not "." and not "..";

    static readonly Func<string, bool> FileNamePredicateUnix = (name) =>
        name.Length > 0
        && name is not "." and not "..";

    public static bool FileNameLegal(string fileName, RenameTarget target)
        => FileNameLegal(new[] { fileName }, target);

    public static bool FileNameLegal(FilePath file, RenameTarget target)
        => FileNameLegal(new[] { file }, target);

    public static bool FileNameLegal(IEnumerable<FilePath> files, RenameTarget target)
        => FileNameLegal(files.Select(f => f.FullName), target);

    public static bool FileNameLegal(IEnumerable<string> names, RenameTarget target)
    {
        var predicate = target switch
        {
            RenameTarget.Unix => FileNamePredicateUnix,
            RenameTarget.FUSE => FileNamePredicateFuse,
            RenameTarget.Windows => FileNamePredicateWindows,
            _ => throw new NotSupportedException(),
        };

        return names.AnyAll(predicate);
    }

    public static string[] ApkExtensions => [.. AdbExplorerConst.APK_NAMES.Select(n => n[1..])];

    public static bool AllFilesAreApks(string[] items) =>
        items.AnyAll(i => i.Contains('.') && ApkExtensions.Any(n => n == i.Split('.').Last().ToUpper()));

    /// <summary>
    /// Returns the relation of <paramref name="other"/> to <paramref name="self"/>.<br />
    /// Example: RelationFrom(File, File.Parent) = Ancestor</summary>
    /// <param name="self"></param>
    /// <param name="other"></param>
    /// <returns></returns>
    public static RelationType RelationFrom(string self, string other)
    {
        var separator = GetSeparator(self);

        if (other == self)
            return RelationType.Self;

        if (other.StartsWith(self) && other[self.Length..(self.Length + 1)][0] == separator)
            return RelationType.Descendant;

        if (self.StartsWith(other))
            return RelationType.Ancestor;

        return RelationType.Unrelated;
    }
}
