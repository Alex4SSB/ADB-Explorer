namespace ADB_Explorer.Services;

public partial class AppRuntimeSettings : ObservableObject
{
    public bool ResetAppSettings { get; set; } = false;

    /// <summary>
    /// Portable update archive is staged; apply the file swap when the process exits.
    /// </summary>
    public bool ApplyUpdateOnExit { get; set; } = false;

    public DriveViewModel? SelectedDrive { get; set; } = null;

    /// <summary>
    /// Path to open after switching devices, instead of drive view.
    /// </summary>
    public AdbLocation? PendingLocationAfterDeviceOpen { get; set; }

    [ObservableProperty]
    public partial bool IsHighContrast { get; set; }

    public bool IsDebug
    {
        get
        {
#if DEBUG
            return true;
#else
            return false;
#endif
        }
    }

    public bool IsExplorerLoaded { get; set; } = false;

    private bool _isPollingStopped = false;
    public bool IsPollingStopped
    {
        get => _isPollingStopped;
        set => SetProperty(ref _isPollingStopped, value);
    }

    public Cursor MainCursor { get; set; } = Cursors.Arrow;

    public float MainWindowScalingFactor { get; set; } = 1.0f;

    private ThumbnailService.ThumbnailSize _thumbsSize = ThumbnailService.ThumbnailSize.Disabled;
    public ThumbnailService.ThumbnailSize ThumbsSize
    {
        get => _thumbsSize;
        set
        {
            _thumbsSize = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// The last size chosen outside search mode - what a location or new tab falls back to.
    /// </summary>
    public ThumbnailService.ThumbnailSize BrowseThumbsSize { get; set; } = ThumbnailService.ThumbnailSize.Disabled;

    public string? DefaultBrowserPath { get; set; }

    public string TempDragPath
    {
        get
        {
            field ??= Directory.CreateTempSubdirectory().FullName;

            return field;
        }
    } = null;

    private bool? _isAppPackaged = null;
    public bool IsAppPackaged
    {
        get
        {
            _isAppPackaged ??= NativeMethods.GetCurrentPackageFamilyName() is not null;
            return _isAppPackaged.Value;
        }
    }

    public bool SkipAppDataNotification { get; set; } = false;

    public bool IsWindows10 => Environment.OSVersion.Version < AdbExplorerConst.WIN11_VERSION;

    public bool IsRTL => Data.Settings.ActualUICulture.TextInfo.IsRightToLeft;
}
