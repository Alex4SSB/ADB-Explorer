using ADB_Explorer.Models;
using ADB_Explorer.Services;
using ADB_Explorer.ViewModels;
using Vanara.Windows.Shell;

namespace ADB_Explorer.Helpers;

public class FolderHelper
{
    public static void CombineDisplayNames()
    {
        var driveView = AdbLocation.StringFromLocation(Navigation.SpecialLocation.DriveView);
        if (Data.CurrentDisplayNames.ContainsKey(driveView))
            Data.CurrentDisplayNames[driveView] = Data.DevicesObject.Current.Name;
        else
            Data.CurrentDisplayNames.Add(driveView, Data.DevicesObject.Current.Name);

        foreach (var drive in Data.DevicesObject.Current.Drives.OfType<LogicalDriveViewModel>().Where(d => d.Type 
            is not AbstractDrive.DriveType.Root 
            and not AbstractDrive.DriveType.Internal))
        {
            Data.CurrentDisplayNames.TryAdd(drive.Path, drive.Type is AbstractDrive.DriveType.External
                ? drive.ID : drive.DisplayName);
        }

        foreach (var item in AdbExplorerConst.DRIVE_TYPES.Where(d => d.Value is AbstractDrive.DriveType.Root or AbstractDrive.DriveType.Internal))
        {
            Data.CurrentDisplayNames.TryAdd(item.Key, AbstractDrive.GetDriveDisplayName(item.Value));
        }

        foreach (var item in AdbExplorerConst.DRIVE_TYPES)
        {
            var names = Enum.GetValues<AbstractDrive.DriveType>().Where(n => n == item.Value && item.Value
                is not AbstractDrive.DriveType.Root
                and not AbstractDrive.DriveType.Internal)
                .Select(AbstractDrive.GetDriveDisplayName);

            if (names.Any())
                Data.CurrentDisplayNames.TryAdd(item.Key, names.First());
        }

        Data.RuntimeSettings.RefreshBreadcrumbs = true;
    }

    public static string FolderExists(string path)
    {
        if (path == AdbLocation.StringFromLocation(Navigation.SpecialLocation.PackageDrive))
            return path;

        if (path == AdbLocation.StringFromLocation(Navigation.SpecialLocation.RecycleBin))
            return AdbExplorerConst.RECYCLE_PATH;

        try
        {
            return Data.CurrentADBDevice.TranslateDevicePath(path);
        }
        catch (Exception e)
        {
            if (path != AdbExplorerConst.RECYCLE_PATH)
                DialogService.ShowMessage(e.Message, Strings.Resources.S_NAV_ERR_TITLE, DialogService.DialogIcon.Critical, copyToClipboard: true);

            return null;
        }
    }

    public static IEnumerable<ShellItem> GetEmptySubfoldersRecursively(ShellFolder rootFolder)
    {
        var emptyFolders = new List<ShellItem>();
        FindEmptySubfolders(rootFolder, emptyFolders);
        return emptyFolders.Where(f => f.ParsingName != rootFolder.ParsingName);
    }

    private static bool FindEmptySubfolders(ShellFolder folder, List<ShellItem> result)
    {
        if (!folder.IsFolder) return false;

        bool hasNonFolder = false;
        bool hasNonEmptyFolder = false;

        foreach (var child in folder)
        {
            if (!child.IsFolder)
            {
                hasNonFolder = true;
                continue;
            }

            // Recurse into subfolder
            if (!FindEmptySubfolders((ShellFolder)child, result))
            {
                hasNonEmptyFolder = true;
            }
        }

        bool isEmpty = !hasNonFolder && !hasNonEmptyFolder && !folder.Any();

        if (isEmpty)
        {
            result.Add(folder);
            return true;
        }

        return false;
    }
}
