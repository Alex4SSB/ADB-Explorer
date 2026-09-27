using static ADB_Explorer.Models.AbstractFile;

namespace ADB_Explorer.Services;

public partial class CopyPasteService
{
    public static SyncFile MergeFolderTree(FileClass folder, string targetPath, IEnumerable<string> filesToReplace)
    {
        StringComparer comparer = StringComparer.InvariantCultureIgnoreCase;

        var children = folder.GetChildren();
        if (children is null || children.Length == 0)
            return folder.GetSyncFile();

        var parent = folder.ParentPath;

        var remote = Directory.EnumerateFiles(targetPath, "*", SearchOption.AllDirectories)
            .Select(f => FileHelper.ConcatPaths(parent, FileHelper.ExtractRelativePath(f, targetPath)))
            .ToHashSet(comparer);

        var filesToReplaceSet = filesToReplace.ToHashSet(comparer);

        var tree = children.Where(c => !remote.Contains(c.Name) || filesToReplaceSet.Contains(c.Name)).ToList();

        return new(folder, tree);
    }

    /// <summary>
    /// Check for existing items in the target location and resolve conflicts.
    /// Matching folders are merged; only nested file collisions are prompted.
    /// </summary>
    public static async Task<FileMergeHelper.MergeOutcome<string>> MergeFiles(IEnumerable<string> filePaths, string targetPath, LogicalDeviceViewModel? device = null)
    {
        if (filePaths is null || targetPath is null)
            return new([], EmptyPathSet, EmptyPathSet);

        var items = filePaths.ToList();
        if (items.Count == 0)
            return new(items, EmptyPathSet, EmptyPathSet);

        device ??= Data.ActiveDevice;
        var target = MergeTarget.Create(targetPath, device);
        var comparer = target.Comparer;

        var candidates = items.Select(path =>
        {
            var name = FileHelper.GetFullName(path);
            var isWindowsSource = path.Contains('\\') || (path.Length >= 2 && path[1] == ':');

            bool isDir;
            long? size = null;
            DateTime? mtimeUtc = null;

            if (isWindowsSource)
            {
                isDir = Directory.Exists(path);
                if (!isDir)
                {
                    try
                    {
                        if (File.Exists(path))
                        {
                            var info = new FileInfo(path);
                            size = info.Length;
                            mtimeUtc = info.LastWriteTimeUtc;
                        }
                    }
                    catch
                    { }
                }
            }
            else
            {
                var src = Data.DirList?.FileList?.FirstOrDefault(f =>
                    comparer.Equals(f.FullPath, path) || comparer.Equals(f.FullName, name));
                isDir = src?.IsDirectory ?? false;
                if (!isDir)
                {
                    size = src?.Size;
                    mtimeUtc = src?.ModifiedTime?.ToUniversalTime();
                }
            }

            return new FileMergeHelper.ConflictCandidate(path, name, isDir, size, mtimeUtc);
        }).ToList();

        if (await ResolveConflicts(target, candidates, GetCommonParentPath(items), device?.ID) is not { } conflicts)
            return new(items, EmptyPathSet, EmptyPathSet);

        return ApplyPathMergeOutcome(items, candidates, target.GetDest, conflicts.Names, conflicts.Resolution, comparer);
    }

    public static Task<FileMergeHelper.MergeOutcome<FileClass>> MergeFiles(string targetPath, params IEnumerable<FileClass> filePaths)
        => MergeFiles(targetPath, filePaths, null);

    /// <summary>
    /// Check for existing items in the target location and resolve conflicts.
    /// Matching folders are merged; only nested file collisions are prompted.
    /// </summary>
    public static async Task<FileMergeHelper.MergeOutcome<FileClass>> MergeFiles(string targetPath, IEnumerable<FileClass> filePaths, LogicalDeviceViewModel? device, LogicalDeviceViewModel? sourceDevice = null)
    {
        if (filePaths is null || targetPath is null)
            return new([], EmptyPathSet, EmptyPathSet);

        var items = filePaths.ToList();
        if (items.Count == 0)
            return new(items, EmptyPathSet, EmptyPathSet);

        device ??= Data.ActiveDevice;
        var target = MergeTarget.Create(targetPath, device);

        var candidates = items.Select(f =>
        {
            long? size = f.Size;
            DateTime? mtimeUtc = f.IsDirectory ? null : f.ModifiedTime?.ToUniversalTime();

            if (!f.IsDirectory
                && f.PathType is FilePathType.Windows
                && File.Exists(f.FullPath))
            {
                try
                {
                    var info = new FileInfo(f.FullPath);
                    size ??= info.Length;
                    mtimeUtc ??= info.LastWriteTimeUtc;
                }
                catch
                { }
            }

            return new FileMergeHelper.ConflictCandidate(f.FullPath, f.FullName, f.IsDirectory, size, mtimeUtc);
        }).ToList();

        var sourcePath = GetCommonParentPath(items.Select(f => f.FullPath));
        if (await ResolveConflicts(target, candidates, sourcePath, device?.ID, sourceDevice?.ID) is not { } conflicts)
            return new(items, EmptyPathSet, EmptyPathSet);

        return ApplyFileClassMergeOutcome(items, target.GetDest, conflicts.Names, conflicts.Resolution, target.Comparer);
    }

