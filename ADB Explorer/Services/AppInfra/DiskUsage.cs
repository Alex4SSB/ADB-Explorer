using ADB_Explorer.Converters;
using ADB_Explorer.Models;

namespace ADB_Explorer.Services;

public class DiskUsage
{
    public int PID { get; }

    public ulong? ReadRate { get; set; } = null;
    public bool IsReadActive => ReadRate > AdbExplorerConst.DISK_READ_THRESHOLD && ReadRate < AdbExplorerConst.MAX_DISK_DISPLAY_RATE;
    public string ReadString => (ReadRate is null || ReadRate > AdbExplorerConst.MAX_DISK_DISPLAY_RATE ? 0 : ReadRate.Value).ToSize(true) + "/s";

    public ulong? WriteRate { get; set; } = null;
    public bool IsWriteActive => WriteRate > AdbExplorerConst.DISK_READ_THRESHOLD && WriteRate < AdbExplorerConst.MAX_DISK_DISPLAY_RATE;
    public string WriteString => (WriteRate is null || WriteRate > AdbExplorerConst.MAX_DISK_DISPLAY_RATE ? 0 : WriteRate.Value).ToSize(true) + "/s";

    public ulong? OtherRate { get; set; } = null;
    public string OtherString => (OtherRate is null || OtherRate > AdbExplorerConst.MAX_DISK_DISPLAY_RATE ? 0 : OtherRate.Value).ToSize(true) + "/s";

    public DiskUsage(int pid, ulong? readRate = null, ulong? writeRate = null, ulong? otherRate = null)
    {
        PID = pid;
        ReadRate = readRate;
        WriteRate = writeRate;
        OtherRate = otherRate;
    }
}

internal static class DiskUsageHelper
{
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetProcessIoCounters(IntPtr ProcessHandle, out IO_COUNTERS IoCounters);

    public static DiskUsage GetDiskUsage(int pid)
    {
        Process[] processes = Process.GetProcesses();

        var process = processes.FirstOrDefault(p => p.Id == pid);
        if (process is null)
            return null;

        try
        {
            GetProcessIoCounters(process.Handle, out IO_COUNTERS counters);

            return new(pid, counters.ReadTransferCount, counters.WriteTransferCount, counters.OtherTransferCount);
        }
        catch
        {
            return null;
        }
    }

    public static IEnumerable<int> GetAdbPid() =>
        Process.GetProcessesByName(AdbExplorerConst.ADB_PROCESS).Select(p => p.Id);


    public static ulong prevRead = 0;
    public static ulong prevWrite = 0;
    public static ulong prevOther = 0;
    public static DiskUsage Usage = new(0);

    public static Mutex DiskUsageMutex = new();

    public static void GetAdbDiskUsage()
    {
        DiskUsageMutex.WaitOne(0);

        var newUsages = GetAdbPid().ToList().Select(GetDiskUsage).Where(usage => usage is not null);

        var newRead = (ulong)newUsages.Sum(u => (decimal)u.ReadRate);
        var newWrite = (ulong)newUsages.Sum(u => (decimal)u.WriteRate);
        var newOther = (ulong)newUsages.Sum(u => (decimal)u.OtherRate);

        var totalRead = newRead - prevRead;
        var totalWrite = newWrite - prevWrite;
        var totalOther = newOther - prevOther;

        Usage = new(0, totalRead, totalWrite, totalOther);

        prevRead = newRead;
        prevWrite = newWrite;
        prevOther = newOther;

        App.Current.Dispatcher.Invoke(() =>
        {
            Data.RuntimeSettings.AdbReadRate = Usage.ReadString;
            Data.RuntimeSettings.AdbWriteRate = Usage.WriteString;
            Data.RuntimeSettings.AdbOtherRate = Usage.OtherString;

            Data.RuntimeSettings.IsAdbReadActive = Usage.IsReadActive;
            Data.RuntimeSettings.IsAdbWriteActive = Usage.IsWriteActive;
        });

        DiskUsageMutex.ReleaseMutex();
    }
}
