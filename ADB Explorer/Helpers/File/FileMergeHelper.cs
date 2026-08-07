using ADB_Explorer.Converters;
using ADB_Explorer.Models;
using ADB_Explorer.Services;
using static ADB_Explorer.Models.AbstractFile;

namespace ADB_Explorer.Helpers;

/// <summary>
/// Merge/conflict helpers: identical size+date detection and SyncFile tree filtering.
/// </summary>
public static class FileMergeHelper
{
    public const double IdenticalMtimeToleranceSeconds = 2.0;

    public enum ConflictResolution
    {
        Cancel,
        Replace,
        SkipConflicts,
        PerFile,
    }

    public readonly record struct DestEntry(bool Exists, bool IsDirectory, long? Size, DateTime? MtimeUtc);

    public readonly record struct ConflictCandidate(
        string Key,
        string Name,
        bool IsDirectory,
        long? Size,
        DateTime? MtimeUtc);

    /// <summary>
    /// True when both sides have size and mtime and they match (mtime within tolerance).
    /// Missing date on either side → not identical.
    /// </summary>
    public static bool AreIdenticalForMerge(
        long? srcSize,
        DateTime? srcMtimeUtc,
        long? destSize,
        DateTime? destMtimeUtc)
    {
        if (srcSize is null || destSize is null || srcSize != destSize)
            return false;

        if (srcMtimeUtc is null || destMtimeUtc is null)
            return false;

        var delta = Math.Abs((srcMtimeUtc.Value - destMtimeUtc.Value).TotalSeconds);
        return delta <= IdenticalMtimeToleranceSeconds;
    }

    public static DestEntry GetWindowsDestEntry(string targetPath, string name)
    {
        var path = Path.Combine(targetPath, name);
        if (File.Exists(path))
        {
            var info = new FileInfo(path);
            return new(true, false, info.Length, info.LastWriteTimeUtc);
        }

        if (Directory.Exists(path))
            return new(true, true, null, null);

        return new(false, false, null, null);
    }

    public static DestEntry GetAndroidDestEntry(
        string targetPath,
        string name,
        IReadOnlyDictionary<string, FileStat>? listingByName = null)
    {
        if (listingByName is not null)
        {
            if (!listingByName.TryGetValue(name, out var stat))
                return new(false, false, null, null);

            var isDir = stat.Type is FileType.Folder;
            DateTime? mtimeUtc = stat.ModifiedTime is DateTime local
                ? local.ToUniversalTime()
                : null;

            return new(true, isDir, isDir ? null : stat.Size, mtimeUtc);
        }

        if (targetPath == Data.CurrentPath)
        {
            var file = Data.DirList?.FileList?.FirstOrDefault(f =>
                f.FullName.Equals(name, StringComparison.OrdinalIgnoreCase)
                || f.FullName.Equals(name, StringComparison.Ordinal));

            if (file is null)
                return new(false, false, null, null);

            DateTime? mtimeUtc = file.ModifiedTime?.ToUniversalTime();
            return new(true, file.IsDirectory, file.IsDirectory ? null : file.Size, mtimeUtc);
        }

        return new(false, false, null, null);
    }