    /// <summary>The paste destination: separator, name comparer, and a lookup of what already exists there.</summary>
    private sealed class MergeTarget
    {
        public required string Path { get; init; }
        public required char Separator { get; init; }
        public required StringComparer Comparer { get; init; }
        public Dictionary<string, FileStat>? AndroidListing { get; init; }
        public bool AndroidListingFailed { get; init; }

        public static MergeTarget Create(string targetPath, LogicalDeviceViewModel? device)
        {
            var sep = FileHelper.GetSeparator(targetPath);
            var caseSensitive = sep is '/' && DriveHelper.GetRestrictions(targetPath, device).CaseInsensitiveNames is not true;
            StringComparer comparer = caseSensitive
                ? StringComparer.InvariantCulture
                : StringComparer.InvariantCultureIgnoreCase;

            Dictionary<string, FileStat>? androidListing = null;
            var listAndroid = sep is '/' && device is not null && !IsExplorerListing(targetPath, device);
            if (listAndroid)
                androidListing = FileMergeHelper.TryListAndroidDirByName(device!.ID, targetPath, comparer);

            return new()
            {
                Path = targetPath,
                Separator = sep,
                Comparer = comparer,
                AndroidListing = androidListing,
                AndroidListingFailed = listAndroid && androidListing is null,
            };
        }

        public FileMergeHelper.DestEntry GetDest(string name) => Separator is '/'
            ? FileMergeHelper.GetAndroidDestEntry(Path, name, AndroidListing)
            : FileMergeHelper.GetWindowsDestEntry(Path, name);
    }

    /// <summary>Finds candidates that collide with the target and prompts for a resolution; null when nothing collides.</summary>
    private static async Task<(HashSet<string> Names, (FileMergeHelper.ConflictResolution Resolution, IReadOnlyList<string>? ReplaceNames) Resolution)?> ResolveConflicts(
        MergeTarget target,
        List<FileMergeHelper.ConflictCandidate> candidates,
        string sourcePath,
        string? deviceId,
        string? sourceDeviceId = null)
    {
        HashSet<string> existingNames;
        if (target.AndroidListingFailed)
        {
            existingNames = candidates.Select(c => c.Name).ToHashSet(target.Comparer);
        }
        else
        {
            existingNames = candidates
                .Select(c => c.Name)
                .Where(n => target.GetDest(n).Exists)
                .ToHashSet(target.Comparer);
        }

        if (existingNames.Count == 0)
            return null;

        var comparisons = await Task.Run(() => FileMergeHelper.ExpandConflicts(
            candidates.Where(c => existingNames.Contains(c.Name)),
            target.Path,
            target.GetDest,
            target.Separator is '\\',
            deviceId,
            target.Comparer,
            sourceDeviceId: sourceDeviceId));

        if (comparisons.Count == 0)
            return null;

        var conflictNames = comparisons.Select(c => c.Name).ToHashSet(target.Comparer);
        var resolution = await PromptConflictResolution(
            sourcePath, target.Path, conflictNames.Count, comparisons, deviceId);

        return (conflictNames, resolution);
    }

    private static bool IsExplorerListing(string targetPath, LogicalDeviceViewModel? device)
    {
        var current = Data.ActiveDevice;
        if (current is null)
            return false;
        if (device is not null && device.ID != current.ID)
            return false;

        return string.Equals(Data.CurrentPath, targetPath, StringComparison.Ordinal);
    }

    private static readonly HashSet<string> EmptyPathSet = [];

