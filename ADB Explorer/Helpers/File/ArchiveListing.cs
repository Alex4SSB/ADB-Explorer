using ADB_Explorer.Models;
using ADB_Explorer.Services;
using static ADB_Explorer.Models.AbstractFile;
using static ADB_Explorer.Models.AdbRegEx;

namespace ADB_Explorer.Helpers;

public readonly record struct ArchiveEntry(
    string Path,
    bool IsDirectory,
    long Size,
    DateTime? Modified,
    long? CompressedSize = null,
    string? Method = null,
    string? Ratio = null,
    string? Crc = null,
    UnixFileMode? Permissions = null);

public readonly record struct ArchiveSummary(long UncompressedSize, long CompressedSize, string Ratio, int FileCount);

public readonly record struct ArchiveToc(IReadOnlyList<ArchiveEntry> Entries, ArchiveSummary? Summary);

public static class ArchiveListing
{
    private static readonly ConcurrentDictionary<string, ArchiveToc> TocCache = new(StringComparer.Ordinal);

    public static bool TryGetArchiveSummary(string archivePath, out ArchiveSummary summary)
    {
        if (TocCache.TryGetValue(archivePath, out var toc) && toc.Summary is { } parsed)
        {
            summary = parsed;
            return true;
        }

        summary = default;
        return false;
    }

    public static IReadOnlyList<ArchiveEntry> ParseListing(string stdout, ArchiveFamily family)
        => ParseToc(stdout, family).Entries;

    public static ArchiveToc ParseToc(string stdout, ArchiveFamily family)
        => family switch
        {
            ArchiveFamily.Tar => new(ParseTar(stdout), null),
            ArchiveFamily.Zip => ParseZip(stdout),
            _ => new([], null),
        };

    public static ArchiveToc FetchTableOfContents(string deviceId, string archivePath, CancellationToken cancellationToken)
    {
        var family = ArchiveHelper.GetFamily(archivePath);
        if (family is ArchiveFamily.None)
            return new([], null);

        var tar = ShellCommands.TranslateCommand("tar");
        var unzip = ShellCommands.TranslateCommand("unzip");

        string stdout = "";
        string stderr = "";

        var exitCode = family switch
        {
            ArchiveFamily.Tar => ADBService.ExecuteDeviceAdbShellCommand(
                deviceId, tar, out stdout, out stderr, cancellationToken, "-tvf", ADBService.EscapeAdbShellString(archivePath)),
            ArchiveFamily.Zip => ADBService.ExecuteDeviceAdbShellCommand(
                deviceId, unzip, out stdout, out stderr, cancellationToken, "-lv", ADBService.EscapeAdbShellString(archivePath)),
            _ => throw new InvalidOperationException(),
        };

        if (exitCode != 0)
            throw new IOException(string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);

        return ParseToc(stdout, family);
    }

    public static IEnumerable<FileStat> GetFileStats(string archivePath, string internalPath, IReadOnlyList<ArchiveEntry> entries)
    {
        internalPath = ArchivePath.NormalizeInternal(internalPath);
        var prefix = string.IsNullOrEmpty(internalPath) ? "" : internalPath + "/";
        var children = new Dictionary<string, FileStat>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            var path = entry.Path;

            if (!string.IsNullOrEmpty(prefix))
            {
                if (!path.StartsWith(prefix, StringComparison.Ordinal))
                    continue;

                path = path[prefix.Length..];
            }

            if (string.IsNullOrEmpty(path))
                continue;

            var slash = path.IndexOf('/');
            var name = slash < 0 ? path : path[..slash];
            var isFolder = entry.IsDirectory || slash >= 0;

            if (children.ContainsKey(name))
            {
                if (isFolder)
                    children[name] = children[name] with { Type = FileType.Folder, Size = null };

                continue;
            }

            var childInternal = string.IsNullOrEmpty(internalPath)
                ? name
                : $"{internalPath}/{name}";

            children[name] = new FileStat(
                FullName: name,
                FullPath: ArchivePath.Join(archivePath, childInternal),
                Type: isFolder ? FileType.Folder : FileType.File,
                IsLink: false,
                Size: entry.IsDirectory ? null : entry.Size,
                ModifiedTime: entry.Modified,
                Permissions: entry.Permissions,
                CompressedSize: entry.IsDirectory ? null : entry.CompressedSize,
                CompressionMethod: entry.IsDirectory ? null : entry.Method,
                CompressionRatio: entry.IsDirectory ? null : entry.Ratio,
                Crc32: entry.IsDirectory ? null : entry.Crc);
        }

