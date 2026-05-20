using ADB_Explorer.Converters;
using ADB_Explorer.Services;

namespace ADB_Explorer.ViewModels;

public class CompletedSyncProgressViewModel : FileOpProgressViewModel
{
    private readonly AdbSyncStatsInfo adbInfo;

    public CompletedSyncProgressViewModel(AdbSyncStatsInfo adbInfo) : base(FileOperation.OperationStatus.Completed)
    {
        this.adbInfo = adbInfo;
    }

    public long FilesTransferred => adbInfo.FilesTransferred;

    public long FilesSkipped => adbInfo.FilesSkipped;

    public double? AverageRateMBps => adbInfo.AverageRate;

    public long? TotalBytes => adbInfo.TotalBytes;

    public double? TotalSeconds => adbInfo.TotalTime;

    public int FileCountCompletedRate => (int)((float)FilesTransferred / (FilesTransferred + FilesSkipped) * 100.0);

    public string FileCountCompletedString => string.Format(Strings.Resources.S_COMPLETED_FILES_NUM, FilesTransferred, FilesTransferred + FilesSkipped);

    public string AverageRateString
    {
        get
        {
            if (AverageRateMBps.HasValue)
            {
                if (AverageRateMBps.Value <= 0)
                    return string.Empty;

                return string.Format(Strings.Resources.S_SECONDS_SHORT, $"{UnitConverter.BytesToSize((long)(AverageRateMBps.Value * 1024 * 1024))}/");
            }
            else
            {
                if (TotalBytes.HasValue && TotalSeconds.HasValue && TotalSeconds.Value > 0)
                {
                    return string.Format(Strings.Resources.S_SECONDS_SHORT, $"{UnitConverter.BytesToSize(TotalBytes.Value / (long)TotalSeconds.Value)}/");
                }

                return string.Empty;
            }
        }
    }

    public string TotalSize => TotalBytes.HasValue ? UnitConverter.BytesToSize(TotalBytes.Value) : string.Empty;

    public string TotalTime => TotalSeconds.HasValue ? UnitConverter.ToTime(TotalSeconds.Value) : string.Empty;
}
