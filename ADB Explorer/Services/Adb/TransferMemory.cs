namespace ADB_Explorer.Services;

/// <summary>
/// Memory that pipes reading ahead of the shell share, up to <see cref="AppSettings.TransferMemoryMB"/>.
/// None is lent while it would leave the system short, so a busy machine isn't pushed into paging.
/// </summary>
public static partial class TransferMemory
{
    // Free physical memory kept for everything else: at least this much, or a tenth of it all
    private const long MinFreeBytes = 1024L * 1024 * 1024;

    private static readonly Lock _lock = new();
    private static long _reserved;

    public static long Reserved
    {
        get
        {
            lock (_lock)
                return _reserved;
        }
    }

    public static bool TryReserve(long bytes)
    {
        var limit = (long)Math.Max(0, Data.Settings.TransferMemoryMB) * 1024 * 1024;

        lock (_lock)
        {
            if (_reserved + bytes > limit || !IsMemoryAvailable(bytes))
                return false;

            _reserved += bytes;

            return true;
        }
    }

    public static void Release(long bytes)
    {
        lock (_lock)
            _reserved -= bytes;
    }

    private static bool IsMemoryAvailable(long bytes)
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref status))
            return false;

        var keepFree = Math.Max(MinFreeBytes, (long)(status.ullTotalPhys / 10));

        return (long)status.ullAvailPhys - bytes >= keepFree;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [LibraryImport("Kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
}
