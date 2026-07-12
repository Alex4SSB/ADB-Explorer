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

        [GeneratedRegex(@"EXE SHA-256: (?<Hash>[0-9a-zA-Z]+)$", RegexOptions.Multiline)]
        public static partial Regex RE_ADB_LIST_HASH();

        [GeneratedRegex(@"(?:INSTALL_FAILED_INVALID_APK.*?)(?<package>com\.[\w.]+)(?:])")]
        public static partial Regex RE_PACKAGE_NAME();

        // versionCode / uid only appear with --show-versioncode / -U, so keep both groups optional
        [GeneratedRegex(@"package:(?<Path>\S+\.(?:apk|apex))=(?<Name>\S+)(?: versionCode:(?<Version>\d+))?(?: uid:(?<Uid>\d+))?")]
        public static partial Regex RE_PACKAGE_LISTING();

        [GeneratedRegex(@"(?:(?<!\d)(?<Date>\d{8})[^\d](?:(?<=[-_])(?<Time>\d{6}))?[^\d])|(?:(?<!\d)(?<DnT>\d{4}(?:[-_]\d{2}){5})[^\d])")]
        public static partial Regex RE_FILE_NAME_DATE();

        [GeneratedRegex(@"inet (?<IP>[\d.]+)")]
        public static partial Regex RE_DEVICE_WLAN_INET();

        [GeneratedRegex(@"^ *TCP +(?<IP>[\d.]+):(?<Port>[\d]+)", RegexOptions.Multiline)]
        public static partial Regex RE_NETSTAT_TCP_SOCK();

        [GeneratedRegex(@"^(?<Hash>\w+)[ -]+(?<Path>.+)$", RegexOptions.Multiline)]
        public static partial Regex RE_ANDROID_FIND_HASH();

        [GeneratedRegex(@"""*([^""]+\.exe)""*")]
        public static partial Regex RE_EXE_FROM_REG();

        [GeneratedRegex(@"^\w:\\$")]
        public static partial Regex RE_WINDOWS_DRIVE_ROOT();

        [GeneratedRegex(@"(?<Alias>[^=\s]+)='(?<Target>.+)'")]
        public static partial Regex RE_GET_ALIAS();

        [GeneratedRegex(@"[\d.]+")]
        public static partial Regex RE_GITHUB_VERSION();

        [GeneratedRegex(@"^\s*(?<Length>\d+)\s+(?<Method>\S+)\s+(?<Compressed>\d+)\s+(?<Ratio>\d+%)\s+(?<Date>[\d-]+ [\d:]+)\s+(?<Crc>[0-9a-f]+)\s+(?<Name>\S+)", RegexOptions.Multiline)]
        public static partial Regex RE_UNZIP_VERBOSE_ENTRY();

        [GeneratedRegex(@"^\s*(?<Length>\d+)\s+(?<Compressed>\d+)\s+(?<Ratio>\d+%)\s+(?<Count>\d+)\s+files?\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
        public static partial Regex RE_UNZIP_VERBOSE_SUMMARY();

        [GeneratedRegex(@"^(?<Mode>[\w-]+)\s+\S+\s+(?<Size>\d+)\s+(?<Date>[\d-]+ [\d:]+)\s+(?<Name>\S+)$")]
        public static partial Regex RE_TAR_LIST();

        [GeneratedRegex(@"^\s*versionName=(?<VersionName>.+?)\s*$", RegexOptions.Multiline)]
        public static partial Regex RE_DUMPSYS_VERSION_NAME();

        [GeneratedRegex(@"^\s*lastUpdateTime=(?<LastUpdateTime>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})\s*$", RegexOptions.Multiline)]
        public static partial Regex RE_DUMPSYS_LAST_UPDATE();

        [GeneratedRegex(@"(?<BlockDev>.+) on (?<MntPt>.+) type (?<Type>.+) \((?<Attr>.+)\)", RegexOptions.Multiline)]
        public static partial Regex RE_MOUNT_PARSE();

        [GeneratedRegex(@"\b([0-9a-fA-F]{8})\b")]
        public static partial Regex RE_SERVICE_CALL_WORD();

        /// <summary>Busybox/GNU style: <c>-r</c> followed by an Append description.</summary>
        [GeneratedRegex(@"-r\s+Append", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        public static partial Regex RE_TAR_APPEND_BUSYBOX();

        /// <summary>Toybox tabular help: <c>r</c> column with an Append description (no leading dash).</summary>
        [GeneratedRegex(@"(?m)^r\s{2,}Append\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        public static partial Regex RE_TAR_APPEND_TOYBOX();
    }
}
