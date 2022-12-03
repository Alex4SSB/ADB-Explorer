using ADB_Explorer.Converters;

namespace ADB_Explorer.Models;

public class LogicalDrive : Drive
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

    public string ID => Path[(Path.LastIndexOf('/') + 1)..];


    public LogicalDrive(string size = "", string used = "", string available = "", sbyte usageP = -1, string path = "", bool isMMC = false, bool isEmulator = false)
        : base(path)
    {
        if (usageP is < -1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(usageP));

        Size = size;
        Used = used;
        Available = available;
        UsageP = usageP;

        if (isMMC)
        {
            Type = DriveType.Expansion;
        }
        else if (isEmulator)
        {
            Type = DriveType.Emulated;
        }
    }

    public LogicalDrive(GroupCollection match, bool isMMC = false, bool isEmulator = false, string forcePath = "")
        : this(
              (ulong.Parse(match["size_kB"].Value) * 1024).ToSize(true, 2, 2),
              (ulong.Parse(match["used_kB"].Value) * 1024).ToSize(true, 2, 2),
              (ulong.Parse(match["available_kB"].Value) * 1024).ToSize(true, 2, 2),
              sbyte.Parse(match["usage_P"].Value),
              string.IsNullOrEmpty(forcePath) ? match["path"].Value : forcePath,
              isMMC,
              isEmulator)
    { }


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
}
