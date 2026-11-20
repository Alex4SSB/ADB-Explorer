using Vanara.PInvoke;
using FileAttributes = System.IO.FileAttributes;
using FileSystemInfo = System.IO.FileSystemInfo;
using static ADB_Explorer.Models.AbstractFile;
using static Vanara.PInvoke.CldApi;

namespace ADB_Explorer.Services;

// A folder listed once isn't asked for again, so changes on the device are brought in: those of the app's own operations
// as they finish, and those made on the device itself in the folders open in File Explorer, every few seconds.
// Placeholders are added, removed and updated; what's still only here (not pushed yet, or edited) is left alone.
public sealed partial class DeviceMount
{
    // After an operation finishes, long enough for Windows to apply the changes the mount answered for
    private static readonly TimeSpan REFRESH_DELAY = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan WINDOW_REFRESH_INTERVAL = TimeSpan.FromSeconds(3);

    // A folder whose refresh is slow (a large one) is refreshed this many times less often
    private const int SLOW_REFRESH_FACTOR = 10;

    // Device and local modified times further apart than this mean the file changed (some storage keeps 2 s steps)
    private static readonly TimeSpan MODIFIED_TOLERANCE = TimeSpan.FromSeconds(2);

    /// <summary>Operations of the app that finished, by when, for the folders they changed to be refreshed.</summary>
    private readonly ConcurrentQueue<(FileOperation Operation, DateTime Finished)> _finishedOperations = new();

    /// <summary>The mount's own operations, whose changes Windows already has.</summary>
    private readonly ConcurrentDictionary<FileOperation, byte> _ownOperations = new();

    /// <summary>Local paths the mount is deleting itself, which the delete callback leaves be.</summary>
    private readonly ConcurrentDictionary<string, byte> _selfDeletes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>When each folder open in File Explorer is next refreshed.</summary>
    private readonly Dictionary<string, DateTime> _windowRefreshDue = new(StringComparer.OrdinalIgnoreCase);

    private DateTime _nextWindowRefresh = DateTime.MinValue;

    /// <summary>When a delete or rename was last answered: refreshing just then could undo it before Windows applies it.</summary>
    private long _lastChangeTicks;

    /// <summary>Called for every operation of the app's queue as it finishes: the mounts of its device, and its target device, take it in.</summary>
    public static void OnOperationFinished(FileOperation operation)
    {
        foreach (var mount in Mounts)
        {
            if (operation.Device?.ID != mount.DeviceId && operation.TargetDevice?.ID != mount.DeviceId)
                continue;

            mount._finishedOperations.Enqueue((operation, DateTime.UtcNow));
            mount._uploadSignal.Set();
        }
    }

    private void AddOwnOperations(IEnumerable<FileOperation> operations)
    {
        foreach (var operation in operations)
            _ownOperations[operation] = 0;
    }

    private void MarkChangeAnswered() => Interlocked.Exchange(ref _lastChangeTicks, DateTime.UtcNow.Ticks);

    private bool IsChangeRecent => DateTime.UtcNow.Ticks - Interlocked.Read(ref _lastChangeTicks) < REFRESH_DELAY.Ticks * 2;

