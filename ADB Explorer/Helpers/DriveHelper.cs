using ADB_Explorer.Models;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Helpers;

internal class DriveHelper
{
    public static void ClearSelectedDrives()
    {
        Data.RuntimeSettings.CollapseDrives = true;
        Data.RuntimeSettings.CollapseDrives = false;
    }

    public static void ClearDrives()
    {
        Data.DevicesObject.Current?.Drives.Clear();
        Data.FileActions.IsDriveViewVisible = false;
    }

    public static DriveViewModel GetCurrentDrive(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;

        var currentDrive = AdbExplorerConst.DRIVE_TYPES.FirstOrDefault(kv => path.StartsWith(kv.Key)).Value;
        return Data.DevicesObject.Current.Drives.FirstOrDefault(d => d.Type == currentDrive);
    }
}
