using System.Text.RegularExpressions;

namespace ADB_Explorer.Models
{
    public static class AdbRegEx
    {
        public static readonly Regex RE_LS_FILE_ENTRY =
            new(@"^(?<Mode>[0-9a-f]+) (?<Size>[0-9a-f]+) (?<Time>[0-9a-f]+) (?<Name>[^/]+?)\r?$",
                RegexOptions.IgnoreCase);

        public static readonly Regex RE_DEVICE_NAME =
            new(@"^(?<id>[\w.:-]+?) +(?<status>unauthorized|device|offline|authorizing|recovery|sideload)(?: +.*(?:model:(?<model>\w+)))?(?: +.*(?:device:(?<device>\w+)))?",
                RegexOptions.Multiline);

        public static readonly Regex RE_FILE_SYNC_PROGRESS =
            new(@"^\[ *(?<TotalPercentage>(?>\d+%|\?))\] (?<CurrentFile>.+?)(?>: (?<CurrentPercentage>\d+%)|(?<CurrentBytes>\d+)\/\?)? *$",
                RegexOptions.Multiline);

        public static readonly Regex RE_FILE_SYNC_ERROR =
            new(@"^adb: error: (?<Message>(?:(?:'(?<WindowsPath>\w:(?:\\+[^\/']+)+)')|(?: (?<WindowsPath1>\w:(?:\\+[^\/:]+)+): )|(?:'(?<AndroidPath>(?:\/+[^']+)+)')|(?: (?<AndroidPath1>(?:\/+[^:]+)+): )|(?:.+?))*?) *$",
                RegexOptions.Multiline);

        public static readonly Regex RE_SHELL_ERROR =
            new(@"^\w+: (?<Message>(?<AndroidPath>[^':]+):.*)$",
                RegexOptions.Multiline);

        public static readonly Regex RE_FILE_SYNC_STATS =
            new(@"^(?<SourcePath>.+?): (?<TotalTransferred>\d+) files? (?>pulled|pushed), (?<TotalSkipped>\d+) skipped\.(?> (?<AverageRate>\d+(?>\.\d+)?) MB\/s \((?<TotalBytes>\d+) bytes in (?<TotalTime>\d+(?>\.\d+)?)s\))? *$",
                RegexOptions.Multiline);

        public static readonly Regex RE_EMULATED_STORAGE_SINGLE =
            new(@"(?<size_kB>\d+)\s+(?<used_kB>\d+)\s+(?<available_kB>\d+)\s+(?<usage_P>\d+)%\s+(?<path>.*?)[\r\n]");

        public static readonly Regex RE_EMULATED_ONLY =
            new(@"(?<size_kB>\d+)\s+(?<used_kB>\d+)\s+(?<available_kB>\d+)\s+(?<usage_P>\d+)%\s+(?<path>\/(?:storage|mnt\/media_rw)\/[\w-]+?)[\r\n]",
                RegexOptions.Multiline);

        public static readonly Regex RE_MMC_BLOCK_DEVICE_NODE =
            new(@"(?<major>[a-f\d]+),(?<minor>[a-f\d]+)");

        public static readonly Regex RE_MDNS_SERVICE =
            new(@"(?<ID>[^\s]+)\t*_adb-tls-(?<PortType>pairing|connect)\._tcp\.*\t*(?<IpAddress>[^:]+):(?<Port>\d+)");

        public static readonly Regex RE_ADB_VERSION =
            new(@"^Version[\t ]*(?<version>[\d.]+)", RegexOptions.Multiline);

        public static readonly Regex RE_PACKAGE_NAME =
            new(@"(?:INSTALL_FAILED_INVALID_APK.*?)(?<package>com\.[\w.]+)(?:])");

        public static readonly Regex RE_PACKAGE_LISTING =
            new(@"(?:package:)(?<package>[\w.]+)(?: versionCode:(?<version>[\d]+))*(?: uid:(?<uid>[\d]+))*");

        public static readonly Regex RE_FILE_NAME_DATE =
            new(@"(?:(?<!\d)(?<Date>\d{8})[^\d](?:(?<=[-_])(?<Time>\d{6}))?[^\d])|(?:(?<!\d)(?<DnT>\d{4}(?:[-_]\d{2}){5})[^\d])");

        public static readonly Regex RE_DEVICE_WLAN_INET =
            new(@"inet (?<IP>[\d.]+)");

        public static readonly Regex RE_NETSTAT_TCP_SOCK =
            new(@"^ *TCP +(?<IP>[\d.]+):(?<Port>[\d]+)", RegexOptions.Multiline);
    }
}
