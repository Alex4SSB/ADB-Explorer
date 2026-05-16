namespace ADB_Explorer.Services;

internal static class SyncTransferTracker
{
    private static long pullBytes;
    private static long pushBytes;
    private static DateTime lastSnapshot = DateTime.UtcNow;

    public static void AddPullBytes(long bytes) => Interlocked.Add(ref pullBytes, bytes);
    public static void AddPushBytes(long bytes) => Interlocked.Add(ref pushBytes, bytes);

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