    private bool IsSelfDelete(string localPath)
        => _selfDeletes.Keys.Any(path => string.Equals(localPath, path, StringComparison.OrdinalIgnoreCase)
            || localPath.StartsWith(path + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether the upload thread has refreshing to come back for.</summary>
    private bool HasRefreshes => !_finishedOperations.IsEmpty;

    /// <summary>The folders of finished operations, once Windows has had time to apply its own; and the open windows' folders when due.</summary>
    private void RunRefreshes(CancellationToken token)
    {
        HashSet<string> folders = new(StringComparer.OrdinalIgnoreCase);
        var now = DateTime.UtcNow;
        while (_finishedOperations.TryPeek(out var finished) && now - finished.Finished >= REFRESH_DELAY)
        {
            _finishedOperations.TryDequeue(out _);
            if (_ownOperations.TryRemove(finished.Operation, out _))
                continue;

            foreach (var path in ChangedFolders(finished.Operation))
            {
                if (ToLocalFolder(path) is { } local)
                    folders.Add(local);
            }
        }

        foreach (var folder in folders)
        {
            if (token.IsCancellationRequested)
                return;

            RefreshFolder(folder, token);
        }

        if (now >= _nextWindowRefresh && !IsChangeRecent)
        {
            _nextWindowRefresh = now + WINDOW_REFRESH_INTERVAL;
            RefreshWindowFolders(folders, token);
        }
    }

    /// <summary>The device folders an operation may have changed: those of its source and target, and either itself where it's a folder.</summary>
    private static IEnumerable<string> ChangedFolders(FileOperation operation)
    {
        foreach (var path in new FilePath?[] { operation.FilePath, operation.TargetPath })
        {
            if (path is not { PathType: FilePathType.Android } || string.IsNullOrEmpty(path.FullPath))
                continue;

            yield return path.FullPath;
            yield return path.ParentPath;
        }
    }

    /// <summary>The local folder of a device folder, if it's on a mounted drive and listed here already.</summary>
    private string? ToLocalFolder(string devicePath)
    {
        if (DriveOf(devicePath) is null)
            return null;

        var local = ToLocalPath(devicePath);

        return Directory.Exists(local) ? local : null;
    }

    private void RefreshWindowFolders(HashSet<string> refreshed, CancellationToken token)
    {
        var now = DateTime.UtcNow;
        var open = ExplorerWindows().Select(LocationOf).Where(path => path is not null && RelativeToRoot(path) is not (null or "."))
                                    .Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        foreach (var gone in _windowRefreshDue.Keys.Except(open, StringComparer.OrdinalIgnoreCase).ToList())
            _windowRefreshDue.Remove(gone);

        foreach (var folder in open)
        {
            if (token.IsCancellationRequested)
                return;

            if (refreshed.Contains(folder) || (_windowRefreshDue.TryGetValue(folder, out var due) && now < due))
                continue;

            var watch = Stopwatch.StartNew();
            RefreshFolder(folder, token);
            _windowRefreshDue[folder] = DateTime.UtcNow + Max(WINDOW_REFRESH_INTERVAL, watch.Elapsed * SLOW_REFRESH_FACTOR);
        }
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

    /// <summary>
    /// Brings a listed folder up to date with the device. Only placeholders change: plain files (not pushed yet), edited ones,
    /// and those in the middle of a save or upload are left alone, and a folder never listed lists fresh when opened.
    /// </summary>
    private void RefreshFolder(string localFolder, CancellationToken token)
    {
        if (ToDevicePath(localFolder) is not { } devicePath || _populating.ContainsKey(localFolder))
            return;

        FileAttributes folderAttributes;
        try
        {
            folderAttributes = File.GetAttributes(localFolder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return;
        }

        if (!folderAttributes.HasFlag(FileAttributes.Directory) || !IsPlaceholder(folderAttributes) || folderAttributes.HasFlag(RECALL_ON_OPEN))
            return;

        if (ListMountable(devicePath, token) is not { } onDevice)
            return;

        var isReadOnly = DriveHelper.GetRestrictions(devicePath, Device).ReadOnly is true;
        Dictionary<string, FileSystemInfo> local;
        try
        {
            local = new DirectoryInfo(localFolder).EnumerateFileSystemInfos().ToDictionary(entry => entry.Name, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return;
        }

        var hasShortSizes = !Device.SupportsLsV2;
        List<(FileInfo File, FileStat DeviceEntry)> changed = [];
        foreach (var (name, entry) in local)
        {
            if (!IsRefreshable(entry))
                continue;

            if (!onDevice.TryGetValue(name, out var deviceEntry) || (deviceEntry.Type is FileType.Folder) != (entry is DirectoryInfo))
            {
                RemovePlaceholder(entry);
                continue;
            }

            if (entry is FileInfo file && HasChanged(file, deviceEntry, hasShortSizes))
                changed.Add((file, deviceEntry));
        }

        List<FileStat> added = [.. onDevice.Values.Where(entry => !local.TryGetValue(entry.FullName, out var existing) || !Path.Exists(existing.FullName))];

        // Only placeholders made or updated take a size, so only then are sizes over 32 bits asked for
        if (hasShortSizes && (changed.Count > 0 || added.Any(entry => entry.Type is FileType.File)))
        {
            var largeSizes = LargeFileSizes(DeviceId, devicePath, false, token);
            changed = [.. changed.Select(pair => (pair.File, WithLargeSize(pair.DeviceEntry, largeSizes)))];
            added = [.. added.Select(entry => WithLargeSize(entry, largeSizes))];
        }

        foreach (var (file, deviceEntry) in changed)
            UpdatePlaceholder(file, deviceEntry, isReadOnly);

        if (added.Count > 0)
            CreatePlaceholders(localFolder, added, isReadOnly);
    }

    /// <summary>The folder's entries the mount shows, by their Windows names; null when it can't be listed (or is gone, for its parent to remove).</summary>
    private Dictionary<string, FileStat>? ListMountable(string devicePath, CancellationToken token)
    {
        try
        {
            var filter = new MountableFilter(DriveOf(devicePath)?.MapsNames is true);
            var entries = AdbService.ListDirectoryEntries(DeviceId, devicePath, token)
                                    .Select(filter.Map)
                                    .OfType<FileStat>().ToList();

            // Nothing listed may as well be a folder that's gone, which mustn't empty it here
            if (entries.Count == 0 && !AdbService.SyncLstat(DeviceId, devicePath).IsDirectory)
                return null;

            return entries.ToDictionary(entry => entry.FullName, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception e)
        {
#if !DEPLOY
            DebugLog.PrintLine($"Mount refresh of {devicePath} couldn't list it: {e.Message}");
#endif
            return null;
        }
    }

    /// <summary>A placeholder in sync, and not the drive's icon file, nor in the middle of a save, upload or download.</summary>
    private bool IsRefreshable(FileSystemInfo entry)
    {
        if (!IsPlaceholder(entry.Attributes) || _setAside.ContainsKey(entry.FullName) || _uploading.ContainsKey(entry.FullName) || IsHydrating(entry.FullName))
            return false;

        if (ToDevicePath(entry.FullName) is not { } devicePath || IsDriveFolderOrIcon(devicePath))
            return false;

        return entry is DirectoryInfo || IsInSync(entry.FullName) is true;
    }

    /// <param name="hasShortSizes">The listing keeps 32 bits of a size, which a file of 4 GiB or more still matches in.</param>
    private static bool HasChanged(FileInfo file, FileStat deviceEntry, bool hasShortSizes)
    {
        var localSize = file.Length;
        if (hasShortSizes)
            localSize &= uint.MaxValue;

        if (localSize != (deviceEntry.Size ?? 0))
            return true;

        return deviceEntry.ModifiedTime is { } modified
            && (file.LastWriteTimeUtc - modified.ToUniversalTime()).Duration() > MODIFIED_TOLERANCE;
    }

    /// <summary>
    /// Deletes a placeholder gone from the device, which the delete callback then leaves be. Of a folder holding what's only
    /// here, just the rest goes, for that to be pushed (which makes the folder on the device again).
    /// </summary>
    private void RemovePlaceholder(FileSystemInfo entry)
    {
        if (entry is DirectoryInfo directory && HasLocalOnlyContent(directory))
        {
#if !DEPLOY
            DebugLog.PrintLine($"Mount refresh kept {entry.FullName}, gone from the device: it holds what isn't pushed");
#endif
            RemoveDeviceCopies(directory);
            return;
        }

        DeleteLocal(entry);
    }

    /// <summary>Removes what the folder holds that the device had, keeping what's only here and the folders on the way to it.</summary>
    private void RemoveDeviceCopies(DirectoryInfo folder)
    {
        List<FileSystemInfo> entries;
        try
        {
            entries = [.. folder.EnumerateFileSystemInfos()];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var entry in entries)
        {
            if (entry is DirectoryInfo subfolder && IsPlaceholder(subfolder.Attributes) && HasLocalOnlyContent(subfolder))
                RemoveDeviceCopies(subfolder);
            else if (IsDeviceCopy(entry))
                DeleteLocal(entry);
        }
    }

    private void DeleteLocal(FileSystemInfo entry)
    {
        _selfDeletes[entry.FullName] = 0;
        try
        {
            entry.Attributes &= ~FileAttributes.ReadOnly;
            if (entry is DirectoryInfo folder)
                folder.Delete(true);
            else
                entry.Delete();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Open in an app, say: tried again on the next refresh
#if !DEPLOY
            DebugLog.PrintLine($"Mount refresh couldn't remove {entry.FullName}: {e.Message}");
#endif
        }
        finally
        {
            _selfDeletes.TryRemove(entry.FullName, out _);
        }
    }

    /// <summary>
    /// Whether anything in the folder is only here: not pushed yet, edited, or in the middle of a save, upload or download.
    /// Skipped files (folder settings, an open document's lock file) are never pushed, so they don't count.
    /// </summary>
    private bool HasLocalOnlyContent(DirectoryInfo folder)
    {
        try
        {
            return folder.EnumerateFileSystemInfos("*", SearchOption.AllDirectories).Any(entry => !IsDeviceCopy(entry));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>Whether the entry holds nothing the device lacks, so removing it loses nothing.</summary>
    private bool IsDeviceCopy(FileSystemInfo entry)
    {
        if (!IsPlaceholder(entry.Attributes))
            return IsSkipped(entry.FullName, entry.Attributes);

        if (_setAside.ContainsKey(entry.FullName) || _uploading.ContainsKey(entry.FullName) || IsHydrating(entry.FullName))
            return false;

        return entry is DirectoryInfo || IsInSync(entry.FullName) is true;
    }

    /// <summary>A file changed on the device: its size and times taken, and its content dropped, to be pulled anew when opened.</summary>
    private static void UpdatePlaceholder(FileInfo file, FileStat deviceEntry, bool isReadOnly)
    {
        var metadata = CreateInfo(deviceEntry with { FullName = file.Name }, 0, isReadOnly).FsMetadata;
        var pMetadata = Marshal.AllocHGlobal(Marshal.SizeOf<CF_FS_METADATA>());
        var hr = CfOpenFileWithOplock(file.FullName, CF_OPEN_FILE_FLAGS.CF_OPEN_FILE_FLAG_WRITE_ACCESS | CF_OPEN_FILE_FLAGS.CF_OPEN_FILE_FLAG_EXCLUSIVE, out var handle);
        try
        {
            if (hr.Succeeded)
            {
                using (handle)
                {
                    // No identity or USN given: the identity stays, and no USN check
                    Marshal.StructureToPtr(metadata, pMetadata, false);
                    hr = CfUpdatePlaceholder(handle, pMetadata, IntPtr.Zero, 0, null, 0,
                        CF_UPDATE_FLAGS.CF_UPDATE_FLAG_MARK_IN_SYNC | CF_UPDATE_FLAGS.CF_UPDATE_FLAG_DEHYDRATE, IntPtr.Zero, IntPtr.Zero);
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(pMetadata);
        }

#if !DEPLOY
        if (hr.Failed)
            DebugLog.PrintLine($"Mount refresh couldn't update {file.FullName}: {hr}");
#endif
    }
}