    private static string GetCommonParentPath(IEnumerable<string> fullPaths)
    {
        var parents = fullPaths
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => FileHelper.GetParentPath(p))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return parents.Count == 0 ? string.Empty : parents[0];
    }

    private static async Task<(FileMergeHelper.ConflictResolution Resolution, IReadOnlyList<string>? ReplaceNames)> PromptConflictResolution(
        string sourcePath,
        string targetPath,
        int conflictCount,
        IReadOnlyList<FileMergeHelper.ConflictComparisonInfo> comparisons,
        string? deviceId = null)
    {
        string destination = FileHelper.GetFullName(targetPath);
        if (Data.CurrentDisplayNames.TryGetValue((deviceId ?? Data.ActiveDevice?.ID, targetPath), out var drive))
            destination = drive;

        var message = conflictCount == 1
            ? string.Format(Strings.Resources.S_CONFLICT_ITEMS_DESTINATION, destination)
            : string.Format(Strings.Resources.S_CONFLICT_ITEMS_PLURAL_DESTINATION, conflictCount, destination);

        var choice = await DialogService.ShowConflictResolution(message, Strings.Resources.S_PASTE_CONFLICTS_TITLE);

        if (choice is FileMergeHelper.ConflictResolution.PerFile)
        {
            var replaceNames = await DialogService.ShowPerFileConflictResolution(
                comparisons,
                string.Format(Strings.Resources.S_CONFLICT_DECIDE_TITLE, comparisons.Count),
                sourcePath,
                targetPath);

            if (replaceNames is null)
                return (FileMergeHelper.ConflictResolution.Cancel, null);

            return (FileMergeHelper.ConflictResolution.PerFile, replaceNames);
        }

        return (choice, null);
    }

    private static FileMergeHelper.MergeOutcome<string> ApplyPathMergeOutcome(
        List<string> items,
        List<FileMergeHelper.ConflictCandidate> candidates,
        Func<string, FileMergeHelper.DestEntry> getDest,
        HashSet<string> conflictRelativePaths,
        (FileMergeHelper.ConflictResolution Resolution, IReadOnlyList<string>? ReplaceNames) resolution,
        StringComparer comparer)
    {
        if (resolution.Resolution is FileMergeHelper.ConflictResolution.Cancel)
            return new([], EmptyPathSet, EmptyPathSet);

        var replaceSet = ResolveReplaceSet(conflictRelativePaths, resolution, comparer);
        var byName = candidates.ToDictionary(c => c.Name, comparer);

        var kept = items.Where(p =>
        {
            var name = FileHelper.GetFullName(p);
            var dest = getDest(name);
            if (!dest.Exists)
                return true;

            if (byName.TryGetValue(name, out var candidate) && candidate.IsDirectory && dest.IsDirectory)
                return true; // merge folder

            return replaceSet.Contains(name);
        }).ToList();

        return new(kept, replaceSet, conflictRelativePaths);
    }

    private static FileMergeHelper.MergeOutcome<FileClass> ApplyFileClassMergeOutcome(
        List<FileClass> items,
        Func<string, FileMergeHelper.DestEntry> getDest,
        HashSet<string> conflictRelativePaths,
        (FileMergeHelper.ConflictResolution Resolution, IReadOnlyList<string>? ReplaceNames) resolution,
        StringComparer comparer)
    {
        if (resolution.Resolution is FileMergeHelper.ConflictResolution.Cancel)
            return new([], EmptyPathSet, EmptyPathSet);

        var replaceSet = ResolveReplaceSet(conflictRelativePaths, resolution, comparer);

        var kept = items.Where(f =>
        {
            var dest = getDest(f.FullName);
            if (!dest.Exists)
                return true;

            if (f.IsDirectory && dest.IsDirectory)
                return true; // merge folder

            return replaceSet.Contains(f.FullName);
        }).ToList();

        return new(kept, replaceSet, conflictRelativePaths);
    }

    private static HashSet<string> ResolveReplaceSet(
        HashSet<string> conflictRelativePaths,
        (FileMergeHelper.ConflictResolution Resolution, IReadOnlyList<string>? ReplaceNames) resolution,
        StringComparer comparer)
        => resolution.Resolution switch
        {
            FileMergeHelper.ConflictResolution.Replace => conflictRelativePaths,
            FileMergeHelper.ConflictResolution.SkipConflicts => new HashSet<string>(comparer),
            FileMergeHelper.ConflictResolution.PerFile =>
                resolution.ReplaceNames?.ToHashSet(comparer) ?? new HashSet<string>(comparer),
            _ => new HashSet<string>(comparer),
        };
}
