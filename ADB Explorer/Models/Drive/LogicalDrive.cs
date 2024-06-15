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

    private string fileSystem = "";
    public string FileSystem
    {
        get => fileSystem;
        set
        {
            if (Set(ref fileSystem, value))
                OnPropertyChanged(nameof(IsFUSE));
        }
    }

    public override bool IsFUSE => FileSystem.Contains("fuse");

    public string ID => Path.Count(c => c == '/') > 1 ? Path[(Path.LastIndexOf('/') + 1)..] : Path;


    public LogicalDrive(string size = "",
                        string used = "",
                        string available = "",
                        sbyte usageP = -1,
                        string path = "",
                        bool isMMC = false,
                        bool isEmulator = false,
                        string fileSystem = "")
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

        FileSystem = fileSystem;
    }

    public LogicalDrive(GroupCollection match, bool isMMC = false, bool isEmulator = false, string forcePath = "")
        : this(
              (ulong.Parse(match["size_kB"].Value) * 1024).ToSize(true, 2, 2),
              (ulong.Parse(match["used_kB"].Value) * 1024).ToSize(true, 2, 2),
              (ulong.Parse(match["available_kB"].Value) * 1024).ToSize(true, 2, 2),
              sbyte.Parse(match["usage_P"].Value),
              string.IsNullOrEmpty(forcePath) ? match["path"].Value : forcePath,
              isMMC,
              isEmulator,
              match["FileSystem"].Value)
    { }
}
