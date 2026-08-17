using ADB_Explorer.Services;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Helpers;

public enum ArchiveVerboseKind
{
    Tar,
    Unzip,
}

/// <summary>
/// Maps <c>tar -v</c> / unzip verbose member names onto determinate progress.
/// Verbose is printed when a member starts, so that member's size is counted
/// when the next member starts — or when <see cref="Finish"/> runs after the command exits.
/// </summary>
public sealed class ArchiveVerboseProgress
{
    private readonly IReadOnlyDictionary<string, long> _memberBytes;
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private string? _pending;
    private bool _finished;

    public ArchiveVerboseProgress(IReadOnlyDictionary<string, long> memberBytes, int phases = 1)
    {
        _memberBytes = memberBytes ?? new Dictionary<string, long>();
        if (phases < 1)
            phases = 1;

        foreach (var size in _memberBytes.Values)
            TotalBytes += size;

        TotalBytes *= phases;
        TotalMembers = _memberBytes.Count * phases;
    }

    public static ArchiveVerboseProgress FromToc(IEnumerable<ArchiveEntry> entries, int phases = 1)
        => new(MemberBytesFromEntries(entries), phases);

    public static Dictionary<string, long> MemberBytesFromEntries(IEnumerable<ArchiveEntry> entries)
    {
        Dictionary<string, long> result = new(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var key = NormalizeMember(entry.Path);
            if (string.IsNullOrEmpty(key))
                continue;

            long size = 0;
            if (!entry.IsDirectory)
                size = entry.Size;

            result[key] = size;
        }

        return result;
    }

    public static Dictionary<string, long> MemberBytesForSelection(
        IEnumerable<ArchiveEntry> entries,
        string internalPath,
        bool isDirectory)
    {
        internalPath = ArchivePath.NormalizeInternal(internalPath);
        var prefix = internalPath + "/";
        return MemberBytesFromEntries(entries.Where(entry =>
        {
            var path = NormalizeMember(entry.Path);
            if (string.IsNullOrEmpty(path))
                return false;
            if (path.Equals(internalPath, StringComparison.Ordinal))
                return true;
            if (!isDirectory)
                return false;
            return path.StartsWith(prefix, StringComparison.Ordinal);
        }));
    }

    public static string NormalizeMember(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return "";

        var name = line.Trim();
        if (name.StartsWith("./", StringComparison.Ordinal))
            name = name[2..];

        return name.TrimEnd('/');
    }

    public static string NormalizeUnzipMember(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return "";

        var trimmed = line.Trim();
        if (trimmed.StartsWith("Archive:", StringComparison.OrdinalIgnoreCase))
            return "";

        string? rest = null;
        if (trimmed.StartsWith("inflating:", StringComparison.OrdinalIgnoreCase))
            rest = trimmed["inflating:".Length..];
        else if (trimmed.StartsWith("extracting:", StringComparison.OrdinalIgnoreCase))
            rest = trimmed["extracting:".Length..];
        else if (trimmed.StartsWith("creating:", StringComparison.OrdinalIgnoreCase))
            rest = trimmed["creating:".Length..];

        if (rest is null)
            return NormalizeMember(trimmed);

        return NormalizeMember(rest);
    }

    public long TotalBytes { get; }

    public int TotalMembers { get; }

    public long CompletedBytes { get; private set; }

    public int CompletedMembers { get; private set; }

    public string? CurrentMember { get; private set; }

    public double Percentage
    {
        get
        {
            if (_finished)
                return 100;

            double pct;
            if (TotalBytes > 0)
                pct = 100.0 * CompletedBytes / TotalBytes;
            else if (TotalMembers > 0)
                pct = 100.0 * CompletedMembers / TotalMembers;
            else
                return 0;

            if (pct < 0)
                return 0;
            if (pct > 100)
                return 100;
            return pct;
        }
    }

    public bool CanReport => TotalBytes > 0 || TotalMembers > 0;

    public void OnVerboseLine(string line, ArchiveVerboseKind kind = ArchiveVerboseKind.Tar)
    {
        string name;
        if (kind is ArchiveVerboseKind.Unzip)
            name = NormalizeUnzipMember(line);
        else
            name = NormalizeMember(line);

        if (string.IsNullOrEmpty(name)
            || name.StartsWith("tar:", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("unzip:", StringComparison.OrdinalIgnoreCase))
            return;

        if (!_seen.Add(name))
            return;

        CompletePending();
        _pending = name;
        CurrentMember = name;
    }

    public void BeginPhase()
    {
        CompletePending();
        _seen.Clear();
        _pending = null;
    }

    /// <summary>
    /// Counts the member that was in progress when the command exited, without ending the overall operation.
    /// </summary>
    public void OnCommandFinished()
        => CompletePending();

    public void Finish()
    {
        CompletePending();
        _finished = true;
        CompletedBytes = TotalBytes;
        CompletedMembers = TotalMembers;
    }

    private void CompletePending()
    {
        if (_pending is null)
            return;

        if (_memberBytes.TryGetValue(_pending, out var size))
            CompletedBytes += size;

        CompletedMembers++;
        _pending = null;
    }
}

/// <summary>
/// Reports archive verbose progress on a file operation using elapsed time (no speed estimate).
/// The operation's <see cref="FileOperation.FilePath"/> display name is left unchanged.
/// </summary>
internal sealed class ArchiveOpProgressSession
{
    private readonly FileOperation _op;

    public ArchiveOpProgressSession(FileOperation op, IReadOnlyDictionary<string, long> memberBytes, int phases = 1)
    {
        _op = op;
        Progress = new ArchiveVerboseProgress(memberBytes, phases);
        Start = DateTime.Now;
        Report();
    }

    public static ArchiveOpProgressSession FromToc(
        FileOperation op,
        string deviceId,
        string archivePath,
        CancellationToken cancellationToken,
        int phases = 1)
    {
        var toc = ArchiveListing.GetOrFetchToc(deviceId, archivePath, cancellationToken);
        return new(op, ArchiveVerboseProgress.MemberBytesFromEntries(toc.Entries), phases);
    }

    public ArchiveVerboseProgress Progress { get; }

    public DateTime Start { get; }

    public void OnLine(string line)
    {
        Progress.OnVerboseLine(line, DetectKind(line));
        Report();
    }

    public void OnTarLine(string line)
    {
        Progress.OnVerboseLine(line);
        Report();
    }

    public void OnUnzipLine(string line)
    {
        Progress.OnVerboseLine(line, ArchiveVerboseKind.Unzip);
        Report();
    }

    private static ArchiveVerboseKind DetectKind(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.StartsWith("inflating:", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("extracting:", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("creating:", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("Archive:", StringComparison.OrdinalIgnoreCase))
            return ArchiveVerboseKind.Unzip;

        return ArchiveVerboseKind.Tar;
    }

    public void BeginPhase()
    {
        Progress.BeginPhase();
        Report();
    }

    public void OnCommandFinished()
    {
        Progress.OnCommandFinished();
        Report();
    }

    public void Finish()
    {
        Progress.Finish();
        Report();
    }

    private void Report()
    {
        var label = _op.FilePath?.FullName ?? "";
        var info = new AdbSyncProgressInfo(label, Progress.Percentage, null, Progress.CompletedBytes);
        _op.StatusInfo = new InProgSyncProgressViewModel(
            info,
            Start,
            Progress.TotalBytes,
            Progress.CompletedBytes,
            showElapsedTime: true);
    }
}
