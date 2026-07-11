using ADB_Explorer.Models;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Helpers;

internal class DriveHelper
{
    public static void ClearDrives()
    {
        App.SafeInvoke(() => Data.DevicesObject.Current?.Drives.Clear());
        Data.FileActions.IsDriveViewVisible = false;
    }

    public static DriveViewModel GetCurrentDrive(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;

        if (AdbLocation.LocationFromString(path) is not Navigation.SpecialLocation.None)
            return null;

        var drives = Data.DevicesObject?.Current?.Drives;
        if (drives is null)
            return null;

        // First search for a non-root drive that matches the path
        var nonRoot = drives.FirstOrDefault(d =>
            d.Type is not AbstractDrive.DriveType.Root && IsOnDrive(path, d));

        if (nonRoot is null)
            return drives.FirstOrDefault(d => d.Type is AbstractDrive.DriveType.Root);

        return nonRoot;
    }

    private static bool IsOnDrive(string path, DriveViewModel drive)
    {
        if (path == drive.Path || path.StartsWith($"{drive.Path.TrimEnd('/')}/", StringComparison.Ordinal))
            return true;

        return drive.Type is AbstractDrive.DriveType.Internal && AdbExplorerConst.IsInternalStoragePath(path);
    }

    public static bool IsModificationAllowedAt(string path, string deviceId)
    {
        if (GetCurrentDrive(path)?.Restrictions.ReadOnly is true)
            return false;

        return ArchiveHelper.IsModificationAllowedAt(path, deviceId);
    }
}
