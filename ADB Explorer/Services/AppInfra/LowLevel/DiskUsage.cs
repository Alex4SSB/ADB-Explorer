using ADB_Explorer.Converters;
using ADB_Explorer.Models;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Services;

public class DiskUsage : ViewModelBase
{
    public DateTime TimeStamp { get; set; }

    public Process Process { get; }

    public int? PID
    {
        get
        {
            try
            {
                return Process?.Id;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }
    }

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
    public string ReadString => (ReadRate is null || ReadRate > AdbExplorerConst.MAX_DISK_DISPLAY_RATE ? 0 : ReadRate.Value).BytesToSize(true) + "/s";

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
    public string WriteString => (WriteRate is null || WriteRate > AdbExplorerConst.MAX_DISK_DISPLAY_RATE ? 0 : WriteRate.Value).BytesToSize(true) + "/s";

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

    public string OtherString => (OtherRate is null || OtherRate > AdbExplorerConst.MAX_DISK_DISPLAY_RATE ? 0 : OtherRate.Value).BytesToSize(true) + "/s";

    public DiskUsage(Process process, ulong? readRate = null, ulong? writeRate = null, ulong? otherRate = null, DateTime? time = null)
    {
        Process = process;
        ReadRate = readRate;
        WriteRate = writeRate;
        OtherRate = otherRate;

        TimeStamp = time ?? DateTime.Now;
    }

    public DiskUsage(ulong? readRate, ulong? writeRate, ulong? otherRate, DateTime? time = null)
        : this(null, readRate, writeRate, otherRate, time)
    {

    }

    public void Update(DiskUsage other)
    {
        if (other is null || other.PID != PID)
            return;

        ReadRate = other.ReadRate;
        WriteRate = other.WriteRate;
        OtherRate = other.OtherRate;

        TimeStamp = DateTime.Now;
    }

    public static DiskUsage Consolidate(IEnumerable<DiskUsage> list)
    {
        if (!list.Any())
            return null;

        var time = list.Max(u => u.TimeStamp);

        var read = (ulong)list.Sum(u => (decimal)u.ReadRate);
        var write = (ulong)list.Sum(u => (decimal)u.WriteRate);
        var other = (ulong)list.Sum(u => (decimal)u.OtherRate);

        return new(read, write, other, time);
    }

    public DiskUsage Subtract(DiskUsage other)
    {
        var timeDelta = (TimeStamp - other.TimeStamp).TotalSeconds;

        var totalRead = (ulong)((ReadRate - other.ReadRate) / timeDelta);
        var totalWrite = (ulong)((WriteRate - other.WriteRate) / timeDelta);
        var totalOther = (ulong)((OtherRate - other.OtherRate) / timeDelta);

        return new(totalRead, totalWrite, totalOther);
    }
}

internal static class DiskUsageHelper
{
    
    private static DiskUsage GetDiskUsage(Process process)
    {
        try
        {
            var counters = NativeMethods.GetProcessIoCounters(process.Handle);

            return new(process, counters.ReadTransferCount, counters.WriteTransferCount, counters.OtherTransferCount);
        }
        catch
        {
            return null;
        }
    }

    private static Process[] GetAdbProcs() =>
        Process.GetProcessesByName(AdbExplorerConst.ADB_PROCESS);

    private static DiskUsage prevUsage;

    private static DateTime LastUpdate = DateTime.MinValue;

    public static void GetAdbDiskUsage()
    {
        var newUsages = GetAdbProcs().Select(GetDiskUsage).Where(usage => usage is not null);

        if (!newUsages.Any())
            return;

        var syncUsages = Data.FileOpQ.Operations
            .OfType<FileSyncOperation>()
            .Where(op => op.Status is FileOperation.OperationStatus.InProgress)
            .Select(op => op.AdbProcess);

        foreach (var usage in syncUsages)
        {
            if (usage is null || usage.Process is null)
                continue;
            try
            {
                var matchingProcess = newUsages.FirstOrDefault(proc => proc.PID == usage.PID);

                if (matchingProcess != null)
                {
                    usage.Update(matchingProcess);
                }
            }
            catch (InvalidOperationException)
            {
            }
        }

        var newUsage = DiskUsage.Consolidate(newUsages);

        if (newUsage is null)
            return;

        if (prevUsage is not null && DateTime.Now - LastUpdate >= AdbExplorerConst.DISK_USAGE_INTERVAL_IDLE)
        {
            var totalUsage = newUsage.Subtract(prevUsage);

            App.Current.Dispatcher.Invoke(() =>
            {
                Data.RuntimeSettings.AdbReadRate = totalUsage.ReadString;
                Data.RuntimeSettings.AdbWriteRate = totalUsage.WriteString;
                Data.RuntimeSettings.AdbOtherRate = totalUsage.OtherString;

                Data.RuntimeSettings.IsAdbReadActive = totalUsage.IsReadActive;
                Data.RuntimeSettings.IsAdbWriteActive = totalUsage.IsWriteActive;
            });

            LastUpdate = DateTime.Now;
        }

        prevUsage = newUsage;
    }
}
