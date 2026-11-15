using Vanara.PInvoke;
using static Vanara.PInvoke.CldApi;

namespace ADB_Explorer.Services;

// Folders are listed progressively: the platform's request is answered with what arrives first,
// and the rest is added while File Explorer already shows the folder, which is then marked complete.
public sealed partial class DeviceMount
{
    // How long the platform's request waits for entries, and how often later ones are added
    private static readonly TimeSpan FIRST_BATCH_WAIT = TimeSpan.FromMilliseconds(100);

    private static readonly TimeSpan BATCH_WAIT = TimeSpan.FromMilliseconds(150);

    private const int MAX_BATCH = 500;

    private const int POPULATION_STOP_WAIT_MS = 2000;

    /// <summary>Folders still being filled, by local path, with the thread filling each once it's started.</summary>
    private readonly ConcurrentDictionary<string, Thread?> _populating = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Folders filled and marked complete, for requests the platform queued before the mark.</summary>
    private readonly ConcurrentDictionary<string, byte> _populated = new(StringComparer.OrdinalIgnoreCase);

    private readonly CancellationTokenSource _populationCancellation = new();

    /// <summary>A device listing read on a thread of its own, so entries can be taken as they arrive.</summary>
    private sealed class FolderListing
    {
        private readonly BlockingCollection<FileStat> _entries = [];

        private readonly MountableFilter _filter;

        public bool Failed { get; private set; }

        public FolderListing(string deviceId, string devicePath, bool mapNames, CancellationToken cancellationToken)
        {
            _filter = new(mapNames);
            new Thread(() =>
            {
                try
                {
                    foreach (var entry in AdbService.ListDirectoryEntries(deviceId, devicePath, cancellationToken))
                        _entries.Add(entry, cancellationToken);
                }
                catch (OperationCanceledException)
                { }
                catch (Exception e)
                {
#if !DEPLOY
                    DebugLog.PrintLine($"Mount listing of {devicePath} failed: {e.Message}");
#endif
                    Failed = true;
                }
                finally
                {
                    _entries.CompleteAdding();
                }
            })
            {
                IsBackground = true,
                Name = "Mount listing",
            }.Start();
        }

        /// <summary>The mountable entries that arrive within <paramref name="wait"/>, up to <see cref="MAX_BATCH"/>.</summary>
        /// <param name="isDone">The listing has ended, and nothing of it is left.</param>
        public List<FileStat> TakeBatch(TimeSpan wait, CancellationToken cancellationToken, out bool isDone)
        {
            List<FileStat> batch = [];
            var deadline = DateTime.UtcNow + wait;
            isDone = false;

            while (batch.Count < MAX_BATCH)
            {
                var remaining = deadline - DateTime.UtcNow;
                if (remaining < TimeSpan.Zero)
                    remaining = TimeSpan.Zero;

                if (_entries.TryTake(out var entry, (int)remaining.TotalMilliseconds, cancellationToken))
                {
                    if (_filter.Map(entry) is { } mapped)
                        batch.Add(mapped);

                    continue;
                }

                isDone = _entries.IsCompleted;
                break;
            }

            return batch;
        }
    }

