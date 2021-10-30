using ADB_Explorer.Converters;
using System.Text.RegularExpressions;
using static ADB_Explorer.Models.AdbExplorerConst;
using static ADB_Explorer.Models.Data;

namespace ADB_Explorer.Models
{
    public enum DriveType
    {
        Root,
        Internal,
        Expansion,
        External,
        Unknown,
        Emulated
    }

    public class Drive
    {
        public string Size { get; private set; }
        public string Used { get; private set; }
        public string Available { get; private set; }
        public byte? UsageP { get; private set; }
        public string Path { get; private set; }
        public string ID {
            get
            {
                return Path[(Path.LastIndexOf('/') + 1)..];
            }
        }
        public string PrettyName
        {
            get
            {
                return DRIVES_PRETTY_NAMES[Type];
            }
        }
        public DriveType Type { get; private set; }
        public string DriveIcon
        {
            get
            {
                return Type switch
                {
                    DriveType.Root => "\uE7EF",
                    DriveType.Internal => "\uEDA2",
                    DriveType.Expansion => "\uE7F1",
                    DriveType.External => "\uE88E",
                    DriveType.Unknown => "\uE9CE",
                    DriveType.Emulated => "\uEDA2",
                    _ => throw new System.NotImplementedException(),
                };
            }
        }

        public Drive(string size, string used, string available, byte? usageP, string path, bool isMMC = false, bool isEmulator = false)
        {
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
                Type = DriveType.External;
            }
        }

        public Drive(GroupCollection match, bool isMMC = false, bool isEmulator = false)
            : this(
                  (ulong.Parse(match["size_kB"].Value) * 1024).ToSize(true, 2, 2),
                  (ulong.Parse(match["used_kB"].Value) * 1024).ToSize(true, 2, 2),
                  (ulong.Parse(match["available_kB"].Value) * 1024).ToSize(true, 2, 2),
                  byte.Parse(match["usage_P"].Value),
                  match["path"].Value,
                  isMMC,
                  isEmulator)
        { }

        public void SetMmc()
        {
            Type = DriveType.Expansion;
        }
    }
}
