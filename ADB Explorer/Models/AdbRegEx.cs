using System.Text.RegularExpressions;

namespace ADB_Explorer.Models
{
    public static class AdbRegEx
    {
        public static readonly Regex LS_FILE_ENTRY_RE =
            new(@"^(?<Mode>[0-9a-f]+) (?<Size>[0-9a-f]+) (?<Time>[0-9a-f]+) (?<Name>[^/]+?)\r?$",
                      RegexOptions.IgnoreCase);

        public static readonly Regex DEVICE_NAME_RE = new(@"^(?<id>[\w.:]+?) +.*device:(?<name>\w+)",
            RegexOptions.Multiline);

        public static readonly Regex PULL_PROGRESS_RE =
            new(@"^\[ *(?<TotalPrecentage>(?>\d+%|\?))\] (?<CurrentFile>.+?)(?>: (?<CurrentPrecentage>\d+%)|(?<CurrentBytes>\d+)\/\?)? *$",
                      RegexOptions.Multiline);

        public static readonly Regex PULL_STATS_RE =
            new(@"^(?<TargetPath>.+?): (?<TotalPulled>\d+) files? pulled, (?<TotalSkipped>\d+) skipped\.(?> (?<AverageRate>\d+(?>\.\d+)?) MB\/s \((?<TotalBytes>\d+) bytes in (?<TotalTime>\d+(?>\.\d+)?)s\))? *$",
                      RegexOptions.Multiline);
    }
}
