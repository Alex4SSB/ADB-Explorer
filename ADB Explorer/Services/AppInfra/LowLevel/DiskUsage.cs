using ADB_Explorer.Converters;
using ADB_Explorer.Models;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Services;

using HANDLE = IntPtr;

public class DiskUsage : ViewModelBase
{
    public Process Process { get; }

    public int? PID => Process?.Id;

    private ulong? readRate;
    public ulong? ReadRate
    {
        get => readRate;
        set
        {
            if (Set(ref readRate, value))
            {
                OnPropertyChanged(nameof(IsReadActive));
                OnPropertyChanged(nameof(ReadString));
            }
        }
    }

    public bool IsReadActive => ReadRate > AdbExplorerConst.DISK_READ_THRESHOLD && ReadRate < AdbExplorerConst.MAX_DISK_DISPLAY_RATE;
    public string ReadString => (ReadRate is null || ReadRate > AdbExplorerConst.MAX_DISK_DISPLAY_RATE ? 0 : ReadRate.Value).ToSize(true) + "/s";

    private ulong? writeRate;
    public ulong? WriteRate
    {
        get => writeRate;
        set
        {
            if (Set(ref writeRate, value))
            {
                OnPropertyChanged(nameof(IsWriteActive));
                OnPropertyChanged(nameof(WriteString));
            }
        }
    }

    public bool IsWriteActive => WriteRate > AdbExplorerConst.DISK_READ_THRESHOLD && WriteRate < AdbExplorerConst.MAX_DISK_DISPLAY_RATE;
    public string WriteString => (WriteRate is null || WriteRate > AdbExplorerConst.MAX_DISK_DISPLAY_RATE ? 0 : WriteRate.Value).ToSize(true) + "/s";

    private ulong? otherRate;
    public ulong? OtherRate
    {
        get => otherRate;
        set
        {
            if (Set(ref otherRate, value))
            {
                OnPropertyChanged(nameof(OtherString));
            }
        }
    }

    public string OtherString => (OtherRate is null || OtherRate > AdbExplorerConst.MAX_DISK_DISPLAY_RATE ? 0 : OtherRate.Value).ToSize(true) + "/s";

    public DiskUsage(Process process, ulong? readRate = null, ulong? writeRate = null, ulong? otherRate = null)
    {
        Process = process;
        ReadRate = readRate;
        WriteRate = writeRate;
        OtherRate = otherRate;
    }

    public DiskUsage(ulong? readRate, ulong? writeRate, ulong? otherRate)
        : this(null, readRate, writeRate, otherRate)
    {

    }

    public void Update(DiskUsage other)
    {
        if (other is null || other.PID != PID)
            return;

        ReadRate = other.ReadRate;
        WriteRate = other.WriteRate;
        OtherRate = other.OtherRate;
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
    private static extern bool GetProcessIoCounters(HANDLE ProcessHandle, out IO_COUNTERS IoCounters);

    public static DiskUsage GetDiskUsage(int pid)
    {
        Process[] processes = Process.GetProcesses();

        var process = processes.FirstOrDefault(p => p.Id == pid);
        return process is null ? null : GetDiskUsage(process);
    }

    public static DiskUsage GetDiskUsage(Process process)
    {
        try
        {
            GetProcessIoCounters(process.Handle, out IO_COUNTERS counters);

            return new(process, counters.ReadTransferCount, counters.WriteTransferCount, counters.OtherTransferCount);
        }
        catch
        {
            return null;
        }
    }

    public static Process[] GetAdbProcs() =>
        Process.GetProcessesByName(AdbExplorerConst.ADB_PROCESS);


    public static ulong prevRead = 0;
    public static ulong prevWrite = 0;
    public static ulong prevOther = 0;
    public static DiskUsage Usage;

    public static void GetAdbDiskUsage()
    {
        var newUsages = GetAdbProcs().Select(GetDiskUsage).Where(usage => usage is not null);

        var syncUsages = Data.FileOpQ.Operations
            .OfType<FileSyncOperation>()
            .Where(op => op.Status is FileOperation.OperationStatus.InProgress)
            .Select(op => op.AdbProcess);

        foreach (var usage in syncUsages)
        {
            if (usage is null || usage.Process is null)
                continue;

            usage.Update(newUsages.FirstOrDefault(proc => proc.PID == usage.PID));
        }

        var newRead = (ulong)newUsages.Sum(u => (decimal)u.ReadRate);
        var newWrite = (ulong)newUsages.Sum(u => (decimal)u.WriteRate);
        var newOther = (ulong)newUsages.Sum(u => (decimal)u.OtherRate);

        var totalRead = newRead - prevRead;
        var totalWrite = newWrite - prevWrite;
        var totalOther = newOther - prevOther;

        Usage = new(totalRead, totalWrite, totalOther);

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
    }
}
