using static ADB_Explorer.Models.AdbExplorerConst;

namespace ADB_Explorer.Models;

/// <summary>
/// Constants that still depend on app or WPF types. The rest live in <see cref="AdbExplorerConst"/> (Core).
/// </summary>
public static class AppConst
{
    public static readonly Dictionary<string, AbstractDrive.DriveType> DRIVE_TYPES = new()
    {
        { "/sdcard", AbstractDrive.DriveType.Internal },
        { "/storage/emulated/0", AbstractDrive.DriveType.Internal },
        { "/storage/self/primary", AbstractDrive.DriveType.Internal },
        { "/mnt/sdcard", AbstractDrive.DriveType.Internal },
        { "/storage/emulated", AbstractDrive.DriveType.Internal },
        { "/mnt/runtime/write/emulated/0", AbstractDrive.DriveType.Internal }, // for emulators
        { RECYCLE_PATH, AbstractDrive.DriveType.Trash },
        { $"/storage/emulated/0/{RECYCLE_FOLDER}", AbstractDrive.DriveType.Trash },
        { $"/storage/self/primary/{RECYCLE_FOLDER}", AbstractDrive.DriveType.Trash },
        { $"/mnt/sdcard/{RECYCLE_FOLDER}", AbstractDrive.DriveType.Trash },
        { $"/mnt/runtime/write/emulated/0/{RECYCLE_FOLDER}", AbstractDrive.DriveType.Trash },
        { AdbLocation.StringFromLocation(Navigation.SpecialLocation.RecycleBin), AbstractDrive.DriveType.Trash },
        { TEMP_PATH, AbstractDrive.DriveType.Temp },
        { AdbLocation.StringFromLocation(Navigation.SpecialLocation.PackageDrive), AbstractDrive.DriveType.Package },
        { "/", AbstractDrive.DriveType.Root },
    };

    private static readonly Dictionary<string, AbstractDrive.DriveType> _inetrnalDrivePaths
        = DRIVE_TYPES.Where(kv => kv.Value is AbstractDrive.DriveType.Internal).ToDictionary();

    public static bool IsInternalStoragePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        return _inetrnalDrivePaths.Any(kv => path == kv.Key || path.StartsWith($"{kv.Key}/", StringComparison.Ordinal));
    }

    public static List<string> POSSIBLE_RECYCLE_PATHS =>
        [.. _inetrnalDrivePaths.Select(kv => $"{kv.Key}/{RECYCLE_FOLDER}")];

    /// <summary>
    /// Per-device <c>{internal storage}/Android</c> paths whose directory entry FUSE forbids renaming or deleting.
    /// Contents under these paths remain writable.
    /// </summary>
    public static readonly string[] FUSE_PROTECTED_ANDROID_ROOT_PATHS =
        [.. _inetrnalDrivePaths.Select(kv => $"{kv.Key.TrimEnd('/')}/Android")];

    public static readonly Point DRAG_OFFSET_DEFAULT = new(48, 89);

    public static readonly Key[] NAVIGATION_KEYS = [Key.Enter, Key.Up, Key.Down, Key.Left, Key.Right, Key.Escape, Key.Home, Key.End];
}
