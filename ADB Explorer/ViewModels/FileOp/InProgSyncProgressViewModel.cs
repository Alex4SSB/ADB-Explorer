using ADB_Explorer.Converters;
using ADB_Explorer.Services;

namespace ADB_Explorer.ViewModels;

internal class InProgSyncProgressViewModel : FileOpProgressViewModel
{
    private readonly AdbSyncProgressInfo adbInfo = null;
    private readonly DateTime? transferStart = null;
    private readonly long? totalFileBytes = null;
    private readonly long? totalBytesTransferred = null;

    public InProgSyncProgressViewModel() : base(FileOperation.OperationStatus.InProgress)
    {

    }

    public InProgSyncProgressViewModel(AdbSyncProgressInfo adbInfo, DateTime transferStart, long? totalFileBytes, long? totalBytesTransferred) : this()
    {
        this.adbInfo = adbInfo;
        this.transferStart = transferStart;
        this.totalFileBytes = totalFileBytes;
        this.totalBytesTransferred = totalBytesTransferred;
    }

    public string PercentageString => $"{adbInfo?.TotalPercentage:0.0}";

    public double? TotalPercentage => adbInfo?.TotalPercentage;

    public long? TotalBytesTransferred => adbInfo?.TotalBytesTransferred;

    public string TotalBytes => TotalBytesTransferred?.BytesToSize();

    public double? CurrentFilePercentage => adbInfo?.CurrentFilePercentage;

    public string CurrentPercentageString => $"{CurrentFilePercentage:0.0}";

    public long? CurrentFileBytesTransferred => adbInfo?.CurrentFileBytesTransferred;

    public string CurrentFilePath => adbInfo?.AndroidPath;

    public string CurrentFileName => Path.GetFileName(CurrentFilePath);

    public string CurrentFileNameWithoutExtension => Path.GetFileNameWithoutExtension(CurrentFilePath);

    public double? RemainingSeconds
    {
        get
        {
            if (transferStart is null || totalFileBytes is null or 0 || totalBytesTransferred is null or <= 0)
                return null;

            var elapsed = (DateTime.Now - transferStart.Value).TotalSeconds;
            if (elapsed <= 0)
                return null;

            var bytesPerSecond = totalBytesTransferred.Value / elapsed;
            if (bytesPerSecond <= 0)
                return null;

            var remaining = totalFileBytes.Value - totalBytesTransferred.Value;
            if (remaining <= 0)
                return null;

            return remaining / bytesPerSecond;
        }
    }

    public string RemainingTime => RemainingSeconds.ToTime(useMilli: false, digits: RemainingSeconds > 60 ? 1 : 0);
}
