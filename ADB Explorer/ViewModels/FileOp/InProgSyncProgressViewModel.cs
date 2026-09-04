using ADB_Explorer.Converters;
using ADB_Explorer.Services;

namespace ADB_Explorer.ViewModels;

internal class InProgSyncProgressViewModel : FileOpProgressViewModel, IDisposable
{
    private readonly AdbSyncProgressInfo? adbInfo = null;
    private readonly DateTime? transferStart = null;
    private readonly long? totalFileBytes = null;
    private readonly long? totalBytesTransferred = null;
    private readonly bool showElapsedTime;
    private DispatcherTimer? elapsedTimer;
    private bool disposed;

    public InProgSyncProgressViewModel() : base(FileOperation.OperationStatus.InProgress)
    {

    }

    public InProgSyncProgressViewModel(
        AdbSyncProgressInfo adbInfo,
        DateTime transferStart,
        long? totalFileBytes,
        long? totalBytesTransferred,
        bool showElapsedTime = false) : this()
    {
        this.adbInfo = adbInfo;
        this.transferStart = transferStart;
        this.totalFileBytes = totalFileBytes;
        this.totalBytesTransferred = totalBytesTransferred;
        this.showElapsedTime = showElapsedTime;

        if (showElapsedTime)
            App.SafeBeginInvoke(StartElapsedTimer);
    }

    public string PercentageString => $"{adbInfo?.TotalPercentage:0.0}";

    public double? TotalPercentage => adbInfo?.TotalPercentage;

    public long? TotalBytesTransferred => adbInfo?.TotalBytesTransferred;

    public string TotalBytes => TotalBytesTransferred?.BytesToSize();

    public double? CurrentFilePercentage => adbInfo?.CurrentFilePercentage;

    public string CurrentPercentageString => $"{CurrentFilePercentage:0.0}";

    public string CurrentFilePath => adbInfo?.AndroidPath;

    public string CurrentFileName => Path.GetFileName(CurrentFilePath);

    public string CurrentFileNameWithoutExtension => Path.GetFileNameWithoutExtension(CurrentFilePath);

    public double? RemainingSeconds
    {
        get
        {
            if (showElapsedTime)
            {
                if (transferStart is null)
                    return null;

                var elapsed = (DateTime.Now - transferStart.Value).TotalSeconds;
                if (elapsed < 0)
                    return 0;
                return elapsed;
            }

            if (transferStart is null || totalFileBytes is null or 0 || totalBytesTransferred is null or <= 0)
                return null;

            var estimateElapsed = (DateTime.Now - transferStart.Value).TotalSeconds;
            if (estimateElapsed <= 0)
                return null;

            var bytesPerSecond = totalBytesTransferred.Value / estimateElapsed;
            if (bytesPerSecond <= 0)
                return null;

            var remaining = totalFileBytes.Value - totalBytesTransferred.Value;
            if (remaining <= 0)
                return null;

            return remaining / bytesPerSecond;
        }
    }

    public string RemainingTime
    {
        get
        {
            var digits = 0;
            if (RemainingSeconds > 60)
                digits = 1;
            return RemainingSeconds.ToTime(useMilli: false, digits: digits);
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        App.SafeBeginInvoke(StopElapsedTimer);
    }

    private void StartElapsedTimer()
    {
        if (disposed || elapsedTimer is not null)
            return;

        elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        elapsedTimer.Tick += ElapsedTimer_Tick;
        elapsedTimer.Start();
    }

    private void StopElapsedTimer()
    {
        if (elapsedTimer is null)
            return;

        elapsedTimer.Tick -= ElapsedTimer_Tick;
        elapsedTimer.Stop();
        elapsedTimer = null;
    }

    private void ElapsedTimer_Tick(object? sender, EventArgs e)
        => OnPropertyChanged(nameof(RemainingTime));
}
