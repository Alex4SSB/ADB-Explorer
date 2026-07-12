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

    /// <summary>
    /// Restrictions of the mount a path sits on. The root drive spans several mounts (/ is ro, /data is rw),
    /// so paths under it are matched against the device mount table by longest prefix.
    /// </summary>
    public static DriveRestrictions GetPathRestrictions(string path)
    {
        var drive = GetCurrentDrive(path);

        if (drive?.Type is AbstractDrive.DriveType.Root
            && Data.DevicesObject?.Current?.MountPoints is { Count: > 0 } mounts)
        {
            Models.FileSystemInfo? best = null;
            foreach (var mount in mounts)
            {
                var point = mount.MountPoint;
                if (string.IsNullOrEmpty(point))
                    continue;

                if (path == point || path.StartsWith(point.TrimEnd('/') + '/', StringComparison.Ordinal))
                {
                    // longest prefix wins; ties go to the later (more recent) mount
                    if (best is null || point.Length >= best.Value.MountPoint.Length)
                        best = mount;
                }
            }

            if (best is not null)
                return DriveRestrictions.From(best.Value.Options, best.Value.FileSystemType);
        }

        return drive?.Restrictions ?? DriveRestrictions.None;
    }

    /// <summary>Archive-aware variant: resolves a composite archive path to the archive file first.</summary>
    public static DriveRestrictions GetPathRestrictions(string path, string? deviceId)
    {
        if (ArchivePath.IsArchivePath(path, deviceId))
            path = ArchivePath.GetArchivePath(path, deviceId);

        return GetPathRestrictions(path);
    }

    public static bool IsModificationAllowedAt(string path, string deviceId)
    {
        if (GetPathRestrictions(path).ReadOnly)
            return false;

        return ArchiveHelper.IsModificationAllowedAt(path, deviceId);
    }
}
