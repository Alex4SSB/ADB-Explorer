using ADB_Explorer.Models;
using ADB_Explorer.Resources;
using ADB_Explorer.Services;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Helpers;

internal class FolderHelper
{
    public static void CombineDisplayNames()
    {
        var driveView = NavHistory.StringFromLocation(NavHistory.SpecialLocation.DriveView);
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

        foreach (var item in AdbExplorerConst.SPECIAL_FOLDERS_DISPLAY_NAMES)
        {
            Data.CurrentDisplayNames.TryAdd(item.Key, item.Value);
        }

        foreach (var item in AdbExplorerConst.DRIVE_TYPES)
        {
            var names = AdbExplorerConst.DRIVE_DISPLAY_NAMES.Where(n => n.Key == item.Value && item.Value
                is not AbstractDrive.DriveType.Root
                and not AbstractDrive.DriveType.Internal);

            if (names.Any())
                Data.CurrentDisplayNames.TryAdd(item.Key, names.First().Value);
        }
    }

    public static string FolderExists(string path)
    {
        if (path == NavHistory.StringFromLocation(NavHistory.SpecialLocation.PackageDrive))
            return path;

        if (path == NavHistory.StringFromLocation(NavHistory.SpecialLocation.RecycleBin))
            return AdbExplorerConst.RECYCLE_PATH;

        try
        {
            return Data.CurrentADBDevice.TranslateDevicePath(path);
        }
        catch (Exception e)
        {
            if (path != AdbExplorerConst.RECYCLE_PATH)
                DialogService.ShowMessage(e.Message, Strings.S_NAV_ERR_TITLE, DialogService.DialogIcon.Critical, copyToClipboard: true);

            return null;
        }
    }
}
