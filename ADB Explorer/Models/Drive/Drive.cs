using ADB_Explorer.ViewModels;
using static ADB_Explorer.Models.AdbExplorerConst;

namespace ADB_Explorer.Models;

public abstract class AbstractDrive : ViewModelBase
{
    public enum DriveType
    {
        Root,
        Internal,
        Expansion,
        External,
        Emulated,
        Unknown,
        Trash,
        Temp,
        Package,
    }

    private DriveType type = DriveType.Unknown;
    public DriveType Type
    {
        get => type;
        set => Set(ref type, value);
    }


    public static implicit operator bool(AbstractDrive obj)
    {
        return obj is not null;
    }

    public string DisplayName
    {
        get => GetDriveDisplayName(Type);
    }

    public static string GetDriveDisplayName(DriveType type) => type switch
    {
        DriveType.Root => Strings.Resources.S_DRIVE_ROOT,
        DriveType.Internal => Strings.Resources.S_DRIVE_INTERNAL_STORAGE,
        DriveType.Expansion => Strings.Resources.S_DRIVE_SD,
        DriveType.External => Strings.Resources.S_DRIVE_OTG,
        DriveType.Unknown => "",
        DriveType.Emulated => Strings.Resources.S_DRIVE_EMULATED,
        DriveType.Trash => Strings.Resources.S_DRIVE_TRASH,
        DriveType.Temp => Strings.Resources.S_DRIVE_TEMP,
        DriveType.Package => Strings.Resources.S_DRIVE_APPS,
        _ => null,
    };
}

public class Drive : AbstractDrive
{
    public string Path { get; protected set; }

    /// <summary>
    /// Filesystem in USEr space. An emulated / virtual filesystem on Android.<br /><br />
    /// Does not support:<br />
    /// • Symbolic links<br />
    /// • Special chars in file name (like NTFS)<br />
    /// • Installing APK from it
    /// </summary>
    public virtual bool IsFUSE { get; }


    public Drive(string path = "")
    {
        Path = path;

        if (Type is DriveType.Unknown && DRIVE_TYPES.TryGetValue(path, out var type))
        {
            Type = type;
            if (Type is DriveType.Internal)
                Path = "/sdcard";
        }
    }
}
