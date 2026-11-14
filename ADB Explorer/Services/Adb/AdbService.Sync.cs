using AdvancedSharpAdbClient;
using AdvancedSharpAdbClient.Exceptions;
using AdvancedSharpAdbClient.Models;

namespace ADB_Explorer.Services;

/// <summary>A device path's sync-protocol stat. <see cref="Exists"/> is false when it can't be stat'ed.</summary>
public readonly record struct DeviceStat(AdbService.UnixFileMode Mode, long Size, long ModifiedTime, uint? Uid = null, uint? Gid = null)
{
    public bool Exists => Mode is not AdbService.UnixFileMode.None;

    public bool IsDirectory => ((uint)Mode & AdbService.S_IFMT) == (uint)AdbService.UnixFileMode.S_IFDIR;

    public bool IsRegularFile => ((uint)Mode & AdbService.S_IFMT) == (uint)AdbService.UnixFileMode.S_IFREG;
}

/// <summary>
/// Listing and stat over the adb server's sync protocol, in process - no adb.exe or device shell per call.
/// Uses LIS2/LST2 where the device supports them, else LIST/STAT v1.
/// </summary>
public partial class AdbService
{
    public const uint S_IFMT = 0xF000;

    private const string FEATURE_STAT_V2 = "stat_v2";

    private const int MAX_POOLED_SYNC = 4;

    private const int SYNC_STAT_V1_LENGTH = 12;

    private const int SYNC_STAT_V2_LENGTH = 68;

    private static readonly ConcurrentDictionary<string, HashSet<string>> _syncFeatures = new();

    private static readonly ConcurrentDictionary<string, ConcurrentBag<SyncService>> _statPool = new();

    private static DeviceData SyncDevice(string deviceId) => new() { Serial = RouteToTransport(deviceId) };

    private static HashSet<string> SyncFeatures(string deviceId)
        => _syncFeatures.GetOrAdd(RouteToTransport(deviceId), serial => GetDeviceFeatures(new DeviceData { Serial = serial }));

    /// <summary>Lists a folder's entries (without . and ..) over the sync protocol.</summary>
    public static IEnumerable<FileStat> SyncListDirectory(string deviceId, string path, CancellationToken cancellationToken)
    {
        var lsV2 = SyncFeatures(deviceId).Contains(FEATURE_LS_V2);

        // A listing leaves the library's socket needing a reopen, so each one gets its own
        using var service = new SyncService(SyncDevice(deviceId));
        IEnumerable<IFileStatistics> entries = lsV2
            ? service.GetDirectoryListingEx(path)
            : service.GetDirectoryListing(path);

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var item = CreateFile(path, entry.Path, (long)entry.Size, entry.Time.ToUnixTimeSeconds(), (UnixFileMode)(uint)entry.FileMode);
            if (item is not null)
                yield return item.Value;
        }
    }

    /// <summary>Stats a path without following a final symlink (like toybox <c>stat</c> without <c>-L</c>).</summary>
    /// <remarks>No stat-following variant: Android 9's adbd fails STA2 even on <c>/system/bin</c> applet links.</remarks>
    public static DeviceStat SyncLstat(string deviceId, string path)
    {
        // STAT v1 is an lstat too
        var command = SyncFeatures(deviceId).Contains(FEATURE_STAT_V2) ? SyncCommand.LST2 : SyncCommand.STAT;

        // A pooled socket may have died with its device or the server: retry once on a fresh one
        for (var attempt = 0; ; attempt++)
        {
            var service = RentStatService(deviceId, fresh: attempt > 0);
            try
            {
                var result = ReadSyncStat(service.Socket, command, path);
                ReturnStatService(deviceId, service);

                return result;
            }
            catch (Exception) when (attempt == 0)
            {
                service.Dispose();
            }
            catch
            {
                service.Dispose();
                throw;
            }
        }
    }

    private static DeviceStat ReadSyncStat(IAdbSocket socket, SyncCommand command, string path)
    {
        socket.SendSyncRequest(command, path);

        var response = socket.ReadSyncResponse();
        if (response != command)
            throw new AdbException($"The server returned an invalid sync response {response}.");

        if (command is SyncCommand.STAT)
        {
            var v1 = new byte[SYNC_STAT_V1_LENGTH];
            socket.Read(v1);

            // v1 is an lstat whose mode is 0 when it fails
            return new((UnixFileMode)BitConverter.ToUInt32(v1, 0), BitConverter.ToUInt32(v1, 4), BitConverter.ToUInt32(v1, 8));
        }

        // error, dev, ino, mode, nlink, uid, gid, size, atime, mtime, ctime
        var v2 = new byte[SYNC_STAT_V2_LENGTH];
        socket.Read(v2);

        if (BitConverter.ToUInt32(v2, 0) != 0)
            return default;

        return new((UnixFileMode)BitConverter.ToUInt32(v2, 20),
                   (long)BitConverter.ToUInt64(v2, 36),
                   BitConverter.ToInt64(v2, 52),
                   BitConverter.ToUInt32(v2, 28),
                   BitConverter.ToUInt32(v2, 32));
    }

    private static SyncService RentStatService(string deviceId, bool fresh)
    {
        if (!fresh
            && _statPool.TryGetValue(RouteToTransport(deviceId), out var pool)
            && pool.TryTake(out var pooled))
        {
            return pooled;
        }

        return new SyncService(SyncDevice(deviceId));
    }

    private static void ReturnStatService(string deviceId, SyncService service)
    {
        var pool = _statPool.GetOrAdd(RouteToTransport(deviceId), _ => []);
        if (pool.Count < MAX_POOLED_SYNC)
            pool.Add(service);
        else
            service.Dispose();
    }

    /// <summary>Runs a sync-protocol call; false when the adb server or device couldn't serve it, so a caller can fall back.</summary>
    public static bool TrySync<T>(Func<T> call, out T result)
    {
        try
        {
            result = call();

            return true;
        }
        catch (Exception e) when (e is AdbException or System.Net.Sockets.SocketException or IOException)
        {
#if !DEPLOY
            DebugLog.PrintLine($"Sync call failed, falling back: {e.Message}");
#endif
            result = default!;

            return false;
        }
    }

    /// <summary>Closes the pooled sync sockets of a device that went away.</summary>
    public static void ReleaseSyncConnections(string deviceId)
    {
        if (!_statPool.TryRemove(RouteToTransport(deviceId), out var pool))
            return;

        while (pool.TryTake(out var service))
            service.Dispose();
    }
}
