namespace ADB_Explorer.ViewModels;

public class CompletedSyncProgressViewModel : FileOpProgressViewModel
{
    private readonly AdbSyncStatsInfo _adbInfo;

    public CompletedSyncProgressViewModel(AdbSyncStatsInfo adbInfo) : base(FileOperation.OperationStatus.Completed)
    {
        _adbInfo = adbInfo;
    }

    public long FilesTransferred => _adbInfo.FilesTransferred;

    public long FilesSkipped => _adbInfo.FilesSkipped;

    public double? AverageRateMBps => _adbInfo.AverageRate;

    public long? TotalBytes => _adbInfo.TotalBytes;

    public double? TotalSeconds => _adbInfo.TotalTime;

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

                return string.Format(Strings.Resources.S_SECONDS_SHORT, $"{UnitFormatter.BytesToSize((long)(AverageRateMBps.Value * 1024 * 1024))}/");
            }
            else
            {
                if (TotalBytes.HasValue && TotalSeconds.HasValue && TotalSeconds.Value > 0)
                {
                    return string.Format(Strings.Resources.S_SECONDS_SHORT, $"{UnitFormatter.BytesToSize(TotalBytes.Value / (long)TotalSeconds.Value)}/");
                }

                return string.Empty;
            }
        }
    }

    public string TotalSize => TotalBytes.HasValue ? UnitFormatter.BytesToSize(TotalBytes.Value) : string.Empty;

    public string TotalTime => TotalSeconds.HasValue ? UnitFormatter.ToTime(TotalSeconds.Value) : string.Empty;
}
