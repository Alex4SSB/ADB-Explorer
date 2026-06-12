using static ADB_Explorer.Models.AdbExplorerConst;

namespace ADB_Explorer.Models;

public abstract partial class AbstractDrive : ObservableObject
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

    [ObservableProperty]
    public partial DriveType Type { get; set; } = DriveType.Unknown;


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

    public void UpdatePath(string newPath)
    {
        Path = newPath;
        OnPropertyChanged(nameof(Path));
    }
}

public record struct FileSystemInfo(string BlockDev, string MountPoint, string FileSystemType, string[] Options);
