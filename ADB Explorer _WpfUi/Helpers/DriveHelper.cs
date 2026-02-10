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
        App.Current.Dispatcher.Invoke(() => Data.DevicesObject.Current?.Drives.Clear());
        Data.FileActions.IsDriveViewVisible = false;
    }

    public static DriveViewModel GetCurrentDrive(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;

        // First search for a non-root drive that matches the path
        var nonRoot = Data.DevicesObject.Current?.Drives.FirstOrDefault(d => d.Type is not AbstractDrive.DriveType.Root && path.StartsWith(d.Path));
        if (nonRoot is null)
            return Data.DevicesObject.Current?.Drives.FirstOrDefault(d => d.Type is AbstractDrive.DriveType.Root);

        return nonRoot;
    }
}
