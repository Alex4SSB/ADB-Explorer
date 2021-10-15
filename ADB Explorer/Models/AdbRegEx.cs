using System.Text.RegularExpressions;

namespace ADB_Explorer.Models
{
    public static class AdbRegEx
    {
        public static readonly Regex LS_FILE_ENTRY_RE =
            new(@"^(?<Mode>[0-9a-f]+) (?<Size>[0-9a-f]+) (?<Time>[0-9a-f]+) (?<Name>[^/]+?)\r?$",
                      RegexOptions.IgnoreCase);

        public static readonly Regex DEVICE_NAME_RE = new(@"^(?<id>[\w.:-]+?) +(?<status>unauthorized|device|offline)(?: +.*(?:device:(?<name>\w+)))?",
            RegexOptions.Multiline);

        public static readonly Regex FILE_SYNC_PROGRESS_RE =
            new(@"^\[ *(?<TotalPercentage>(?>\d+%|\?))\] (?<CurrentFile>.+?)(?>: (?<CurrentPercentage>\d+%)|(?<CurrentBytes>\d+)\/\?)? *$",
                      RegexOptions.Multiline);

        public static readonly Regex FILE_SYNC_STATS_RE =
            new(@"^(?<TargetPath>.+?): (?<TotalPulled>\d+) files? (?>pulled|pushed), (?<TotalSkipped>\d+) skipped\.(?> (?<AverageRate>\d+(?>\.\d+)?) MB\/s \((?<TotalBytes>\d+) bytes in (?<TotalTime>\d+(?>\.\d+)?)s\))? *$",
                      RegexOptions.Multiline);

        public static readonly Regex EMULATED_STORAGE_SIZE =
            new(@"(?<size_kB>\d+)\s+(?<used_kB>\d+)\s+(?<available_kB>\d+)\s+(?<usage_P>\d+)%\s+(?<path>\/storage\/[\w-]+)");
    }
}
