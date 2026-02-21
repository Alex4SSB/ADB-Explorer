using ADB_Explorer.Converters;
using ADB_Explorer.Services;

namespace ADB_Explorer.ViewModels;

internal class InProgSyncProgressViewModel : FileOpProgressViewModel
{
    private readonly AdbSyncProgressInfo adbInfo = null;

    public InProgSyncProgressViewModel() : base(FileOperation.OperationStatus.InProgress)
    {

    }

    public InProgSyncProgressViewModel(AdbSyncProgressInfo adbInfo) : this()
    {
        this.adbInfo = adbInfo;
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
}
