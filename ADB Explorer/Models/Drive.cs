using ADB_Explorer.Converters;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using static ADB_Explorer.Models.AdbExplorerConst;

namespace ADB_Explorer.Models
{
    public enum DriveType
    {
        Root,
        Internal,
        Expansion,
        External,
        Unknown,
        Emulated,
        Trash,
    }

    public class Drive : INotifyPropertyChanged
    {
        private const sbyte usageWarningTh = 90;

        public string Size { get; private set; }
        public string Used { get; private set; }
        public string Available { get; private set; }
        public sbyte UsageP { get; private set; }
        public string Path { get; private set; }
        public string ID => Path[(Path.LastIndexOf('/') + 1)..];
        public string PrettyName => DRIVES_PRETTY_NAMES[Type];
        public bool UsageWarning => UsageP >= usageWarningTh;

        private DriveType type;
        public DriveType Type
        {
            get => type;
            private set
            {
                Set(ref type, value);
                OnPropertyChanged(nameof(DriveIcon));
                OnPropertyChanged(nameof(PrettyName));
            }
        }
        public string DriveIcon => Type switch
        {
            DriveType.Root => "\uF259",
            DriveType.Internal => "\uEDA2",
            DriveType.Expansion => "\uE7F1",
            DriveType.External => "\uE88E",
            DriveType.Unknown => "\uE9CE",
            DriveType.Emulated => "\uEDA2",
            DriveType.Trash => "\uE74D",
            _ => throw new System.NotImplementedException(),
        };

        private ulong recycledItemsCount;
        public ulong RecycledItemsCount
        {
            get => recycledItemsCount;
            set => Set(ref recycledItemsCount, value);
        }

        public Drive(string size = "", string used = "", string available = "", sbyte usageP = -1, string path = "", bool isMMC = false, bool isEmulator = false)
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

        public void SetMmc()
        {
            Type = DriveType.Expansion;
        }

        public void SetOtg()
        {
            Type = DriveType.External;
        }

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

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual bool Set<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(storage, value))
            {
                return false;
            }

            storage = value;
            OnPropertyChanged(propertyName);

            return true;
        }

        protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
