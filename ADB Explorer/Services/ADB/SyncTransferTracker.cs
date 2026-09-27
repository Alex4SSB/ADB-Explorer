namespace ADB_Explorer.Services;

internal static class SyncTransferTracker
{
    private static long _pullBytes;
    private static long _pushBytes;
    private static DateTime _lastSnapshot = DateTime.UtcNow;
    private static DateTime _lastTransferUtc = DateTime.MinValue;

    public static void AddPullBytes(long bytes)
    {
        if (bytes > 0)
            NoteTransfer();
        Interlocked.Add(ref _pullBytes, bytes);
    }

    public static void AddPushBytes(long bytes)
    {
        if (bytes > 0)
            NoteTransfer();
        Interlocked.Add(ref _pushBytes, bytes);
    }

    public static bool HasRecentActivity(TimeSpan window) =>
        _lastTransferUtc != DateTime.MinValue && DateTime.UtcNow - _lastTransferUtc < window;

    private static void NoteTransfer()
    {
        _lastTransferUtc = DateTime.UtcNow;
        DiskUsagePollingService.LastServerResponse = DateTime.Now;
    }

    /// <summary>
    /// Atomically resets the byte counters and returns bytes/s since the last call.
    /// </summary>
    public static (long ReadBytesPerSec, long WriteBytesPerSec) Snapshot()
    {
        var now = DateTime.UtcNow;
        var elapsed = (now - _lastSnapshot).TotalSeconds;
        _lastSnapshot = now;

        var pull = Interlocked.Exchange(ref _pullBytes, 0);
        var push = Interlocked.Exchange(ref _pushBytes, 0);

        if (elapsed <= 0)
            return (0, 0);

        return ((long)(pull / elapsed), (long)(push / elapsed));
    }
}
