using ADB_Explorer.Converters;
using ADB_Explorer.Models;
using ADB_Explorer.Resources;
using ADB_Explorer.Services;

namespace ADB_Explorer.Helpers;

public static class FileHelper
{
    public static FileClass ListerFileManipulator(FileClass item)
    {
        if (Data.CutItems.Count > 0 && (Data.CutItems[0].ParentPath == Data.DirList.CurrentPath))
        {
            var cutItem = Data.CutItems.Where(f => f.FullPath == item.FullPath);
            if (cutItem.Any())
            {
                item.CutState = cutItem.First().CutState;
                Data.CutItems.Remove(cutItem.First());
                Data.CutItems.Add(item);
            }
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

        if (Data.DirList.FileList.Any(file => file.DisplayName == newName))
        {
            DialogService.ShowMessage(Strings.S_PATH_EXIST(newName), Strings.S_RENAME_CONF_TITLE, DialogService.DialogIcon.Exclamation, copyToClipboard: true);
            return;
        }

        ShellFileOperation.Rename(file, newPath, Data.CurrentADBDevice);
    }

    public static string DisplayName(TextBox textBox) => DisplayName(textBox.DataContext as FilePath);

    public static string DisplayName(FilePath file) => Data.Settings.ShowExtensions ? file?.FullName : file?.NoExtName;

    public static FileClass GetFromCell(DataGridCellInfo cell) => CellConverter.GetDataGridCell(cell).DataContext as FileClass;

    public static void ClearCutFiles(IEnumerable<FileClass> items = null)
    {
        var list = items is null ? Data.CutItems : items;
        foreach (var item in list)
        {
            item.CutState = FileClass.CutType.None;
        }
        Data.CutItems.RemoveAll(list.ToList());

        Data.FileActions.PasteState = FileClass.CutType.None;
    }

    public static string ConcatPaths(FilePath path1, string path2) => 
        ConcatPaths(path1.FullPath, path2, path1.PathType is AbstractFile.FilePathType.Android ? '/' : '\\');

    public static string ConcatPaths(string path1, string path2, char separator = '/')
    {
        return $"{path1.TrimEnd(AbstractFile.Separators)}{separator}{path2.TrimStart(AbstractFile.Separators)}";
    }

    public static string ExtractRelativePath(string fullPath, string parent)
    {
        if (fullPath == parent)
            return FilePath.GetFullName(fullPath);

        var index = fullPath.IndexOf(parent);
        
        var result = index < 0
            ? fullPath
            : fullPath[parent.Length..];

        return result.TrimStart('/', '\\');
    }

    public static string GetParentPath(string fullPath) =>
        fullPath[..FilePath.LastSeparatorIndex(fullPath)];
}
