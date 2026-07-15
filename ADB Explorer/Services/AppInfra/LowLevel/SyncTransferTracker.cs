namespace ADB_Explorer.Services;

internal static class SyncTransferTracker
{
    private static long pullBytes;
    private static long pushBytes;
    private static DateTime lastSnapshot = DateTime.UtcNow;
    private static DateTime lastTransferUtc = DateTime.MinValue;

    public static void AddPullBytes(long bytes)
    {
        if (bytes > 0)
            NoteTransfer();
        Interlocked.Add(ref pullBytes, bytes);
    }

    public static void AddPushBytes(long bytes)
    {
        if (bytes > 0)
            NoteTransfer();
        Interlocked.Add(ref pushBytes, bytes);
    }

    public static bool HasRecentActivity(TimeSpan window) =>
        lastTransferUtc != DateTime.MinValue && DateTime.UtcNow - lastTransferUtc < window;

    private static void NoteTransfer()
    {
        lastTransferUtc = DateTime.UtcNow;
        DiskUsagePollingService.LastServerResponse = DateTime.Now;
    }

    /// <summary>
    /// Atomically resets the byte counters and returns bytes/s since the last call.
    /// </summary>
    public static (long ReadBytesPerSec, long WriteBytesPerSec) Snapshot()
    {
        var now = DateTime.UtcNow;
        var elapsed = (now - lastSnapshot).TotalSeconds;
        lastSnapshot = now;

        var pull = Interlocked.Exchange(ref pullBytes, 0);
        var push = Interlocked.Exchange(ref pushBytes, 0);

        if (elapsed <= 0)
            return (0, 0);

        return ((long)(pull / elapsed), (long)(push / elapsed));
    }
}
