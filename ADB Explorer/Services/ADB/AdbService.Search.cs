using static ADB_Explorer.Models.AbstractFile;
using static ADB_Explorer.Models.AdbExplorerConst;

namespace ADB_Explorer.Services;

public partial class AdbService
{
    // find /sdcard/.Trash-AdbExplorer/ -maxdepth 1 -mindepth 1 \( -iname "\*" ! -iname ".RecycleIndex" ! -iname ".RecycleIndex.bak" \) 2>/dev/null | wc -l
    // Exclude the recycle folder, exclude content of sub-folders, include all files (including hidden), exclude the recycle index file, discard errors, count lines
    private static readonly string[] FIND_COUNT_PARAMS_1 = ["-maxdepth", "1", "-mindepth", "1", "\\("];

    private static readonly string[] FIND_COUNT_PARAMS_2 = ["\\)", @"2>/dev/null"];

    private static readonly string[] FIND_COUNT_PARAMS_3 = ["|", "wc", "-l"];

    public static ulong CountFiles(string deviceID, string path, IEnumerable<string>? includeNames = null, IEnumerable<string>? excludeNames = null)
    {
        string[] args = PrepFindArgs(path, includeNames, excludeNames, true);

        ExecuteDeviceAdbShellCommand(deviceID, "find", out string stdout, out _, CancellationToken.None, args);

        return ulong.TryParse(stdout, out var count) ? count : 0;
    }

    public static string[] FindFilesInPath(string deviceID, string path, IEnumerable<string>? includeNames = null, IEnumerable<string>? excludeNames = null, bool caseSensitive = false)
    {
        string[] args = PrepFindArgs(path, includeNames, excludeNames, false, caseSensitive);

        ExecuteDeviceAdbShellCommand(deviceID, "find", out string stdout, out _, CancellationToken.None, args);

        return stdout.Split(LINE_SEPARATORS, StringSplitOptions.RemoveEmptyEntries);
    }

