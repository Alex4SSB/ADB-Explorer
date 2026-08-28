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

    public static DriveRestrictions GetRestrictions(string path, LogicalDeviceViewModel? device = null)
    {
        if (string.IsNullOrEmpty(path))
            return DriveRestrictions.None;

        device ??= Data.Active.Device ?? Data.DevicesObject?.Current;
        var drive = GetCurrentDrive(path, device);

        // Overlay/submount writability (e.g. rw /vendor vs ro /) only matters with a root shell.
        if (device is not { HasRootShell: true })
            return drive?.Restrictions ?? DriveRestrictions.None;

        var lookupPath = GetMountLookupPath(path, device, drive);
        var includeRoot = drive is null || drive.Type is AbstractDrive.DriveType.Root;
        var mount = device.Mounts.Find(lookupPath, includeRoot);
        if (mount is not null)
            return DriveRestrictions.From(mount.Value.Options);

        return drive?.Restrictions ?? DriveRestrictions.None;
    }

    public static DriveRestrictions GetRestrictions(string path, string? deviceId)
    {
        LogicalDeviceViewModel? device = null;
        if (!string.IsNullOrEmpty(deviceId))
            device = Data.DevicesObject?.LogicalDeviceViewModels?.FirstOrDefault(d => d.ID == deviceId);

        return GetRestrictions(path, device);
    }

    /// <summary>
    /// Translate a drive alias such as <c>/sdcard</c> to the path <c>mount</c> actually lists.
    /// </summary>
    private static string GetMountLookupPath(string path, LogicalDeviceViewModel? device, DriveViewModel? drive)
    {
        var deviceId = device?.ID;
        if (!string.IsNullOrEmpty(deviceId) && ArchivePath.IsArchivePath(path, deviceId))
            path = ArchivePath.GetArchivePath(path, deviceId);

        drive ??= GetCurrentDrive(path, device);
        if (drive is null || string.IsNullOrEmpty(drive.LinkTargetPath))
            return path;

        var drivePath = drive.Path.TrimEnd('/');
        if (path == drive.Path || path == drivePath)
            return drive.LinkTargetPath;

        if (path.StartsWith(drivePath + "/", StringComparison.Ordinal))
            return FileHelper.ConcatPaths(drive.LinkTargetPath, path[(drivePath.Length + 1)..]);

        return path;
    }

    public static bool IsModificationAllowedAt(string path, string deviceId)
    {
        LogicalDeviceViewModel? device = null;
        if (!string.IsNullOrEmpty(deviceId))
            device = Data.DevicesObject?.LogicalDeviceViewModels?.FirstOrDefault(d => d.ID == deviceId);

        if (GetRestrictions(path, device).ReadOnly)
            return false;

        return ArchiveHelper.IsModificationAllowedAt(path, deviceId);
    }

    public static bool RequiresTempForApkInstall(string path)
    {
        if (path == AdbExplorerConst.TEMP_PATH
            || path.StartsWith($"{AdbExplorerConst.TEMP_PATH}/", StringComparison.Ordinal))
            return false;

        return GetRestrictions(path).NoApkInstall;
    }

    /// <summary>
    /// Whether <paramref name="path"/> is on a filesystem that can persist unix mode/owner/group.
    /// FUSE/FAT volumes and archives expose synthetic rwx bits that chmod/chown cannot change.
    /// </summary>
    public static bool SupportsUnixMetadataChanges(string? path, LogicalDeviceViewModel? device = null)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        device ??= Data.Active.Device ?? Data.DevicesObject?.Current;
        if (device is null)
            return false;

        if (ArchivePath.IsArchivePath(path, device.ID))
            return false;

        var drive = GetCurrentDrive(path, device);
        if (IsNonUnixMetadataFileSystem(drive?.FileSystem))
            return false;

        var restrictions = GetRestrictions(path, device);
        if (restrictions.NoExec)
            return false;

        if (!restrictions.ReadOnly)
            return true;

        // Without a root shell, nested native mounts are attributed to the read-only
        // root drive. Do not treat that as a real RO block for those paths.
        if (device.HasRootShell)
            return false;

        return drive?.Type is AbstractDrive.DriveType.Root;
    }

    public static ShellAccessHelper.UnixPermissionChanges GetEditableUnixChanges(FileClass? file, LogicalDeviceViewModel? device = null)
    {
        if (file?.Permissions is null)
            return ShellAccessHelper.UnixPermissionChanges.None;

        device ??= Data.Active.Device ?? Data.DevicesObject?.Current;
        if (device is null)
            return ShellAccessHelper.UnixPermissionChanges.None;

        if (Data.FileActions.IsRecycleBin || Data.FileActions.IsAppDrive)
            return ShellAccessHelper.UnixPermissionChanges.None;

        if (!SupportsUnixMetadataChanges(file.FullPath, device))
            return ShellAccessHelper.UnixPermissionChanges.None;

        return ShellAccessHelper.GetAllowedChanges(file.OwnerUid, file.User, device.ShellIdentity);
    }

    private static bool IsNonUnixMetadataFileSystem(string? fileSystem)
    {
        if (string.IsNullOrEmpty(fileSystem))
            return false;

        return fileSystem.ToLowerInvariant() switch
        {
            "fuse" or "fuseblk" or "sdcardfs" or "vfat" or "exfat"
                or "msdos" or "fat" or "ntfs" or "texfat" => true,
            _ => false,
        };
    }
}