        return children.Values
            .OrderBy(e => e.Type is not FileType.Folder)
            .ThenBy(e => e.FullName, StringComparer.OrdinalIgnoreCase);
    }

    public static IEnumerable<FileStat> ListEntries(string deviceId, string archivePath, string internalPath, CancellationToken cancellationToken)
    {
        var toc = TocCache.GetOrAdd(archivePath, key => FetchTableOfContents(deviceId, key, cancellationToken));
        return GetFileStats(archivePath, internalPath, toc.Entries);
    }

    private static List<ArchiveEntry> ParseTar(string stdout)
    {
        var result = new List<ArchiveEntry>();
        foreach (var line in stdout.Split(ADBService.LINE_SEPARATORS, StringSplitOptions.RemoveEmptyEntries))
        {
            var match = RE_TAR_LIST().Match(line);
            if (!match.Success)
                continue;

            var rawName = match.Groups["Name"].Value;
            var mode = match.Groups["Mode"].Value;
            var isDirectory = mode[0] is 'd' || rawName.EndsWith('/');

            long.TryParse(match.Groups["Size"].Value, out var size);
            DateTime? modified = DateTime.TryParseExact(match.Groups["Date"].Value, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) 
                ? dt : null;
            var permissions = ParseTarMode(mode);

            result.Add(new ArchiveEntry(rawName.TrimEnd('/'), isDirectory, size, modified, Permissions: permissions));
        }

        return result;
    }

    private static ArchiveToc ParseZip(string stdout)
    {
        var result = new List<ArchiveEntry>();
        ArchiveSummary? summary = null;

        string lastLine = "";
        foreach (var rawLine in stdout.Split(ADBService.LINE_SEPARATORS, StringSplitOptions.RemoveEmptyEntries))
        {
            lastLine = rawLine;
            
            var match = RE_UNZIP_VERBOSE_ENTRY().Match(rawLine);
            if (!match.Success)
                continue;

            var rawName = match.Groups["Name"].Value;
            var isDirectory = rawName.EndsWith('/');

            long.TryParse(match.Groups["Length"].Value, out var size);
            long.TryParse(match.Groups["Compressed"].Value, out var compressed);
            var method = match.Groups["Method"].Value;
            var ratio = match.Groups["Ratio"].Value;
            var crc = match.Groups["Crc"].Value;
            var dateText = match.Groups["Date"].Value;
            DateTime? modified = DateTime.TryParseExact(dateText, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) 
                ? dt 
                : null;

            result.Add(new ArchiveEntry(
                rawName.TrimEnd('/'),
                isDirectory,
                size,
                modified,
                compressed,
                method,
                ratio,
                crc));
        }

        var summaryMatch = RE_UNZIP_VERBOSE_SUMMARY().Match(lastLine);
        if (!summaryMatch.Success)
            summaryMatch = RE_UNZIP_VERBOSE_SUMMARY().Match(stdout);

        if (summaryMatch.Success)
        {
            long.TryParse(summaryMatch.Groups["Length"].Value, out var uncompressed);
            long.TryParse(summaryMatch.Groups["Compressed"].Value, out var compressedTotal);
            int.TryParse(summaryMatch.Groups["Count"].Value, out var fileCount);
            
            summary = new ArchiveSummary(
                uncompressed,
                compressedTotal,
                summaryMatch.Groups["Ratio"].Value,
                fileCount);
        }

        return new(result, summary);
    }

    private static UnixFileMode? ParseTarMode(string mode)
    {
        if (mode.Length < 10)
            return null;

        UnixFileMode result = 0;

        if (mode[1] is 'r') result |= UnixFileMode.UserRead;
        if (mode[2] is 'w') result |= UnixFileMode.UserWrite;
        if (mode[3] is 'x' or 's') result |= UnixFileMode.UserExecute;

        if (mode[4] is 'r') result |= UnixFileMode.GroupRead;
        if (mode[5] is 'w') result |= UnixFileMode.GroupWrite;
        if (mode[6] is 'x' or 's') result |= UnixFileMode.GroupExecute;

        if (mode[7] is 'r') result |= UnixFileMode.OtherRead;
        if (mode[8] is 'w') result |= UnixFileMode.OtherWrite;
        if (mode[9] is 'x' or 't') result |= UnixFileMode.OtherExecute;

        return result;
    }
}
