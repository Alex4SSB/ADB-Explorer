namespace ADB_Explorer.ViewModels;

internal class InProgSyncProgressViewModel : FileOpProgressViewModel, IDisposable
{
    private readonly AdbSyncProgressInfo? _adbInfo = null;
    private readonly DateTime? _transferStart = null;
    private readonly long? _totalFileBytes = null;
    private readonly long? _totalBytesTransferred = null;
    private readonly bool _showElapsedTime;
    private DispatcherTimer? _elapsedTimer;
    private bool _disposed;

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
        _adbInfo = adbInfo;
        _transferStart = transferStart;
        _totalFileBytes = totalFileBytes;
        _totalBytesTransferred = totalBytesTransferred;
        _showElapsedTime = showElapsedTime;

        if (showElapsedTime)
            App.SafeBeginInvoke(StartElapsedTimer);
    }

    public string PercentageString => $"{_adbInfo?.TotalPercentage:0.0}";

    public double? TotalPercentage => _adbInfo?.TotalPercentage;

    public long? TotalBytesTransferred => _adbInfo?.TotalBytesTransferred;

    public string TotalBytes => TotalBytesTransferred?.BytesToSize();

    public double? CurrentFilePercentage => _adbInfo?.CurrentFilePercentage;

    public string CurrentPercentageString => $"{CurrentFilePercentage:0.0}";

    public string CurrentFilePath => _adbInfo?.AndroidPath;

    public string CurrentFileName => Path.GetFileName(CurrentFilePath);

    public string CurrentFileNameWithoutExtension => Path.GetFileNameWithoutExtension(CurrentFilePath);

    public double? RemainingSeconds
    {
        get
        {
            if (_showElapsedTime)
            {
                if (_transferStart is null)
                    return null;

                var elapsed = (DateTime.Now - _transferStart.Value).TotalSeconds;
                if (elapsed < 0)
                    return 0;
                return elapsed;
            }

            if (_transferStart is null || _totalFileBytes is null or 0 || _totalBytesTransferred is null or <= 0)
                return null;

            var estimateElapsed = (DateTime.Now - _transferStart.Value).TotalSeconds;
            if (estimateElapsed <= 0)
                return null;

            var bytesPerSecond = _totalBytesTransferred.Value / estimateElapsed;
            if (bytesPerSecond <= 0)
                return null;

            var remaining = _totalFileBytes.Value - _totalBytesTransferred.Value;
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
        if (_disposed)
            return;

        _disposed = true;
        App.SafeBeginInvoke(StopElapsedTimer);
    }

    private void StartElapsedTimer()
    {
        if (_disposed || _elapsedTimer is not null)
            return;

        _elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _elapsedTimer.Tick += ElapsedTimer_Tick;
        _elapsedTimer.Start();
    }

    private void StopElapsedTimer()
    {
        if (_elapsedTimer is null)
            return;

        _elapsedTimer.Tick -= ElapsedTimer_Tick;
        _elapsedTimer.Stop();
        _elapsedTimer = null;
    }

    private void ElapsedTimer_Tick(object? sender, EventArgs e)
        => OnPropertyChanged(nameof(RemainingTime));
}
