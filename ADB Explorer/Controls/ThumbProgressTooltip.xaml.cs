using ADB_Explorer.Services;

namespace ADB_Explorer.Controls;

/// <summary>
/// Interaction logic for ThumbProgressTooltip.xaml
/// </summary>
public partial class ThumbProgressTooltip : UserControl
{
    public BitmapSource? Icon
    {
        get => (BitmapSource?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(BitmapSource),
            typeof(ThumbProgressTooltip), new PropertyMetadata(null));

    public string ProgressText
    {
        get => (string)GetValue(ProgressTextProperty);
        set => SetValue(ProgressTextProperty, value);
    }

    public static readonly DependencyProperty ProgressTextProperty =
        DependencyProperty.Register(nameof(ProgressText), typeof(string),
            typeof(ThumbProgressTooltip), new PropertyMetadata(string.Empty));

    private DispatcherTimer? _pullTimeoutTimer;
    private bool _thumbnailProgressActive;
    private bool _apkIconProgressActive;

    public ThumbProgressTooltip()
    {
        InitializeComponent();
        Visibility = Visibility.Collapsed;

        Loaded += (_, _) =>
        {
            ThumbnailService.ThumbnailProgressChanged += OnThumbnailProgressChanged;
            ThumbnailService.ThumbnailPullingProgressUpdated += OnThumbnailPullingProgressUpdated;
            ApkIconService.IconLoadProgressChanged += OnApkIconLoadProgressChanged;
            ApkIconService.IconLoadProgressTick += OnApkIconLoadProgressTick;
        };
        Unloaded += (_, _) =>
        {
            ThumbnailService.ThumbnailProgressChanged -= OnThumbnailProgressChanged;
            ThumbnailService.ThumbnailPullingProgressUpdated -= OnThumbnailPullingProgressUpdated;
            ApkIconService.IconLoadProgressChanged -= OnApkIconLoadProgressChanged;
            ApkIconService.IconLoadProgressTick -= OnApkIconLoadProgressTick;
            StopPullTimeoutTimer();
        };
    }

    private void OnApkIconLoadProgressTick()
    {
        App.SafeInvoke(ResetPullTimeoutTimer);
    }

    private void OnApkIconLoadProgressChanged(bool isStarting)
    {
        App.SafeInvoke(() =>
        {
            _apkIconProgressActive = isStarting;
            if (isStarting)
            {
                ProgressText = Strings.Resources.S_THUMB_SNACKBAR_APK_ICONS;
                Visibility = Visibility.Visible;
                StartPullTimeoutTimer();
            }
            else
            {
                StopPullTimeoutTimer();
                UpdateVisibilityAfterProgressEnd();
            }
        });
    }

    private void OnThumbnailProgressChanged(ThumbnailService.ThumbnailStep step, bool isStarting)
    {
        App.SafeInvoke(() =>
        {
            if (isStarting)
            {
                _thumbnailProgressActive = true;
                ProgressText = step switch
                {
                    ThumbnailService.ThumbnailStep.ReadingDatabase => Strings.Resources.S_THUMB_SNACKBAR_READING,
                    ThumbnailService.ThumbnailStep.CheckingUpdates => Strings.Resources.S_THUMB_SNACKBAR_CHECKING,
                    ThumbnailService.ThumbnailStep.Pulling => Strings.Resources.S_THUMB_SNACKBAR_PULLING,
                    _ => string.Empty,
                };
                Visibility = Visibility.Visible;

                if (step is ThumbnailService.ThumbnailStep.Pulling)
                    StartPullTimeoutTimer();
            }
            else
            {
                if (step is ThumbnailService.ThumbnailStep.Pulling)
                    StopPullTimeoutTimer();

                _thumbnailProgressActive = false;
                UpdateVisibilityAfterProgressEnd();
            }
        });
    }

    private void UpdateVisibilityAfterProgressEnd()
    {
        if (_apkIconProgressActive)
        {
            ProgressText = Strings.Resources.S_THUMB_SNACKBAR_APK_ICONS;
            Visibility = Visibility.Visible;
            return;
        }

        if (_thumbnailProgressActive)
            return;

        ProgressText = string.Empty;
        Visibility = Visibility.Collapsed;
    }

    private void OnThumbnailPullingProgressUpdated(int completed, int total)
    {
        App.SafeInvoke(ResetPullTimeoutTimer);
    }

    private void StartPullTimeoutTimer()
    {
        StopPullTimeoutTimer();
        _pullTimeoutTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _pullTimeoutTimer.Tick += OnPullTimeout;
        _pullTimeoutTimer.Start();
    }

    private void ResetPullTimeoutTimer()
    {
        if (_pullTimeoutTimer is null)
            return;
        _pullTimeoutTimer.Stop();
        _pullTimeoutTimer.Start();
    }

    private void StopPullTimeoutTimer()
    {
        if (_pullTimeoutTimer is null)
            return;
        _pullTimeoutTimer.Stop();
        _pullTimeoutTimer = null;
    }

    private void OnPullTimeout(object? sender, EventArgs e)
    {
        StopPullTimeoutTimer();
        _apkIconProgressActive = false;
        _thumbnailProgressActive = false;
        ProgressText = string.Empty;
        Visibility = Visibility.Collapsed;
    }
}
