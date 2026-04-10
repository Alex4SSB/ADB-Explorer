using ADB_Explorer.Controls;
using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using Microsoft.Extensions.Hosting;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace ADB_Explorer.Services;

public class ThumbnailSnackbarService(ISnackbarService snackbarService) : IHostedService
{
    private ThumbnailSnackbarContent? _thumbnailPullContent;
    private DispatcherTimer? _pullTimeoutTimer;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        ThumbnailService.ThumbnailProgressChanged += OnThumbnailProgressChanged;
#if DEBUG
        App.SafeBeginInvoke(ShowDebugSnackbar, DispatcherPriority.Loaded);
#endif
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        ThumbnailService.ThumbnailProgressChanged -= OnThumbnailProgressChanged;
        ThumbnailService.ThumbnailPullingProgressUpdated -= OnThumbnailPullingProgressUpdated;
        return Task.CompletedTask;
    }

    private void OnThumbnailProgressChanged(ThumbnailService.ThumbnailStep step, bool isStarting)
    {
        App.SafeInvoke(() =>
        {
            var presenter = snackbarService.GetSnackbarPresenter();

            if (isStarting)
            {
                if (step == ThumbnailService.ThumbnailStep.Pulling)
                {
                    if (presenter is not null)
                    {
                        _thumbnailPullContent = new ThumbnailSnackbarContent
                        {
                            Text = Strings.Resources.S_THUMB_SNACKBAR_PULLING,
                            ShowProgress = true,
                        };
                        var snackbar = new AdbSnackbar(presenter)
                        {
                            Title = string.Format(Strings.Resources.S_THUMB_SNACKBAR_TITLE, Data.DevicesObject.Current.Name),
                            Content = _thumbnailPullContent,
                            Appearance = ControlAppearance.Secondary,
                            Timeout = TimeSpan.MaxValue,
                            ProgressMode = SnackProgressMode.Off,
                        };
                        snackbar.Show(true);
                        StartPullTimeoutTimer();

                        ThumbnailService.ThumbnailPullingProgressUpdated += OnThumbnailPullingProgressUpdated;
                    }
                }
                else
                {
                    string message = step switch
                    {
                        ThumbnailService.ThumbnailStep.ReadingDatabase => Strings.Resources.S_THUMB_SNACKBAR_READING,
                        ThumbnailService.ThumbnailStep.CheckingUpdates => Strings.Resources.S_THUMB_SNACKBAR_CHECKING,
                        _ => ""
                    };
                    if (presenter is not null)
                    {
                        string deviceName = Data.RuntimeSettings.IsRTL
                        ? $"{TextHelper.RTL_MARK}{Data.DevicesObject.Current.Name}{TextHelper.LTR_MARK}"
                        : Data.DevicesObject.Current.Name;

                        var snackbar = new AdbSnackbar(presenter)
                        {
                            Title = string.Format(Strings.Resources.S_THUMB_SNACKBAR_TITLE, deviceName),
                            Content = new ThumbnailSnackbarContent { Text = message },
                            Appearance = ControlAppearance.Secondary,
                            Timeout = TimeSpan.MaxValue,
                            ProgressMode = SnackProgressMode.Indeterminate,
                        };
                        _ = presenter.ImmediatelyDisplay(snackbar);
                    }
                }
            }
            else
            {
                if (step == ThumbnailService.ThumbnailStep.Pulling)
                {
                    StopPullTimeoutTimer();
                    ThumbnailService.ThumbnailPullingProgressUpdated -= OnThumbnailPullingProgressUpdated;
                    _thumbnailPullContent = null;
                }
                _ = presenter?.HideCurrent();
            }
        });
    }

    private void OnThumbnailPullingProgressUpdated(int completed, int total)
    {
        App.SafeInvoke(() =>
        {
            ResetPullTimeoutTimer();
            if (_thumbnailPullContent is not null)
            {
                _thumbnailPullContent.Maximum = total;
                _thumbnailPullContent.Value = completed;
                _thumbnailPullContent.CounterText = $"{completed} / {total}";
            }
        });
    }

    private void StartPullTimeoutTimer()
    {
        StopPullTimeoutTimer();
        _pullTimeoutTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _pullTimeoutTimer.Tick += OnPullTimeoutTick;
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
        _pullTimeoutTimer?.Stop();
        _pullTimeoutTimer = null;
    }

    private void OnPullTimeoutTick(object? sender, EventArgs e)
    {
        StopPullTimeoutTimer();
        ThumbnailService.ThumbnailPullingProgressUpdated -= OnThumbnailPullingProgressUpdated;
        _thumbnailPullContent = null;
        _ = snackbarService.GetSnackbarPresenter()?.HideCurrent();
    }

#if DEBUG
    private void ShowDebugSnackbar()
    {
        var presenter = snackbarService.GetSnackbarPresenter();
        if (presenter is not null)
            _ = presenter.ImmediatelyDisplay(CreateDebugSnackbar(presenter));
    }

    private static AdbSnackbar CreateDebugSnackbar(SnackbarPresenter presenter) =>
        new(presenter)
        {
            Title = "Debug Title",
            Content = new ThumbnailSnackbarContent
            {
                Text = "debug text",
                ShowProgress = true,
                Maximum = 100,
                Value = 60,
                CounterText = "6 / 10",
            },
            Appearance = ControlAppearance.Secondary,
            Timeout = TimeSpan.MaxValue,
            ProgressMode = SnackProgressMode.Indeterminate,
        };
#endif
}
