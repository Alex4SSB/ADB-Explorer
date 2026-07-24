using ADB_Explorer.Controls;
using ADB_Explorer.Helpers;
using Microsoft.Extensions.Hosting;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace ADB_Explorer.Services;

public class FileOpSnackbarService(ISnackbarService snackbarService, HashSet<string> suppressedDevices) : IHostedService
{
    /// <summary>
    /// How long a finished operation keeps being shown in the snackbar after leaving the
    /// InProgress state, so short operations don't disappear (or cause the whole snackbar to
    /// flash open and closed) almost instantly.
    /// </summary>
    private static readonly TimeSpan CompletedGracePeriod = TimeSpan.FromSeconds(1.5);

    private AdbSnackbar? _snackbar;
    private FileOpSnackbarContent? _content;
    private FileOperationQueue? _subscribedQueue;
    private DispatcherTimer? _reevaluateTimer;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        StopReevaluateTimer();
        UnsubscribeQueue();
        return Task.CompletedTask;
    }

    public void SubscribeQueue(FileOperationQueue queue)
    {
        _subscribedQueue = queue;
        queue.Operations.CollectionChanged += Operations_CollectionChanged;
        queue.PropertyChanged += Queue_PropertyChanged;

        foreach (var op in queue.Operations)
            op.PropertyChanged += FileOperation_PropertyChanged;
    }

    private void UnsubscribeQueue()
    {
        if (_subscribedQueue is null)
            return;

        _subscribedQueue.Operations.CollectionChanged -= Operations_CollectionChanged;
        _subscribedQueue.PropertyChanged -= Queue_PropertyChanged;

        foreach (var op in _subscribedQueue.Operations)
            op.PropertyChanged -= FileOperation_PropertyChanged;

        _subscribedQueue = null;
    }

    private void Operations_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action is NotifyCollectionChangedAction.Reset)
        {
            // AddRange/RemoveAll fires Reset with no NewItems/OldItems; re-subscribe to all current operations.
            if (_subscribedQueue is not null)
                foreach (var op in _subscribedQueue.Operations)
                {
                    op.PropertyChanged -= FileOperation_PropertyChanged;
                    op.PropertyChanged += FileOperation_PropertyChanged;
                }
        }
        else
        {
            if (e.NewItems is not null)
                foreach (FileOperation op in e.NewItems)
                    op.PropertyChanged += FileOperation_PropertyChanged;

            if (e.OldItems is not null)
                foreach (FileOperation op in e.OldItems)
                    op.PropertyChanged -= FileOperation_PropertyChanged;
        }

        App.SafeInvoke(Refresh);
    }

    private void Queue_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FileOperationQueue.Progress) && _snackbar is not null)
            App.SafeInvoke(() => _snackbar.ProgressValue = _subscribedQueue!.Progress);
    }

    private void FileOperation_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FileOperation.Status))
            App.SafeInvoke(Refresh);
    }

    private void Refresh()
    {
        var visible = _subscribedQueue?.Operations
            .Where(op => op.IsSnackbarVisible(CompletedGracePeriod))
            .ToList() ?? [];

        if (visible.Count == 0)
        {
            HideSnackbar();
            return;
        }

        ShowSnackbar(visible);
    }

    /// <summary>
    /// Nothing triggers a property change purely because time has passed, so while the snackbar
    /// is open, periodically re-evaluate which operations are still within their post-completion
    /// grace period. Using a plain recurring tick (rather than computing and rescheduling an
    /// exact one-shot delay for the next expiry) keeps this self-correcting: even if a tick is
    /// ever missed or a state update raced with a scheduled callback, the next tick still runs
    /// and the snackbar can't get stuck open indefinitely.
    /// </summary>
    private void StartReevaluateTimer()
    {
        if (_reevaluateTimer is not null)
            return;

        _reevaluateTimer = new DispatcherTimer { Interval = CompletedGracePeriod };
        _reevaluateTimer.Tick += ReevaluateTimer_Tick;
        _reevaluateTimer.Start();
    }

    private void StopReevaluateTimer()
    {
        if (_reevaluateTimer is null)
            return;

        _reevaluateTimer.Stop();
        _reevaluateTimer.Tick -= ReevaluateTimer_Tick;
        _reevaluateTimer = null;
    }

    private void ReevaluateTimer_Tick(object? sender, EventArgs e) => Refresh();

    private bool _isShowing = false;

    private void ShowSnackbar(List<FileOperation> operations)
    {
        var presenter = snackbarService.GetSnackbarPresenter();
        if (presenter is null)
            return;

        if (_snackbar is null || _content is null)
        {
            _content = new FileOpSnackbarContent();
            _snackbar = new(presenter)
            {
                Title = $"{Strings.Resources.S_FILE_OP_TOOLTIP}: {Strings.Resources.S_FILEOP_RUNNING}",
                Content = _content,
                Appearance = ControlAppearance.Secondary,
                Timeout = TimeSpan.MaxValue,
                ProgressMode = SnackProgressMode.ExternalProgress,
                ProgressMaximum = 1.0,
                ProgressValue = _subscribedQueue?.Progress ?? 0.0
            };
        }

        _content.OperationsSource = operations;

        if (operations.Count > 0)
            suppressedDevices.Add(operations[0].Device.SerialNumber);

        if (!_isShowing)
        {
            _isShowing = true;
            StartReevaluateTimer();
            _ = presenter.ImmediatelyDisplay(_snackbar);
        }
    }

    private void HideSnackbar()
    {
        StopReevaluateTimer();

        if (_isShowing && snackbarService.GetSnackbarPresenter() is { } presenter)
        {
            _isShowing = false;
            _ = presenter.HideCurrent();
        }
    }
}
