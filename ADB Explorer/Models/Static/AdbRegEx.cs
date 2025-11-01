namespace ADB_Explorer.Models
{
    public static partial class AdbRegEx
    {
        [GeneratedRegex(@"^(?<Mode>[0-9a-f]+) (?<Size>[0-9a-f]+) (?<Time>[0-9a-f]+) (?<Name>[^/]+?)\r?$", RegexOptions.IgnoreCase)]
        public static partial Regex RE_LS_FILE_ENTRY();

        [GeneratedRegex(@"^(?<id>[\w.:-]+?) +(?<status>unauthorized|device|offline|authorizing|recovery|sideload)(?: +.*(?:model:(?<model>\w+)))?(?: +.*(?:device:(?<device>\w+)))?[^\r\n]*", RegexOptions.Multiline)]
        public static partial Regex RE_DEVICE_NAME();

        [GeneratedRegex(@"^\w+: (?<Message>(?<AndroidPath>[^':]+):.*)$", RegexOptions.Multiline)]
        public static partial Regex RE_SHELL_ERROR();

        [GeneratedRegex(@"(?<FileSystem>[\w\/]+)\s+(?<size_kB>\d+)\s+(?<used_kB>\d+)\s+(?<available_kB>\d+)\s+(?<usage_P>\d+)%\s+(?<path>.*?)[\r\n]")]
        public static partial Regex RE_EMULATED_STORAGE_SINGLE();

        [GeneratedRegex(@"(?<FileSystem>[\w\/]+)\s+(?<size_kB>\d+)\s+(?<used_kB>\d+)\s+(?<available_kB>\d+)\s+(?<usage_P>\d+)%\s+(?<path>\/(?:storage|mnt\/media_rw)\/[\w-]+?)[\r\n]", RegexOptions.Multiline)]
        public static partial Regex RE_EMULATED_ONLY();

        [GeneratedRegex(@"(?<major>[a-f\d]+),(?<minor>[a-f\d]+)")]
        public static partial Regex RE_MMC_BLOCK_DEVICE_NODE();

        [GeneratedRegex(@"(?<ID>[^\s]+)\t*_adb-tls-(?<PortType>pairing|connect)\._tcp\.*\t*(?<IpAddress>[^:]+):(?<Port>\d+)")]
        public static partial Regex RE_MDNS_SERVICE();

        [GeneratedRegex(@"^Version[\t ]*(?<version>[\d.]+)[\s\S]*^Installed as (?<Path>.+)$", RegexOptions.Multiline)]
        public static partial Regex RE_ADB_VERSION();

        [GeneratedRegex(@"(?:INSTALL_FAILED_INVALID_APK.*?)(?<package>com\.[\w.]+)(?:])")]
        public static partial Regex RE_PACKAGE_NAME();

        [GeneratedRegex(@"(?:package:)(?<package>[\w.]+)(?: versionCode:(?<version>[\d]+))*(?: uid:(?<uid>[\d]+))*")]
        public static partial Regex RE_PACKAGE_LISTING();

        [GeneratedRegex(@"(?:(?<!\d)(?<Date>\d{8})[^\d](?:(?<=[-_])(?<Time>\d{6}))?[^\d])|(?:(?<!\d)(?<DnT>\d{4}(?:[-_]\d{2}){5})[^\d])")]
        public static partial Regex RE_FILE_NAME_DATE();

        [GeneratedRegex(@"inet (?<IP>[\d.]+)")]
        public static partial Regex RE_DEVICE_WLAN_INET();

        [GeneratedRegex(@"^ *TCP +(?<IP>[\d.]+):(?<Port>[\d]+)", RegexOptions.Multiline)]
        public static partial Regex RE_NETSTAT_TCP_SOCK();

        [GeneratedRegex(@"^(?<Hash>\w+)[ -]+(?<Path>.+)$", RegexOptions.Multiline)]
        public static partial Regex RE_ANDROID_FIND_HASH();

        [GeneratedRegex("\\/\\/\\/ (?<Source>[\\s\\S]+?) \\/\\/\\/(?:(?: )|(?: (?<Target>\\/[\\s\\S]+?) ))\\/\\/\\/", RegexOptions.Multiline)]
        public static partial Regex RE_LINK_TARGETS();

        [GeneratedRegex("\\/\\/\\/ (?<Target>[\\s\\S]+?) \\/\\/\\/ (?<Mode>.*) \\/\\/\\/", RegexOptions.Multiline)]
        public static partial Regex RE_LINK_MODE();

        [GeneratedRegex("\\/\\/\\/ (?<Name>[\\s\\S]+?) \\/\\/\\/ (?<Size>.*) \\/\\/\\/ (?<Date>.*) \\/\\/\\/", RegexOptions.Multiline)]
        public static partial Regex RE_FIND_TREE();

        [GeneratedRegex(@"""*([^""]+\.exe)""*")]
        public static partial Regex RE_EXE_FROM_REG();

        [GeneratedRegex(@"^\w:\\$")]
        public static partial Regex RE_WINDOWS_DRIVE_ROOT();

        [GeneratedRegex(@"(?<Alias>[^=\s]+)='(?<Target>.+)'")]
        public static partial Regex RE_GET_ALIAS();

        [GeneratedRegex(@"[\d.]+")]
        public static partial Regex RE_GITHUB_VERSION();

        [GeneratedRegex(@" +(?<Size>\d+) +(?<Date>\d{4}-\d\d-\d\d \d\d:\d\d) +(?<Name>.+)")]
        public static partial Regex RE_UNZIP_LIST();
    }
}
