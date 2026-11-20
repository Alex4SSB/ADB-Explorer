using Microsoft.Win32.SafeHandles;
using Vanara.PInvoke;
using static ADB_Explorer.Models.AbstractDrive;
using static ADB_Explorer.Models.AbstractFile;
using static Vanara.PInvoke.CldApi;
using DriveType = ADB_Explorer.Models.AbstractDrive.DriveType;

namespace ADB_Explorer.Services;

// The mount root stands in for the device: a folder for each drive the mount mirrors, named and iconed as in the app.
// The drives are taken when mounting, so one inserted later shows once the device is mounted again.
public sealed partial class DeviceMount
{
    private const string INTERNAL_STORAGE_PATH = "/sdcard";

    private const string FOLDER_SETTINGS_FILE = "desktop.ini";

    private static readonly DriveType[] MOUNTABLE_DRIVE_TYPES = [DriveType.Internal, DriveType.Expansion, DriveType.External];

    /// <param name="MapsNames">
    /// Names Windows refuses show as lookalikes (<see cref="FileNameEncoding"/>), when names are substituted. Not on FUSE drives,
    /// where names go to and from the device as they are, and those Windows refuses are left out.
    /// </param>
    private readonly record struct MountedDrive(string FolderName, string DevicePath, DriveType Type, bool MapsNames);

    private readonly record struct MountableDrive(string Name, string DevicePath, DriveType Type);

    /// <summary>Internal storage, SD cards and USB drives; internal storage even when the device's drives aren't known yet.</summary>
    private static List<MountableDrive> MountableDrives(LogicalDeviceViewModel device)
    {
        List<DriveViewModel> drives;
        if (App.AppDispatcher is { } dispatcher && !dispatcher.CheckAccess())
            drives = dispatcher.Invoke(() => device.Drives.ToList());
        else
            drives = [.. device.Drives];

        List<MountableDrive> result = [.. drives
            .Where(drive => IsMountable(drive) && !string.IsNullOrEmpty(drive.Path))
            .Select(drive => new MountableDrive(drive.DisplayName, drive.Path.TrimEnd('/'), drive.Type))
            .DistinctBy(drive => drive.DevicePath)];

        if (!result.Any(drive => drive.Type is DriveType.Internal))
            result.Insert(0, new(GetDriveDisplayName(DriveType.Internal), INTERNAL_STORAGE_PATH, DriveType.Internal));

        return result;
    }

    /// <summary>Storage drives, and the temp drive.</summary>
    private static bool IsMountable(DriveViewModel drive) => drive switch
    {
        LogicalDriveViewModel => MOUNTABLE_DRIVE_TYPES.Contains(drive.Type),
        VirtualDriveViewModel { Type: DriveType.Temp } => true,
        _ => false,
    };

    /// <summary>Each drive's folder, named as the app shows it, made valid and unique for Windows.</summary>
    private static List<MountedDrive> DriveFolders(LogicalDeviceViewModel device)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<MountedDrive> result = [];
        foreach (var drive in MountableDrives(device))
        {
            var name = SanitizeFileName(string.IsNullOrWhiteSpace(drive.Name) ? FileHelper.GetFullName(drive.DevicePath) : drive.Name);
            var unique = name;
            for (var number = 2; !names.Add(unique); number++)
                unique = $"{name} ({number})";

            result.Add(new(unique, drive.DevicePath, drive.Type, !IsFuseDrive(drive.Type) && FileHelper.SubstitutesIllegalNames));
        }

