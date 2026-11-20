using Vanara.PInvoke;
using Vanara.Windows.Shell;
using FileAttributes = System.IO.FileAttributes;
using static Vanara.PInvoke.CldApi;

namespace ADB_Explorer.Services;

// Files and folders made in the mount (copied in, saved, created in File Explorer) have no placeholder, and no callback
// tells of them or of edits to placeholders. A watcher finds both; once left alone for a moment, each new tree or edited
// file is pushed as one of the app's own operations, and made (or marked) an in-sync placeholder when that completes.
public sealed partial class DeviceMount
{
    // How long a new tree must go unchanged before it's pushed, so a copy in progress isn't pushed halfway
    private static readonly TimeSpan UPLOAD_QUIET_PERIOD = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan UPLOAD_POLL = TimeSpan.FromMilliseconds(250);

    private const int WATCHER_BUFFER_SIZE = 64 * 1024;

    // Folder settings and thumbnail caches File Explorer writes, and the lock files apps keep beside an open document
    private static readonly string[] SKIPPED_NAMES = [FOLDER_SETTINGS_FILE, "Thumbs.db"];

    private static readonly string[] SKIPPED_PREFIXES = ["~$", ".~lock."];

    private const string TEMP_EXTENSION = ".tmp";

    // The backup copy editors such as gedit and Vim keep beside a file
    private const string BACKUP_SUFFIX = "~";

    private FileSystemWatcher? _watcher;

    private Thread? _uploadThread;

    private readonly CancellationTokenSource _uploadCancellation = new();

    private readonly AutoResetEvent _uploadSignal = new(false);

    /// <summary>Local paths the watcher reported, by when they last changed.</summary>
    private readonly ConcurrentDictionary<string, DateTime> _changedPaths = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Pushes that finished, for the upload thread to make their trees placeholders.</summary>
    private readonly ConcurrentQueue<Action> _finishedUploads = new();

    /// <summary>The roots of new trees being pushed, left alone until they're placeholders.</summary>
    private readonly ConcurrentDictionary<string, byte> _uploading = new(StringComparer.OrdinalIgnoreCase);

    private volatile bool _isRescanNeeded;

    /// <summary>A file's size and modified time, to tell whether it changed while it was being pushed.</summary>
    private readonly record struct LocalVersion(long Size, DateTime Modified);

    private void StartUploads()
    {
        _watcher = new FileSystemWatcher(LocalRoot)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
            InternalBufferSize = WATCHER_BUFFER_SIZE,
        };
        _watcher.Created += OnLocalChange;
        _watcher.Changed += OnLocalChange;
        _watcher.Renamed += OnLocalChange;

        // Changes were lost: the whole mount is searched for new items instead
        _watcher.Error += (_, _) =>
        {
            _isRescanNeeded = true;
            _uploadSignal.Set();
        };

        _uploadThread = new Thread(RunUploads)
        {
            IsBackground = true,
            Name = "Mount uploads",
        };
        _uploadThread.Start();

