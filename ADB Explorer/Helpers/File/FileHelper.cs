using ADB_Explorer.Converters;
using ADB_Explorer.Models;
using ADB_Explorer.Resources;
using ADB_Explorer.Services;
using System.IO;

namespace ADB_Explorer.Helpers;

internal class FileHelper
{
    public static FileClass ListerFileManipulator(FileClass item)
    {
        if (Data.CutItems.Any() && (Data.CutItems[0].ParentPath == Data.DirList.CurrentPath))
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

    public static bool IsHiddenRecycleItem(FileClass file)
    {
        if (AdbExplorerConst.RECYCLE_PATHS.Contains(file.FullPath))
            return true;

        if (!string.IsNullOrEmpty(Data.FileActions.ExplorerFilter) && !file.ToString().Contains(Data.FileActions.ExplorerFilter, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    public static void RenameFile(string newName, FileClass file)
    {
        var newPath = $"{file.ParentPath}{(file.ParentPath.EndsWith('/') ? "" : "/")}{newName}{(Data.Settings.ShowExtensions ? "" : file.Extension)}";
        if (Data.DirList.FileList.Any(file => file.FullName == newName))
        {
            DialogService.ShowMessage(Strings.S_PATH_EXIST(newPath), Strings.S_RENAME_CONF_TITLE, DialogService.DialogIcon.Exclamation);
            return;
        }

        try
        {
            ShellFileOperation.MoveItem(Data.CurrentADBDevice, file, newPath);
        }
        catch (Exception e)
        {
            DialogService.ShowMessage(e.Message, Strings.S_RENAME_ERR_TITLE, DialogService.DialogIcon.Critical);
            throw;
        }

        file.UpdatePath(newPath);
        if (Data.Settings.ShowExtensions)
            file.UpdateType();
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
}
