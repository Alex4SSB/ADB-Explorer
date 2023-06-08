using ADB_Explorer.Converters;
using ADB_Explorer.Services;

namespace ADB_Explorer.ViewModels;

public class CompletedSyncProgressViewModel : FileOpProgressViewModel
{
    private readonly AdbSyncStatsInfo adbInfo;

    public CompletedSyncProgressViewModel(AdbSyncStatsInfo adbInfo) : base(Services.FileOperation.OperationStatus.Completed)
    {
        this.adbInfo = adbInfo;
    }

    public UInt64 FilesTransferred => adbInfo.FilesTransferred;

    public UInt64 FilesSkipped => adbInfo.FilesSkipped;

    public decimal? AverageRateMBps => adbInfo.AverageRate;

    public UInt64? TotalBytes => adbInfo.TotalBytes;

    public decimal? TotalSeconds => adbInfo.TotalTime;

    public int FileCountCompletedRate => (int)((float)FilesTransferred / (FilesTransferred + FilesSkipped) * 100.0);

    public string FileCountCompletedString => $"{FilesTransferred} of {FilesTransferred + FilesSkipped}";

    public string AverageRateString => AverageRateMBps.HasValue ? $"{UnitConverter.ToSize((UInt64)(AverageRateMBps.Value * 1024 * 1024))}/s" : string.Empty;

    public string TotalSize => TotalBytes.HasValue ? UnitConverter.ToSize(TotalBytes.Value) : string.Empty;

    public string TotalTime => TotalSeconds.HasValue ? UnitConverter.ToTime(TotalSeconds.Value) : string.Empty;
}
