using ADB_Explorer.Converters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static ADB_Explorer.Models.AdbExplorerConst;

namespace ADB_Explorer.Models
{
    public class Drive
    {
        public string Size { get; private set; }
        public string Used { get; private set; }
        public string Available { get; private set; }
        public byte UsageP { get; private set; }
        public string Path { get; private set; }
        public string PrettyName { get; private set; }

        public Drive(string size, string used, string available, byte usageP, string path)
        {
            Size = size;
            Used = used;
            Available = available;
            UsageP = usageP;
            Path = path;

            PrettyName = path[(path.LastIndexOf('/') + 1)..];
        }

        public Drive(GroupCollection match)
            : this(
                  (ulong.Parse(match["size_kB"].Value) * 1000).ToSize(),
                  (ulong.Parse(match["used_kB"].Value) * 1000).ToSize(),
                  (ulong.Parse(match["available_kB"].Value) * 1000).ToSize(),
                  byte.Parse(match["usage_P"].Value),
                  match["path"].Value)
        { }

        public void SetPrettyName(string name)
        {
            PrettyName = name;
        }
    }
}