        _watcher.EnableRaisingEvents = true;
    }

    private void OnLocalChange(object sender, FileSystemEventArgs e)
    {
        _changedPaths[e.FullPath] = DateTime.UtcNow;
        _uploadSignal.Set();
    }

    /// <summary>Stops watching and pushing, before the connection goes. Pushes already queued run on, and their trees stay plain files.</summary>
    private void StopUploads()
    {
        _watcher?.Dispose();
        _watcher = null;

        _uploadCancellation.Cancel();
        _uploadSignal.Set();
        _uploadThread?.Join(POPULATION_STOP_WAIT_MS);
    }

    private void RunUploads()
    {
        // Placeholders are disguised as plain files by default, which would make every one of them look new
        NativeMethods.RtlSetThreadPlaceholderCompatibilityMode(NativeMethods.PHCM_EXPOSE_PLACEHOLDERS);

        var token = _uploadCancellation.Token;
        Dictionary<string, DateTime> pending = new(StringComparer.OrdinalIgnoreCase);

        while (!token.IsCancellationRequested)
        {
            try
            {
                while (_finishedUploads.TryDequeue(out var finish))
                    finish();

                if (_isRescanNeeded)
                {
                    _isRescanNeeded = false;
                    foreach (var item in FindNewItems())
                        pending[item] = DateTime.UtcNow;
                }

                CollectChanges(pending);
                UploadQuietItems(pending, token);
                ResolveSetAside();
                RunRefreshes(token);
            }
            catch (Exception e)
            {
#if !DEPLOY
                DebugLog.PrintLine($"Mount uploads failed: {e.Message}");
#endif
            }

            // Otherwise back for the next refresh of the folders open in File Explorer
            var isWaiting = pending.Count > 0 || !_setAside.IsEmpty || HasRefreshes;
            _uploadSignal.WaitOne(isWaiting ? UPLOAD_POLL : WINDOW_REFRESH_INTERVAL);
        }
    }

    /// <summary>Takes the watcher's paths as the new trees they're in, or the edited files they are, each timed by its latest change.</summary>
    private void CollectChanges(Dictionary<string, DateTime> pending)
    {
        foreach (var (path, time) in _changedPaths)
        {
            // Changed again since, it stays for the next round
            if (!_changedPaths.TryRemove(KeyValuePair.Create(path, time)))
                continue;

            var newItem = NewItemOf(path);

            // When it appears, not once pushed: an app may keep a saved file open (and unpushable) for long
            if (newItem is not null)
                MarkReplaced(ToDevicePath(newItem));

            if ((newItem ?? EditedFileOf(path)) is not { } item || _uploading.ContainsKey(item))
                continue;

            if (!pending.TryGetValue(item, out var latest) || latest < time)
                pending[item] = time;
        }
    }

    private void UploadQuietItems(Dictionary<string, DateTime> pending, CancellationToken token)
    {
        var now = DateTime.UtcNow;
        foreach (var (item, time) in pending.Where(entry => now - entry.Value >= UPLOAD_QUIET_PERIOD).ToList())
        {
            if (token.IsCancellationRequested)
                return;

            // Still open for writing somewhere, or being downloaded: given another quiet period
            if (IsHydrating(item) || IsBeingWritten(item))
            {
                pending[item] = now;
                continue;
            }

            pending.Remove(item);
            Upload(item);
        }
    }

    /// <summary>
    /// The topmost new item on the way to <paramref name="localPath"/>: the root of the tree to push. Null for a placeholder,
    /// for what's gone or skipped, and for the mount root's own entries, which aren't on any drive.
    /// </summary>
    private string? NewItemOf(string localPath)
    {
        if (RelativeToRoot(localPath) is not { } relative || relative == ".")
            return null;

        var parts = relative.Split(Path.DirectorySeparatorChar);
        var current = Path.Combine(LocalRoot, parts[0]);
        foreach (var part in parts.Skip(1))
        {
            current = Path.Combine(current, part);

            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(current);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return null;
            }

            if (!IsPlaceholder(attributes))
                return IsSkipped(current, attributes) ? null : current;
        }

        return null;
    }

    /// <summary>
    /// A placeholder file edited here: Windows takes it out of sync when its content changes. Null for anything else,
    /// including the writes that hydrate a file, which leave it in sync.
    /// </summary>
    private string? EditedFileOf(string localPath)
    {
        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(localPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        if (!IsPlaceholder(attributes) || attributes.HasFlag(FileAttributes.Directory) || IsSkipped(localPath, attributes) || ToDevicePath(localPath) is null)
            return null;

        // Its download writing to it, without opening it (which could wait for the download)
        if (IsHydrating(localPath))
            return null;

        return IsInSync(localPath) is true ? null : localPath;
    }

    /// <summary>A placeholder's in-sync state; null when it can't be read, as while an app holds the file exclusively.</summary>
    private static bool? IsInSync(string path)
    {
        try
        {
            // Opening a placeholder doesn't hydrate it, only reading its content does
            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var info = CfGetPlaceholderInfo<CF_PLACEHOLDER_BASIC_INFO>(new HFILE(handle.DangerousGetHandle()));

            return info.InSyncState is CF_IN_SYNC_STATE.CF_IN_SYNC_STATE_IN_SYNC;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool IsPlaceholder(FileAttributes attributes)
        => (attributes & (FileAttributes.ReparsePoint | RECALL_ON_OPEN | RECALL_ON_DATA_ACCESS)) != 0;

    /// <summary>Temporary files, and what File Explorer and apps keep beside the user's files.</summary>
    private static bool IsSkipped(string path, FileAttributes attributes)
    {
        var name = Path.GetFileName(path);

        return attributes.HasFlag(FileAttributes.Temporary)
            || SKIPPED_NAMES.Contains(name, StringComparer.OrdinalIgnoreCase)
            || (!attributes.HasFlag(FileAttributes.Directory) && IsTemporaryName(name));
    }

    /// <summary>New items and edited files anywhere in the mount, for when the watcher lost track. Folders never listed hold neither.</summary>
    private List<string> FindNewItems()
    {
        List<string> result = [];
        foreach (var drive in _drives)
            FindNewItems(Path.Combine(LocalRoot, drive.FolderName), result);

        return result;
    }

    private void FindNewItems(string folder, List<string> result)
    {
        try
        {
            foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos())
            {
                if (!IsPlaceholder(entry.Attributes))
                {
                    if (!IsSkipped(entry.FullName, entry.Attributes))
                        result.Add(entry.FullName);
                }
                else if (entry is DirectoryInfo && !entry.Attributes.HasFlag(RECALL_ON_OPEN))
                {
                    FindNewItems(entry.FullName, result);
                }
                else if (entry is FileInfo && !IsSkipped(entry.FullName, entry.Attributes) && !IsHydrating(entry.FullName) && IsInSync(entry.FullName) is false)
                {
                    result.Add(entry.FullName);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { }
    }

    /// <summary>Whether a file of the tree can't be opened while denying others write access: a copy or save is still writing it.</summary>
    private static bool IsBeingWritten(string item)
    {
        try
        {
            IEnumerable<string> files = [item];
            if (Directory.Exists(item))
                files = Directory.EnumerateFiles(item, "*", SearchOption.AllDirectories);

            foreach (var file in files)
            {
                using var _ = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            }

            return false;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            // Unreadable, the push will fail and say so
            return false;
        }
    }

    /// <summary>
    /// Pushes a new tree, or an edited file, to the same place on the device, where the app itself may change it.
    /// Under the device's own name, which a name shown as a lookalike (<see cref="FileNameEncoding"/>) isn't.
    /// </summary>
    private void Upload(string item)
    {
        if (!Path.Exists(item))
            return;

        var devicePath = ToDevicePath(item);
        if (devicePath is null || !CanChange(devicePath))
        {
#if !DEPLOY
            DebugLog.PrintLine($"Mount can't upload {item}: not a place the app changes");
#endif
            return;
        }

        // A placeholder only goes up when known to be edited: not one saved back as it was, already pushed,
        // or whose state can't be read, as pushing that could replace the device's file with an incomplete copy
        if (IsPlaceholder(File.GetAttributes(item)) && IsInSync(item) is not false)
            return;

        var versions = LocalVersions(item);
        ShellItem shellItem;
        try
        {
            shellItem = ShellItem.Open(item);
        }
        catch (Exception e)
        {
#if !DEPLOY
            DebugLog.PrintLine($"Mount can't upload {item}: {e.Message}");
#endif
            return;
        }

        _uploading[item] = 0;
        var operation = FileActionLogic.PushShellObject(shellItem, FileHelper.GetParentPath(devicePath), device: Device, targetName: FileHelper.GetFullName(devicePath));
        if (operation is null)
        {
            // Nothing was queued: either it's on the device already, or it couldn't be read (or the app is closing)
            try
            {
                if (IsOnDevice(item, devicePath, versions))
                    ConvertTree(item, versions);
            }
            finally
            {
                _uploading.TryRemove(item, out _);
            }

            return;
        }

        // Every run is reported, so retrying a failed push from the app's queue still ends in placeholders
        AddOwnOperations([operation]);
        operation.WhenFinished(status => QueueFinishedUpload(() => OnUploadFinished(item, versions, status)));
        if (IsFinal(operation.Status))
            QueueFinishedUpload(() => OnUploadFinished(item, versions, operation.Status));
    }

    /// <summary>Whether the device has the item already: a folder, or a file of the same size, not one an edit hasn't reached.</summary>
    private bool IsOnDevice(string item, string devicePath, Dictionary<string, LocalVersion?> versions)
    {
        var stat = AdbService.SyncLstat(DeviceId, devicePath);
        if (!stat.Exists)
            return false;

        if (versions.GetValueOrDefault(item) is { } version)
            return stat.IsRegularFile && stat.Size == version.Size;

        return stat.IsDirectory;
    }

    /// <summary>For the upload thread, woken for it: it may be waiting for nothing but changes.</summary>
    private void QueueFinishedUpload(Action finish)
    {
        _finishedUploads.Enqueue(finish);
        _uploadSignal.Set();
    }

    private void OnUploadFinished(string item, Dictionary<string, LocalVersion?> versions, FileOperation.OperationStatus status)
    {
        // Left a plain file (or out of sync), to push again when it changes next
        if (status is not FileOperation.OperationStatus.Completed)
        {
            _uploading.TryRemove(item, out _);
            return;
        }

        var isPartial = !ConvertTree(item, versions);
        _uploading.TryRemove(item, out _);
        if (!isPartial)
            return;

        // What changed meanwhile goes up as trees of its own: under a folder that's a placeholder now, each one left
        List<string> left = [];
        if (Directory.Exists(item) && IsPlaceholder(File.GetAttributes(item)))
            FindNewItems(item, left);
        else
            left.Add(item);

        var now = DateTime.UtcNow;
        foreach (var path in left)
            _changedPaths[path] = now;

        _uploadSignal.Set();
    }

    /// <summary>Each file's version, and each folder (with no version), as they're about to be pushed.</summary>
    private static Dictionary<string, LocalVersion?> LocalVersions(string item)
    {
        Dictionary<string, LocalVersion?> result = new(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (new DirectoryInfo(item) is { Exists: true } root)
            {
                result[item] = null;
                foreach (var entry in root.EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
                    result[entry.FullName] = entry is FileInfo file ? new LocalVersion(file.Length, file.LastWriteTimeUtc) : null;
            }
            else if (new FileInfo(item) is { Exists: true } file)
            {
                result[item] = new LocalVersion(file.Length, file.LastWriteTimeUtc);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { }

        return result;
    }

    /// <summary>
    /// Makes a pushed tree in-sync placeholders, folders before what's in them, or marks a pushed edit in sync.
    /// What changed or appeared since it was pushed is left as it is.
    /// </summary>
    /// <returns>Whether all of it is in sync now.</returns>
    private static bool ConvertTree(string path, Dictionary<string, LocalVersion?> versions)
    {
        if (!versions.TryGetValue(path, out var version))
            return false;

        if (version is { } pushed)
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length != pushed.Size || file.LastWriteTimeUtc != pushed.Modified)
                return false;

            return MakeInSync(path, IsPlaceholder(file.Attributes));
        }

        if (!Directory.Exists(path) || !MakeInSync(path, false))
            return false;

        var isComplete = true;
        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(path))
                isComplete &= ConvertTree(entry, versions);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        return isComplete;
    }

    /// <summary>
    /// A plain file or folder made an in-sync placeholder, keeping its content, or a placeholder marked in sync.
    /// Fails while another app has it open, which the exclusive open (an oplock, not a share-none handle) finds without
    /// disturbing that app. A folder is made fully populated: it holds everything it has on the device.
    /// </summary>
    private static bool MakeInSync(string path, bool isPlaceholder)
    {
        var hr = CfOpenFileWithOplock(path, CF_OPEN_FILE_FLAGS.CF_OPEN_FILE_FLAG_WRITE_ACCESS | CF_OPEN_FILE_FLAGS.CF_OPEN_FILE_FLAG_EXCLUSIVE, out var handle);
        if (hr.Succeeded)
        {
            using (handle)
            {
                if (isPlaceholder)
                    hr = CfSetInSyncState(handle, CF_IN_SYNC_STATE.CF_IN_SYNC_STATE_IN_SYNC, CF_SET_IN_SYNC_FLAGS.CF_SET_IN_SYNC_FLAG_NONE, IntPtr.Zero);
                else
                    hr = CfConvertToPlaceholder(handle, PLACEHOLDER_IDENTITY, 1, CF_CONVERT_FLAGS.CF_CONVERT_FLAG_MARK_IN_SYNC, out _, IntPtr.Zero);
            }
        }

#if !DEPLOY
        if (hr.Failed)
            DebugLog.PrintLine($"Mount couldn't mark {path} in sync: {hr}");
#endif

        return hr.Succeeded;
    }
}
