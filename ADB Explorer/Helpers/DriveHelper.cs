using ADB_Explorer.Models;

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
}