    private void OnFetchPlaceholders(in CF_CALLBACK_INFO info, in CF_CALLBACK_PARAMETERS parameters)
    {
        var opInfo = OperationInfo(info, CF_OPERATION_TYPE.CF_OPERATION_TYPE_TRANSFER_PLACEHOLDERS);
        var localPath = info.VolumeDosName + info.NormalizedPath;
        if (RelativeToRoot(localPath) == ".")
        {
            TransferDriveFolders(opInfo);
            return;
        }

        var devicePath = ToDevicePath(localPath);
        if (devicePath is null)
        {
            TransferPlaceholders(opInfo, [], NTStatus.STATUS_OBJECT_PATH_NOT_FOUND, true);
            return;
        }

        // Claimed before listing, as the platform can ask for a folder twice at once.
        // A request for one still being filled gets nothing: what's there so far is all there is for now.
        if (_populated.ContainsKey(localPath))
        {
            TransferPlaceholders(opInfo, [], NTStatus.STATUS_SUCCESS, true);
            return;
        }

        if (!_populating.TryAdd(localPath, null))
        {
            TransferPlaceholders(opInfo, [], NTStatus.STATUS_SUCCESS, false);
            return;
        }

        var token = _populationCancellation.Token;
        var listing = new FolderListing(DeviceId, devicePath, DriveOf(devicePath)?.MapsNames is true, token);
        List<FileStat> batch;
        bool isDone;
        try
        {
            batch = listing.TakeBatch(FIRST_BATCH_WAIT, token, out isDone);
        }
        catch (OperationCanceledException)
        {
            _populating.TryRemove(localPath, out _);
            TransferPlaceholders(opInfo, [], NTStatus.STATUS_UNSUCCESSFUL, false);
            return;
        }

        if (isDone)
        {
            _populating.TryRemove(localPath, out _);
            if (listing.Failed)
                TransferPlaceholders(opInfo, [], NTStatus.STATUS_UNSUCCESSFUL, false);
            else
                TransferPlaceholders(opInfo, batch, NTStatus.STATUS_SUCCESS, true);

            return;
        }

        var thread = new Thread(() => ContinuePopulating(localPath, listing, token))
        {
            IsBackground = true,
            Name = "Mount population",
        };
        _populating[localPath] = thread;

        TransferPlaceholders(opInfo, batch, NTStatus.STATUS_SUCCESS, false);
        thread.Start();
    }

    private void ContinuePopulating(string localPath, FolderListing listing, CancellationToken cancellationToken)
    {
        try
        {
            bool isDone;
            do
            {
                var batch = listing.TakeBatch(BATCH_WAIT, cancellationToken, out isDone);
                if (batch.Count > 0)
                    CreatePlaceholders(localPath, batch);
            }
            while (!isDone);

            // Otherwise it stays incomplete, and is listed again when next opened
            if (!listing.Failed && MarkPopulated(localPath))
                _populated[localPath] = 0;
        }
        catch (OperationCanceledException)
        { }
        catch (Exception e)
        {
#if !DEPLOY
            DebugLog.PrintLine($"Mount population of {localPath} failed: {e.Message}");
#endif
        }
        finally
        {
            _populating.TryRemove(localPath, out _);
        }
    }

    private static void CreatePlaceholders(string folder, List<FileStat> entries)
    {
        var infos = entries.Select(CreateInfo).ToArray();
        var hr = CfCreatePlaceholders(folder, infos, (uint)infos.Length, CF_CREATE_FLAGS.CF_CREATE_FLAG_NONE, out _);
#if !DEPLOY
        if (hr.Failed)
        {
            var failed = infos.Where(info => info.Result.Failed).Select(info => $"{info.RelativeFileName} ({info.Result.Code:X})");
            DebugLog.PrintLine($"Mount placeholder creation in {folder} failed: {hr}; {string.Join(", ", failed)}");
        }
#endif
    }

    private static bool MarkPopulated(string folder)
    {
        var hr = CfOpenFileWithOplock(folder, CF_OPEN_FILE_FLAGS.CF_OPEN_FILE_FLAG_WRITE_ACCESS, out var handle);
        if (hr.Succeeded)
        {
            using (handle)
            {
                hr = CfUpdatePlaceholder(handle, IntPtr.Zero, IntPtr.Zero, 0, null, 0,
                    CF_UPDATE_FLAGS.CF_UPDATE_FLAG_DISABLE_ON_DEMAND_POPULATION, IntPtr.Zero, IntPtr.Zero);
            }
        }

#if !DEPLOY
        if (hr.Failed)
            DebugLog.PrintLine($"Mount couldn't mark {folder} complete: {hr}");
#endif

        return hr.Succeeded;
    }

    /// <summary>Stops filling folders, before the connection goes.</summary>
    private void StopPopulating()
    {
        _populationCancellation.Cancel();

        foreach (var thread in _populating.Values)
        {
            // Not started yet (still on its first batch, or a moment before starting): nothing to wait for
            if (thread is null || thread.ThreadState.HasFlag(System.Threading.ThreadState.Unstarted))
                continue;

            thread.Join(POPULATION_STOP_WAIT_MS);
        }
    }
}