    public static Dictionary<string, FileStat>? TryListAndroidDirByName(string deviceId, string targetPath, StringComparer comparer)
    {
        try
        {
            return ADBService.ListDirectoryEntries(deviceId, targetPath, CancellationToken.None)
                .GroupBy(e => e.FullName, comparer)
                .ToDictionary(g => g.Key, g => g.First(), comparer);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Removes leaves from <paramref name="source"/> that already match files under
    /// <paramref name="windowsRoot"/> (the Windows path of the transfer root).
    /// Returns whether anything remains to transfer.
    /// </summary>
    public static bool FilterIdenticalPullTree(SyncFile source, string windowsRoot)
    {
        if (source is null || string.IsNullOrEmpty(windowsRoot))
            return source is not null;

        if (!source.IsDirectory)
        {
            return !IsIdenticalToWindowsFile(source, windowsRoot);
        }

        // No pre-built child tree — adb pull transfers the whole folder.
        if (source.Children.Count == 0)
            return true;

        return FilterPullChildren(source, source.FullPath, windowsRoot);
    }

    private static bool FilterPullChildren(SyncFile folder, string androidRoot, string windowsRoot)
    {
        var kept = new List<SyncFile>();
        foreach (var child in folder.Children.ToList())
        {
            if (child.IsDirectory)
            {
                if (FilterPullChildren(child, androidRoot, windowsRoot))
                    kept.Add(child);
            }
            else
            {
                var relative = FileHelper.ExtractRelativePath(child.FullPath, androidRoot);
                var winPath = FileHelper.ConcatPaths(windowsRoot, relative, '\\');
                if (!IsIdenticalToWindowsFile(child, winPath))
                    kept.Add(child);
            }
        }

        folder.Children.Clear();
        folder.Children.AddRange(kept);
        return kept.Count > 0;
    }

    private static bool IsIdenticalToWindowsFile(SyncFile source, string windowsPath)
    {
        if (!File.Exists(windowsPath))
            return false;

        var info = new FileInfo(windowsPath);
        DateTime? srcUtc = source.UnixTime.FromUnixTime(asLocal: false)
            ?? source.DateModified?.ToUniversalTime();

        return AreIdenticalForMerge(source.Size, srcUtc, info.Length, info.LastWriteTimeUtc);
    }

    /// <summary>
    /// Removes leaves from a Windows push tree that already match Android files under
    /// <paramref name="androidRoot"/> (device path of the transfer root).
    /// </summary>
    public static bool FilterIdenticalPushTree(SyncFile source, string androidRoot, string deviceId)
    {
        _ = deviceId;
        if (source is null || string.IsNullOrEmpty(androidRoot))
            return source is not null;

        Dictionary<string, (long? Size, DateTime? MtimeUtc)> destIndex;
        try
        {
            var tree = FileHelper.GetFolderTree([androidRoot], isFolder: source.IsDirectory, CancellationToken.None);
            destIndex = tree
                .Where(t => !t.IsFolder)
                .GroupBy(t => t.Name, StringComparer.Ordinal)
                .ToDictionary(
                    g => g.Key,
                    g => (g.First().Size, g.First().Date.FromUnixTime(asLocal: false)),
                    StringComparer.Ordinal);
        }
        catch
        {
            destIndex = [];
        }

        // Also index by relative path from androidRoot for nested files.
        var byRelative = destIndex.ToDictionary(
            kv => FileHelper.ExtractRelativePath(kv.Key, androidRoot),
            kv => kv.Value,
            StringComparer.Ordinal);

        if (!source.IsDirectory)
        {
            return !IsIdenticalPushLeaf(source, source.FullName, byRelative, destIndex, androidRoot);
        }

        return FilterPushChildren(source, source.FullPath, byRelative, destIndex, androidRoot);
    }

    private static bool FilterPushChildren(
        SyncFile folder,
        string windowsRoot,
        Dictionary<string, (long? Size, DateTime? MtimeUtc)> byRelative,
        Dictionary<string, (long? Size, DateTime? MtimeUtc)> byAbsolute,
        string androidRoot)
    {
        var kept = new List<SyncFile>();
        foreach (var child in folder.Children.ToList())
        {
            if (child.IsDirectory)
            {
                if (FilterPushChildren(child, windowsRoot, byRelative, byAbsolute, androidRoot))
                    kept.Add(child);
            }
            else
            {
                var relative = FileHelper.ExtractRelativePath(child.FullPath, windowsRoot);
                if (!IsIdenticalPushLeaf(child, relative, byRelative, byAbsolute, androidRoot))
                    kept.Add(child);
            }
        }

        folder.Children.Clear();
        folder.Children.AddRange(kept);
        return kept.Count > 0;
    }

    private static bool IsIdenticalPushLeaf(
        SyncFile source,
        string relativeOrName,
        Dictionary<string, (long? Size, DateTime? MtimeUtc)> byRelative,
        Dictionary<string, (long? Size, DateTime? MtimeUtc)> byAbsolute,
        string androidRoot)
    {
        (long? Size, DateTime? MtimeUtc) dest;
        if (!byRelative.TryGetValue(relativeOrName, out dest))
        {
            var absolute = FileHelper.ConcatPaths(androidRoot, relativeOrName, '/');
            if (!byAbsolute.TryGetValue(absolute, out dest))
                return false;
        }

        DateTime? srcUtc = null;
        try
        {
            if (File.Exists(source.FullPath))
                srcUtc = new FileInfo(source.FullPath).LastWriteTimeUtc;
        }
        catch
        { }

        srcUtc ??= source.DateModified?.ToUniversalTime()
            ?? source.UnixTime.FromUnixTime(asLocal: false);

        return AreIdenticalForMerge(source.Size, srcUtc, dest.Size, dest.MtimeUtc);
    }

    /// <summary>
    /// Side-by-side source/destination metadata for the per-file conflict grid.
    /// <see cref="Name"/> is relative to the transfer target (may include nested path segments).
    /// </summary>
    public readonly record struct ConflictComparisonInfo(
        string Name,
        bool IsDirectory,
        long? SourceSize,
        DateTime? SourceMtimeUtc,
        long? DestSize,
        DateTime? DestMtimeUtc,
        bool IsIdentical);

    public readonly record struct MergeOutcome<T>(
        IReadOnlyList<T> Items,
        IReadOnlySet<string> ReplaceRelativePaths,
        IReadOnlySet<string> ConflictRelativePaths);

    /// <summary>
    /// Expands top-level collisions into file-level conflicts. Matching folders are merged
    /// (not listed); only nested file collisions and type mismatches are returned.
    /// </summary>
    public static IReadOnlyList<ConflictComparisonInfo> ExpandConflicts(
        IEnumerable<ConflictCandidate> candidates,
        string targetPath,
        Func<string, DestEntry> getTopDest,
        bool targetIsWindows,
        string? deviceId,
        StringComparer comparer,
        CancellationToken cancellationToken = default)
    {
        var targetSep = targetIsWindows ? '\\' : '/';
        var results = new List<ConflictComparisonInfo>();

        foreach (var candidate in candidates)
        {
            var dest = getTopDest(candidate.Name);
            if (!dest.Exists)
                continue;

            if (candidate.IsDirectory && dest.IsDirectory)
            {
                var destFolder = FileHelper.ConcatPaths(targetPath, candidate.Name, targetSep);
                results.AddRange(CollectFolderMergeConflicts(
                    candidate.Key,
                    candidate.Name,
                    destFolder,
                    targetIsWindows,
                    deviceId,
                    comparer,
                    cancellationToken));
                continue;
            }

            // Source folder vs dest file: auto-replace (folders are not prompted).
            if (candidate.IsDirectory)
                continue;

            // File vs file, or file vs dest folder type mismatch.
            var identical = !dest.IsDirectory
                && AreIdenticalForMerge(candidate.Size, candidate.MtimeUtc, dest.Size, dest.MtimeUtc);

            results.Add(new ConflictComparisonInfo(
                candidate.Name,
                dest.IsDirectory,
                candidate.Size,
                candidate.MtimeUtc,
                dest.IsDirectory ? null : dest.Size,
                dest.IsDirectory ? null : dest.MtimeUtc,
                identical));
        }

        return [.. results.OrderBy(r => r.Name, comparer)];
    }

    private static List<ConflictComparisonInfo> CollectFolderMergeConflicts(
        string sourceFolderPath,
        string topLevelName,
        string destFolderPath,
        bool targetIsWindows,
        string? deviceId,
        StringComparer comparer,
        CancellationToken cancellationToken)
    {
        var targetSep = targetIsWindows ? '\\' : '/';
        var sourceIsWindows = sourceFolderPath.Contains('\\')
            || (sourceFolderPath.Length >= 2 && sourceFolderPath[1] == ':');

        var destIndex = targetIsWindows
            ? IndexWindowsTree(destFolderPath, comparer)
            : IndexAndroidTree(destFolderPath, deviceId, comparer, cancellationToken);

        if (destIndex.Count == 0)
            return [];

        var sourceIndex = sourceIsWindows
            ? IndexWindowsTree(sourceFolderPath, comparer)
            : IndexAndroidTree(sourceFolderPath, deviceId, comparer, cancellationToken);

        var results = new List<ConflictComparisonInfo>();

        foreach (var (relativeKey, source) in sourceIndex)
        {
            if (string.IsNullOrEmpty(relativeKey))
                continue;

            if (!destIndex.TryGetValue(relativeKey, out var dest))
                continue;

            if (source.IsDirectory && dest.IsDirectory)
                continue; // nested folders merge silently

            // Source folder vs dest file: auto-replace without prompting.
            if (source.IsDirectory)
                continue;

            var displayName = FileHelper.ConcatPaths(
                topLevelName,
                relativeKey.Replace('/', targetSep),
                targetSep);

            var identical = !dest.IsDirectory
                && AreIdenticalForMerge(source.Size, source.MtimeUtc, dest.Size, dest.MtimeUtc);

            results.Add(new ConflictComparisonInfo(
                displayName,
                dest.IsDirectory,
                source.Size,
                source.MtimeUtc,
                dest.IsDirectory ? null : dest.Size,
                dest.IsDirectory ? null : dest.MtimeUtc,
                identical));
        }

        return results;
    }

    private static Dictionary<string, DestEntry> IndexWindowsTree(string destFolderPath, StringComparer comparer)
    {
        var index = new Dictionary<string, DestEntry>(comparer);
        if (!Directory.Exists(destFolderPath))
            return index;

        try
        {
            foreach (var dir in Directory.EnumerateDirectories(destFolderPath, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(destFolderPath, dir).Replace('\\', '/');
                index[relative] = new(true, true, null, null);
            }

            foreach (var file in Directory.EnumerateFiles(destFolderPath, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(destFolderPath, file).Replace('\\', '/');
                var info = new FileInfo(file);
                index[relative] = new(true, false, info.Length, info.LastWriteTimeUtc);
            }
        }
        catch
        { }

        return index;
    }

    private static Dictionary<string, DestEntry> IndexAndroidTree(
        string destFolderPath,
        string? deviceId,
        StringComparer comparer,
        CancellationToken cancellationToken)
    {
        var index = new Dictionary<string, DestEntry>(comparer);
        if (string.IsNullOrEmpty(deviceId))
            return index;

        try
        {
            var tree = FileHelper.GetFolderTree([destFolderPath], isFolder: true, cancellationToken);
            foreach (var entry in tree)
            {
                var relative = FileHelper.ExtractRelativePath(entry.Name, destFolderPath).Replace('\\', '/');
                if (string.IsNullOrEmpty(relative) || relative == entry.Name)
                    continue;

                if (entry.IsFolder)
                    index[relative] = new(true, true, null, null);
                else
                    index[relative] = new(true, false, entry.Size, entry.Date.FromUnixTime(asLocal: false));
            }
        }
        catch
        { }

        return index;
    }

    /// <summary>
    /// Removes leaves that the user chose to skip (or that conflict and were not selected for replace).
    /// New (non-conflicting) leaves are kept. Returns whether anything remains.
    /// </summary>
    public static bool FilterSyncTreeByConflictResolution(
        SyncFile source,
        string topLevelName,
        IReadOnlySet<string> replaceRelativePaths,
        IReadOnlySet<string> conflictRelativePaths,
        char targetSep)
    {
        if (source is null)
            return false;

        if (!source.IsDirectory)
        {
            if (!conflictRelativePaths.Contains(topLevelName))
                return true;

            return replaceRelativePaths.Contains(topLevelName);
        }

        if (source.Children.Count == 0)
            return true;

        return FilterSyncChildrenByResolution(
            source,
            source.FullPath,
            topLevelName,
            replaceRelativePaths,
            conflictRelativePaths,
            targetSep);
    }

    private static bool FilterSyncChildrenByResolution(
        SyncFile folder,
        string sourceRoot,
        string topLevelName,
        IReadOnlySet<string> replaceRelativePaths,
        IReadOnlySet<string> conflictRelativePaths,
        char targetSep)
    {
        var kept = new List<SyncFile>();
        foreach (var child in folder.Children.ToList())
        {
            if (child.IsDirectory)
            {
                var wasEmpty = child.Children.Count == 0;
                if (FilterSyncChildrenByResolution(
                        child,
                        sourceRoot,
                        topLevelName,
                        replaceRelativePaths,
                        conflictRelativePaths,
                        targetSep))
                {
                    kept.Add(child);
                }
                else if (wasEmpty)
                {
                    // Empty folders are auto-replaced / always kept when merging.
                    kept.Add(child);
                }
            }
            else
            {
                var relative = FileHelper.ExtractRelativePath(child.FullPath, sourceRoot)
                    .Replace('/', targetSep)
                    .Replace('\\', targetSep);
                var key = FileHelper.ConcatPaths(topLevelName, relative, targetSep);

                if (!conflictRelativePaths.Contains(key))
                {
                    kept.Add(child); // new file under merge
                    continue;
                }

                if (replaceRelativePaths.Contains(key))
                    kept.Add(child);
            }
        }

        folder.Children.Clear();
        folder.Children.AddRange(kept);
        return kept.Count > 0;
    }
}
