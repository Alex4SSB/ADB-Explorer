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
}

public class Drive : AbstractDrive
{
    public string Path { get; protected set; }


    public Drive(string path = "")
    {
        Path = path;

        if (Type is DriveType.Unknown && DRIVE_TYPES.ContainsKey(path))
        {
            Type = DRIVE_TYPES[path];
            if (Type is DriveType.Internal)
                Path = "/sdcard";
        }
    }
}
