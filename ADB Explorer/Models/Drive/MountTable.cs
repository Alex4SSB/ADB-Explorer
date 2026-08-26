namespace ADB_Explorer.Models;

/// <summary>
/// Snapshot of <c>mount</c> output. Later entries at the same mount point win (overlay on top of lower).
/// </summary>
public sealed class MountTable
{
    public static MountTable Empty { get; } = new([]);

    private readonly FileSystemInfo[] _entries;

    public MountTable(IReadOnlyList<FileSystemInfo> entries)
        => _entries = entries as FileSystemInfo[] ?? [.. entries];

    public bool IsEmpty => _entries.Length == 0;

    public IReadOnlyList<FileSystemInfo> Entries => _entries;

    public static MountTable Parse(string? stdout)
    {
        if (string.IsNullOrEmpty(stdout))
            return Empty;

        var entries = new List<FileSystemInfo>();
        foreach (Match match in AdbRegEx.RE_MOUNT_PARSE().Matches(stdout))
        {
            entries.Add(new(
                BlockDev: match.Groups["BlockDev"].Value,
                MountPoint: match.Groups["MntPt"].Value,
                FileSystemType: match.Groups["Type"].Value,
                Options: match.Groups["Attr"].Value.Split(',')));
        }

        return entries.Count == 0 ? Empty : new(entries);
    }

    /// <summary>
    /// Longest mount point that covers <paramref name="path"/>. Equal-length ties keep the last entry.
    /// </summary>
    /// <param name="includeRoot">
    /// When false, skip the <c>/</c> mount so a FUSE/sdcard path is not attributed to the root filesystem.
    /// </param>
    public FileSystemInfo? Find(string path, bool includeRoot = true)
    {
        if (string.IsNullOrEmpty(path) || _entries.Length == 0)
            return null;

        FileSystemInfo? best = null;
        var bestLen = -1;

        for (var i = 0; i < _entries.Length; i++)
        {
            var entry = _entries[i];
            var mountPoint = entry.MountPoint;
            if (!includeRoot && mountPoint is "/")
                continue;

            if (!Covers(mountPoint, path))
                continue;

            var len = mountPoint is "/" ? 1 : mountPoint.TrimEnd('/').Length;
            if (len < bestLen)
                continue;

            best = entry;
            bestLen = len;
        }

        return best;
    }

    public static bool Covers(string mountPoint, string path)
    {
        if (string.IsNullOrEmpty(mountPoint) || string.IsNullOrEmpty(path))
            return false;

        if (mountPoint is "/")
            return path.StartsWith('/');

        var mp = mountPoint.TrimEnd('/');
        return path == mp || path.StartsWith(mp + "/", StringComparison.Ordinal);
    }
}