    public static IEnumerable<FileStat> SearchResultsStreaming(string deviceID, string path, string query, CancellationToken cancellationToken, bool caseSensitive = false)
    {
        string[] args = PrepSearchArgs(deviceID, path, query, caseSensitive);
        if (args.Length == 0)
            yield break;

        var actualCmd = ShellCommands.TranslateCommand("find");
        foreach (var line in ExecuteDeviceAdbCommandAsync(deviceID, "shell", cancellationToken, [actualCmd, ..args]))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (ParseSearchResultLine(line) is { } fileStat && !IsWithinRecycleBin(fileStat.FullPath))
                yield return fileStat;
        }
    }

    private static bool IsWithinRecycleBin(string path) =>
        POSSIBLE_RECYCLE_PATHS.Any(trashPath =>
            path.Equals(trashPath, StringComparison.Ordinal)
            || path.StartsWith(trashPath + "/", StringComparison.Ordinal));

    /// <summary>Recursively finds files under <paramref name="path"/> matching any of <paramref name="includeNames"/>
    /// (case-insensitive glob). Unlike <see cref="FindFilesInPath"/>, this descends the whole subtree.</summary>
    public static IEnumerable<string> FindFilesRecursive(string deviceID, string path, IEnumerable<string> includeNames, CancellationToken cancellationToken)
    {
        var names = includeNames.ToArray();
        if (names.Length == 0)
            yield break;

        if (!path.EndsWith('/'))
            path += "/";

        var nameClauses = string.Join(" -o ", names.Select(f => $"-iname {EscapeAdbShellString(f)}"));
        string[] args = [EscapeAdbShellString(path), "\\(", nameClauses, "\\)", "2>/dev/null"];

        var actualCmd = ShellCommands.TranslateCommand("find");
        foreach (var line in ExecuteDeviceAdbCommandAsync(deviceID, "shell", cancellationToken, [actualCmd, .. args]))
        {
            if (!string.IsNullOrWhiteSpace(line))
                yield return line.Trim();
        }
    }

    public static IEnumerable<string> SearchPathsStreaming(string deviceID, string path, string query, CancellationToken cancellationToken, bool caseSensitive = false)
    {
        foreach (var result in SearchResultsStreaming(deviceID, path, query, cancellationToken, caseSensitive))
            yield return result.FullPath;
    }

    /// <summary>Finds files under <paramref name="path"/> whose contents match <paramref name="query"/> (literal).
    /// Archive files (<see cref="ArchiveHelper.ArchiveExcludeGlobs"/>) are always excluded.</summary>
    public static IEnumerable<FileStat> SearchContentsStreaming(string deviceID, string path, string query, bool recursive, CancellationToken cancellationToken, bool caseSensitive = false)
    {
        string[] matchArgs = PrepContentSearchArgs(path, query, recursive, caseSensitive);
        if (matchArgs.Length == 0)
            yield break;

        var matchCmd = recursive ? ShellCommands.TranslateCommand("grep") : ShellCommands.TranslateCommand("find");

        List<string> matches = [];
        foreach (var line in ExecuteDeviceAdbCommandAsync(deviceID, "shell", cancellationToken, [matchCmd, .. matchArgs]))
        {
            if (!string.IsNullOrWhiteSpace(line) && !IsWithinRecycleBin(line))
                matches.Add(line.Trim());
        }

        if (matches.Count == 0)
            yield break;

        var statCmd = ShellCommands.TranslateCommand("find");
        var statArgs = PrepStatArgs(deviceID, matches);
        foreach (var line in ExecuteDeviceAdbCommandAsync(deviceID, "shell", cancellationToken, [statCmd, .. statArgs]))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (ParseSearchResultLine(line) is { } fileStat)
                yield return fileStat;
        }
    }

    /// <summary>
    /// Determines which of the specified file paths exist on a device identified by the given device ID.
    /// </summary>
    /// <remarks>This method executes a shell command on the device to check for the existence of the provided
    /// paths. Any errors encountered during command execution are ignored.</remarks>
    /// <param name="deviceID">The unique identifier of the device on which to check the existence of the file paths.</param>
    /// <param name="paths">An enumerable collection of file paths to verify for existence on the specified device.</param>
    /// <returns>An array of strings containing the paths that exist on the device. The array is empty if none of the specified
    /// paths exist.</returns>
    public static string[] PathsExist(string deviceID, params IEnumerable<string> paths)
    {
        ExecuteDeviceAdbShellCommand(deviceID,
                                     "find",
                                     out string stdout,
                                     out _,
                                     CancellationToken.None,
                                     [.. paths.Select(item => EscapeAdbShellString(item)), "-maxdepth 0", @"2>/dev/null"]);

        return stdout.Split(LINE_SEPARATORS, StringSplitOptions.RemoveEmptyEntries);
    }

    private static string[] PrepFindArgs(string path, IEnumerable<string>? includeNames, IEnumerable<string>? excludeNames, bool countOnly, bool caseSensitive = false)
    {
        if (includeNames is not null && excludeNames is not null)
            throw new ArgumentException("""
                Valid combinations include:
                • includeNames = null, excludeNames = null
                • includeNames != null, excludeNames = null
                • includeNames = null, excludeNames != null
                """);

        if (!path.EndsWith('/'))
            path += "/";

        var nameArg = caseSensitive ? "-name" : "-iname";

        string[] args = [EscapeAdbShellString(path), .. FIND_COUNT_PARAMS_1];

        if (includeNames is null)
            args = [.. args, nameArg, "\"\\*\""];
        else
            args = [.. args, string.Join(" -o ", includeNames.Select(f => $"{nameArg} {EscapeAdbShellString(f)}"))];

        if (excludeNames is not null)
            args = [.. args, string.Join(' ', excludeNames.Select(f => $"! {nameArg} {EscapeAdbShellString(f)}"))];

        args = [.. args, .. FIND_COUNT_PARAMS_2];
        if (countOnly)
            args = [.. args, .. FIND_COUNT_PARAMS_3];

        return args;
    }

    private static string[] PrepSearchArgs(string deviceID, string path, string query, bool caseSensitive = false)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        if (!path.EndsWith('/'))
            path += "/";

        var nameArg = caseSensitive ? "-name" : "-iname";
        var trimmedQuery = query.Trim();
        var pattern = FileHelper.ContainsWildcard(trimmedQuery) ? trimmedQuery : $"*{EscapeFindPattern(trimmedQuery)}*";
        var lineEnd = $"\\n'";

        if (ShellCommands.FindPrintf(deviceID))
        {
            var dirPrintf = $"'%p{ADB_FIELD_SEP}d{ADB_FIELD_SEP}d{ADB_FIELD_SEP}{lineEnd}";
            var filePrintf = $"'%p{ADB_FIELD_SEP}%s{ADB_FIELD_SEP}%T@{ADB_FIELD_SEP}{lineEnd}";
            var linkPrintf = $"'%p{ADB_FIELD_SEP}l{ADB_FIELD_SEP}%T@{ADB_FIELD_SEP}{lineEnd}";

            return
            [
                EscapeAdbShellString(path),
                "\\(",
                nameArg,
                EscapeAdbShellString(pattern),
                "\\(",
                "-type", "d", "-printf", dirPrintf,
                "-o", "-type", "f", "-printf", filePrintf,
                "-o", "-type", "l", "-printf", linkPrintf,
                "\\)",
                "\\)",
                @"2>/dev/null",
            ];
        }

        var stat = ShellCommands.TranslateCommand("stat");

        return
        [
            EscapeAdbShellString(path),
            "\\(",
            nameArg,
            EscapeAdbShellString(pattern),
            "\\)",
            "2>/dev/null | while IFS= read -r f;",
            "do if [ -d \\\"$f\\\" ]; then",
            $"echo \\\"$f{ADB_FIELD_SEP}d{ADB_FIELD_SEP}d{ADB_FIELD_SEP}\\\";",
            "elif [ -L \\\"$f\\\" ]; then",
            $"echo \\\"$f{ADB_FIELD_SEP}l{ADB_FIELD_SEP}$({stat} -c '%Y' \\\"$f\\\"){ADB_FIELD_SEP}\\\";",
            "else",
            $"echo \\\"$f{ADB_FIELD_SEP}$({stat} -c '%s{ADB_FIELD_SEP}%Y' \\\"$f\\\"){ADB_FIELD_SEP}\\\";",
            "fi; done;",
        ];
    }

    private static string[] PrepContentSearchArgs(string path, string query, bool recursive, bool caseSensitive = false)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        if (!path.EndsWith('/'))
            path += "/";

        string[] caseArg = caseSensitive ? [] : ["-i"];
        string[] patternArgs = ["--", EscapeAdbShellString(query)];

        if (recursive)
        {
            // Short "-S PATTERN" does not take its value on-device (verified on-emulator); long form does.
            var excludeArgs = ArchiveHelper.ArchiveExcludeGlobs.Select(glob => $"--exclude={EscapeAdbShellString(glob)}");

            return
            [
                "-r", "-l", "-I", "-F",
                .. caseArg,
                .. excludeArgs,
                .. patternArgs,
                EscapeAdbShellString(path),
                "2>/dev/null",
            ];
        }

        // grep has no shallow-recursion flag; enumerate depth-1 files via find and pipe them through xargs.
        var excludeFind = ArchiveHelper.ArchiveExcludeGlobs.SelectMany(glob => new[] { "-not", "-iname", EscapeAdbShellString(glob) });
        var grep = ShellCommands.TranslateCommand("grep");

        return
        [
            EscapeAdbShellString(path), "-maxdepth", "1", "-type", "f",
            .. excludeFind,
            "-print0", "2>/dev/null", "|", "xargs", "-0", grep, "-l", "-I", "-F",
            .. caseArg,
            .. patternArgs,
        ];
    }

    /// <summary>Fetches type/size/mtime for a known, explicit list of device paths (used to stat grep's match output).</summary>
    private static string[] PrepStatArgs(string deviceID, IEnumerable<string> paths)
    {
        var lineEnd = $"\\n'";
        var pathArgs = paths.Select(p => EscapeAdbShellString(p));

        if (ShellCommands.FindPrintf(deviceID))
        {
            var filePrintf = $"'%p{ADB_FIELD_SEP}%s{ADB_FIELD_SEP}%T@{ADB_FIELD_SEP}{lineEnd}";
            return [.. pathArgs, "-maxdepth", "0", "-printf", filePrintf, "2>/dev/null"];
        }

        var stat = ShellCommands.TranslateCommand("stat");

        return
        [
            .. pathArgs,
            "-maxdepth", "0",
            "2>/dev/null | while IFS= read -r f;",
            "do",
            $"echo \\\"$f{ADB_FIELD_SEP}$({stat} -c '%s{ADB_FIELD_SEP}%Y' \\\"$f\\\"){ADB_FIELD_SEP}\\\";",
            "done;",
        ];
    }

    private static FileStat? ParseSearchResultLine(string line)
    {
        var parts = line.Split(ADB_FIELD_SEP, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3)
            return null;

        var fullPath = parts[0];
        var name = FileHelper.GetFullName(fullPath);
        var isDirectory = parts[1] == "d";
        var isLink = parts[1] == "l";

        long? size = null;
        if (!isDirectory && !isLink && long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedSize))
            size = parsedSize;

        DateTime? modifiedTime = null;
        if (parts[2] != "d"
            && double.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out var unixTime))
        {
            modifiedTime = DateTimeOffset.FromUnixTimeSeconds((long)unixTime).LocalDateTime;
        }

        var type = isDirectory ? FileType.Folder
            : isLink ? FileType.Unknown
            : FileType.File;

        return new(name, fullPath, type, isLink, size, modifiedTime, null);
    }

    private static string EscapeFindPattern(string query) =>
        string.Concat(query.Select(c =>
            c switch
            {
                '\\' => @"\\",
                '*' => @"\*",
                '?' => @"\?",
                '[' => @"\[",
                _ => new string(c, 1)
            }));

    public static long CountRecycle(string deviceID)
    {
        return (long)CountFiles(deviceID, RECYCLE_PATH, excludeNames: ["*" + RECYCLE_INDEX_SUFFIX]);
    }

    public static ulong CountPackages(string deviceID)
    {
        return CountFiles(deviceID, TEMP_PATH, includeNames: INSTALL_APK.Select(name => "*" + name));
    }

    private static string BuildFindCountCommand(string path, IEnumerable<string>? includeNames = null, IEnumerable<string>? excludeNames = null)
        => "find " + string.Join(' ', PrepFindArgs(path, includeNames, excludeNames, countOnly: true));
}
