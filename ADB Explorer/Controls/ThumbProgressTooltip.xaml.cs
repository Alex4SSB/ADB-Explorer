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
            // Icon pulls only — label-only backfill (common while scrolling after icons are done)
            // still uses IconLoadProgressChanged for live-sort pausing, but must not show this tooltip.
            ApkIconService.IconPullProgressChanged += OnApkIconPullProgressChanged;
            ApkIconService.IconLoadProgressTick += OnApkIconLoadProgressTick;

            // Package icon-view sort refresh can unload/recreate this control while loads are
            // still active; the start event already fired, so resync visibility on Loaded.
            if (ApkIconService.IsIconPullInProgress)
                ShowApkIconProgress();
        };
        Unloaded += (_, _) =>
        {
            ThumbnailService.ThumbnailProgressChanged -= OnThumbnailProgressChanged;
            ThumbnailService.ThumbnailPullingProgressUpdated -= OnThumbnailPullingProgressUpdated;
            ApkIconService.IconPullProgressChanged -= OnApkIconPullProgressChanged;
            ApkIconService.IconLoadProgressTick -= OnApkIconLoadProgressTick;
            StopPullTimeoutTimer();
        };
    }

    private void OnApkIconLoadProgressTick()
    {
        // Only keep the idle timeout alive while an icon pull is actually showing.
        if (!_apkIconProgressActive && !ApkIconService.IsIconPullInProgress)
            return;

        App.SafeBeginInvoke(ResetPullTimeoutTimer);
    }

    private void OnApkIconPullProgressChanged(bool isStarting)
    {
        App.SafeBeginInvoke(() =>
        {
            if (isStarting)
            {
                ShowApkIconProgress();
                return;
            }

            _apkIconProgressActive = false;
            StopPullTimeoutTimer();
            UpdateVisibilityAfterProgressEnd();
        });
    }

    private void ShowApkIconProgress()
    {
        _apkIconProgressActive = true;
        ProgressText = Strings.Resources.S_THUMB_SNACKBAR_APK_ICONS;
        // Avoid Visibility.Collapsed ↔ Visible toggles that re-fire Rectangle.Loaded and
        // restart the stroke animation (visible flicker between icon-load batches).
        if (Visibility != Visibility.Visible)
            Visibility = Visibility.Visible;
        StartPullTimeoutTimer();
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
        // Keep showing while APK icons are still pulling — individual pulls can take
        // longer than the idle timeout when concurrency is capped and the queue is deep.
        // Label-only work must not keep this tooltip alive.
        if (ApkIconService.IsIconPullInProgress)
        {
            ShowApkIconProgress();
            ResetPullTimeoutTimer();
            return;
        }

        StopPullTimeoutTimer();
        _apkIconProgressActive = false;
        _thumbnailProgressActive = false;
        ProgressText = string.Empty;
        Visibility = Visibility.Collapsed;
    }
}
