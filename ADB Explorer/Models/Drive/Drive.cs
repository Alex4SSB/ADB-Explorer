using ADB_Explorer.Converters;
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
}

public class Drive : AbstractDrive
{
    private string size;
    public string Size
    {
        get => size;
        set => Set(ref size, value);
    }

    private string used;
    public string Used
    {
        get => used;
        set => Set(ref used, value);
    }

    private string available;
    public string Available
    {
        get => available;
        set => Set(ref available, value);
    }

    private sbyte usageP;
    public sbyte UsageP
    {
        get => usageP;
        set => Set(ref usageP, value);
    }

    public string Path { get; }

    private ulong itemsCount = 0;
    public ulong ItemsCount
    {
        get => itemsCount;
        set => Set(ref itemsCount, value);
    }

    public string ID => Path[(Path.LastIndexOf('/') + 1)..];

    public List<string> SetDriveParams(string size = "", string used = "", string available = "", sbyte usageP = -1)
    {
        List<string> updatedParams = new();

        if (usageP is < -1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(usageP));

        if (size != "" && Size != size)
        {
            Size = size;
            updatedParams.Add(nameof(Size));
        }

        if (used != "" && Used != used)
        {
            Used = used;
            updatedParams.Add(nameof(Used));
        }

        if (available != "" && Available != available)
        {
            Available = available;
            updatedParams.Add(nameof(Available));
        }

        if (usageP > -1 && UsageP != usageP)
        {
            UsageP = usageP;
            updatedParams.Add(nameof(UsageP));
        }

        return updatedParams;
    }

    public Drive(string size = "", string used = "", string available = "", sbyte usageP = -1, string path = "", bool isMMC = false, bool isEmulator = false)
    {
        if (usageP is < -1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(usageP));

        Size = size;
        Used = used;
        Available = available;
        UsageP = usageP;
        Path = path;

        if (DRIVE_TYPES.ContainsKey(path))
        {
            Type = DRIVE_TYPES[path];
            if (Type is DriveType.Internal)
                Path = "/sdcard";
        }
        else if (isMMC)
        {
            Type = DriveType.Expansion;
        }
        else if (isEmulator)
        {
            Type = DriveType.Emulated;
        }
        else
        {
            Type = DriveType.Unknown;
        }
    }

    public Drive(GroupCollection match, bool isMMC = false, bool isEmulator = false, string forcePath = "")
        : this(
              (ulong.Parse(match["size_kB"].Value) * 1024).ToSize(true, 2, 2),
              (ulong.Parse(match["used_kB"].Value) * 1024).ToSize(true, 2, 2),
              (ulong.Parse(match["available_kB"].Value) * 1024).ToSize(true, 2, 2),
              sbyte.Parse(match["usage_P"].Value),
              string.IsNullOrEmpty(forcePath) ? match["path"].Value : forcePath,
              isMMC,
              isEmulator)
    { }

    public static implicit operator bool(Drive obj)
    {
        return obj is not null;
    }

    public static bool operator ==(Drive lVal, Drive rVal)
    {
        return
            lVal.Type == rVal.Type &&
            lVal.Path == rVal.Path &&
            lVal.Available == rVal.Available &&
            lVal.Size == rVal.Size &&
            lVal.Used == rVal.Used &&
            lVal.UsageP == rVal.UsageP;
    }

    public static bool operator !=(Drive lVal, Drive rVal) => !(lVal == rVal);

    public override bool Equals(object obj)
    {
        if (ReferenceEquals(this, obj))
        {
            return true;
        }

        if (obj is null)
        {
            return false;
        }

        return this == (Drive)obj;
    }

    public override int GetHashCode()
    {
        return Path.GetHashCode();
    }


    public class DriveEqualityComparer : IEqualityComparer<Drive>
    {
        public bool Equals(Drive x, Drive y)
        {
            if (x.Path != y.Path) return false;
            if (x.Size != y.Size) return false;
            if (x.Used != y.Used) return false;
            if (y.Type is not DriveType.Unknown && x.Type != y.Type) return false;

            return true;
        }

        public int GetHashCode([DisallowNull] Drive obj)
        {
            throw new NotImplementedException();
        }
    }
}