        return result;
    }

    /// <summary>Android serves its storage volumes through FUSE (or sdcardfs): internal storage, SD cards and USB drives.</summary>
    private static bool IsFuseDrive(DriveType type) => MOUNTABLE_DRIVE_TYPES.Contains(type);

    /// <summary>The mounted drive a device path is on, the innermost where drives nest.</summary>
    private MountedDrive? DriveOf(string devicePath)
        => _drives
            .Where(candidate => IsUnder(devicePath, candidate.DevicePath))
            .OrderByDescending(candidate => candidate.DevicePath.Length)
            .Cast<MountedDrive?>()
            .FirstOrDefault();

    private static bool IsUnder(string path, string root) => path == root || path.StartsWith($"{root}/");

    /// <summary>Whether the device path is on a drive the mount mirrors, outside the app's recycle bin.</summary>
    public static bool CanMount(LogicalDeviceViewModel device, string? devicePath)
        => devicePath is not null
        && !FileHelper.IsInTrash(devicePath)
        && MountableDrives(device).Any(drive => IsUnder(devicePath, drive.DevicePath));

    private bool IsMounted(string devicePath)
        => !FileHelper.IsInTrash(devicePath) && _drives.Any(drive => IsUnder(devicePath, drive.DevicePath));

    /// <summary>A drive's own folder, or the icon file the mount put in it: neither is the device's to change.</summary>
    private bool IsDriveFolderOrIcon(string devicePath)
        => _drives.Any(drive => devicePath == drive.DevicePath || devicePath == $"{drive.DevicePath}/{FOLDER_SETTINGS_FILE}");

    /// <summary>The local path for a device path on a mounted drive, or the mount root for any other path.</summary>
    public string ToLocalPath(string devicePath)
    {
        if (DriveOf(devicePath) is not { } found)
            return LocalRoot;

        var relative = devicePath[found.DevicePath.Length..].Trim('/');
        if (relative.Length == 0)
            return Path.Combine(LocalRoot, found.FolderName);

        // Without a case variant, which only a folder's listing can tell
        var names = relative.Split('/');
        if (found.MapsNames)
            names = [.. names.Select(name => FileNameEncoding.ToWindows(name) ?? name)];

        return Path.Combine([LocalRoot, found.FolderName, .. names]);
    }

    /// <summary>The path under <see cref="LocalRoot"/>, "." for the root itself, or null outside it.</summary>
    private string? RelativeToRoot(string localPath)
    {
        var relative = Path.GetRelativePath(LocalRoot, localPath);
        if (relative.StartsWith("..") || Path.IsPathRooted(relative))
            return null;

        return relative;
    }

    /// <summary>The device path for a local path under a drive's folder; null for the root, which no device path matches, and outside it.</summary>
    private string? ToDevicePath(string localPath)
    {
        if (RelativeToRoot(localPath) is not { } relative || relative == ".")
            return null;

        var parts = relative.Split(Path.DirectorySeparatorChar, 2);
        MountedDrive? drive = _drives
            .Where(candidate => string.Equals(candidate.FolderName, parts[0], StringComparison.OrdinalIgnoreCase))
            .Cast<MountedDrive?>()
            .FirstOrDefault();

        if (drive is not { } found)
            return null;

        if (parts.Length == 1)
            return found.DevicePath;

        var names = parts[1].Split(Path.DirectorySeparatorChar);
        if (found.MapsNames)
            names = [.. names.Select(FileNameEncoding.ToDevice)];

        return FileHelper.ConcatPaths(found.DevicePath, string.Join('/', names));
    }

    private bool IsUnderRoot(string? path) => !string.IsNullOrEmpty(path) && RelativeToRoot(path) is not null;

    /// <summary>The root's listing: the drives' folders, read-only so File Explorer reads their icon files.</summary>
    private void TransferDriveFolders(CF_OPERATION_INFO opInfo)
    {
        var now = DateTime.Now;
        List<FileStat> folders = [.. _drives.Select(drive => new FileStat(drive.FolderName, drive.DevicePath, FileType.Folder, false, 0, now, null))];
        TransferPlaceholders(opInfo, folders, NTStatus.STATUS_SUCCESS, true, FileFlagsAndAttributes.FILE_ATTRIBUTE_READONLY);

        foreach (var drive in _drives)
        {
            var (dll, index) = FileIconProvider.GetDriveIconResource(drive.Type);
            AddFolderSettings(Path.Combine(LocalRoot, drive.FolderName), $"[.ShellClassInfo]\r\nIconResource={dll},{index}\r\n");
        }
    }

    /// <summary>
    /// A desktop.ini for a folder of the mount, which File Explorer reads as the folder is read-only. Made an in-sync,
    /// pinned placeholder, so the platform never asks the device for it, and the mount's cleanup removes it with the rest.
    /// </summary>
    private static void AddFolderSettings(string folder, string settings)
    {
        var path = Path.Combine(folder, FOLDER_SETTINGS_FILE);
        try
        {
            var info = new DirectoryInfo(folder);
            info.Attributes |= FileAttributes.ReadOnly;

            File.WriteAllText(path, settings, Encoding.Unicode);
            File.SetAttributes(path, FileAttributes.Hidden | FileAttributes.System);

            using SafeFileHandle handle = File.OpenHandle(path, FileMode.Open, FileAccess.ReadWrite);
            var file = new HFILE(handle.DangerousGetHandle());
            CfConvertToPlaceholder(file, PLACEHOLDER_IDENTITY, 1, CF_CONVERT_FLAGS.CF_CONVERT_FLAG_MARK_IN_SYNC, out _, IntPtr.Zero).ThrowIfFailed();
            CfSetPinState(file, CF_PIN_STATE.CF_PIN_STATE_PINNED, CF_SET_PIN_FLAGS.CF_SET_PIN_FLAG_NONE, IntPtr.Zero).ThrowIfFailed();
        }
        catch (Exception e)
        {
#if !DEPLOY
            DebugLog.PrintLine($"Mount folder settings for {folder} failed: {e.Message}");
#endif
        }
    }
}
