using System.Text.RegularExpressions;

namespace ADB_Explorer.Models
{
    public static class AdbRegEx
    {
        public static readonly Regex LS_FILE_ENTRY_RE =
            new(@"^(?<Mode>[0-9a-f]+) (?<Size>[0-9a-f]+) (?<Time>[0-9a-f]+) (?<Name>[^/]+?)\r?$",
                      RegexOptions.IgnoreCase);

        public static readonly Regex DEVICE_NAME_RE = new(@"^(?<id>[\w.:-]+?) +(?<status>unauthorized|device|offline|authorizing|recovery)(?: +.*(?:model:(?<model>\w+)))?(?: +.*(?:device:(?<device>\w+)))?",
            RegexOptions.Multiline);

        public static readonly Regex FILE_SYNC_PROGRESS_RE =
            new(@"^\[ *(?<TotalPercentage>(?>\d+%|\?))\] (?<CurrentFile>.+?)(?>: (?<CurrentPercentage>\d+%)|(?<CurrentBytes>\d+)\/\?)? *$",
                      RegexOptions.Multiline);

        public static readonly Regex FILE_SYNC_STATS_RE =
            new(@"^(?<TargetPath>.+?): (?<TotalTransferred>\d+) files? (?>pulled|pushed), (?<TotalSkipped>\d+) skipped\.(?> (?<AverageRate>\d+(?>\.\d+)?) MB\/s \((?<TotalBytes>\d+) bytes in (?<TotalTime>\d+(?>\.\d+)?)s\))? *$",
                      RegexOptions.Multiline);

        public static readonly Regex EMULATED_STORAGE_SINGLE =
            new(@"(?<size_kB>\d+)\s+(?<used_kB>\d+)\s+(?<available_kB>\d+)\s+(?<usage_P>\d+)%\s+(?<path>.*?)[\r\n]");

        public static readonly Regex EMULATED_ONLY =
            new(@"(?<size_kB>\d+)\s+(?<used_kB>\d+)\s+(?<available_kB>\d+)\s+(?<usage_P>\d+)%\s+(?<path>\/(?:storage|mnt\/media_rw)\/[\w-]+?)[\r\n]",
                RegexOptions.Multiline);

        public static readonly Regex MMC_BLOCK_DEVICE_NODE =
            new(@"(?<major>[a-f\d]+),(?<minor>[a-f\d]+)");

        public static readonly Regex MDNS_SERVICE =
            new(@"(?<ID>[^\s]+)\t*_adb-tls-(?<PortType>pairing|connect)\._tcp\.*\t*(?<IpAddress>[^:]+):(?<Port>\d+)");

        public static readonly Regex ADB_VERSION =
            new(@"^Version[\t ]*(?<version>[\d.]+)", RegexOptions.Multiline);

        public static readonly Regex PACKAGE_NAME =
            new(@"(?:INSTALL_FAILED_INVALID_APK.*?)(?<package>com\.[\w.]+)(?:])");
    }
}
