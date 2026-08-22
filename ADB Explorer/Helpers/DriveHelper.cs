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

    public static DriveViewModel GetCurrentDrive(string path, LogicalDeviceViewModel? device = null)
    {
        if (string.IsNullOrEmpty(path)) return null;

        if (AdbLocation.LocationFromString(path) is not Navigation.SpecialLocation.None)
            return null;

        device ??= Data.Active.Device ?? Data.DevicesObject?.Current;
        var drives = device?.Drives;
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
        LogicalDeviceViewModel? device = null;
        if (!string.IsNullOrEmpty(deviceId))
            device = Data.DevicesObject?.LogicalDeviceViewModels?.FirstOrDefault(d => d.ID == deviceId);

        if (GetCurrentDrive(path, device)?.Restrictions.ReadOnly is true)
            return false;

        return ArchiveHelper.IsModificationAllowedAt(path, deviceId);
    }

    public static bool RequiresTempForApkInstall(string path)
    {
        if (path == AdbExplorerConst.TEMP_PATH
            || path.StartsWith($"{AdbExplorerConst.TEMP_PATH}/", StringComparison.Ordinal))
            return false;

        return GetCurrentDrive(path)?.Restrictions.NoApkInstall is true;
    }
}
