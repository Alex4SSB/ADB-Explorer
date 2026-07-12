using ADB_Explorer.Controls;
using ADB_Explorer.Helpers;
using Microsoft.Extensions.Hosting;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace ADB_Explorer.Services;

public class FileOpSnackbarService(ISnackbarService snackbarService, HashSet<string> suppressedDevices) : IHostedService
{
    private AdbSnackbar? _snackbar;
    private FileOpSnackbarContent? _content;
    private FileOperationQueue? _subscribedQueue;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
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
        var hasInProgress = _subscribedQueue?.Operations
            .Any(op => op.Status is FileOperation.OperationStatus.InProgress) ?? false;

        if (!hasInProgress)
        {
            HideSnackbar();
            return;
        }

        ShowSnackbar(_subscribedQueue!.Operations);
    }

    private bool _isShowing = false;

    // Keep the snackbar up at least this long so a fast (small-file) operation doesn't just flash.
    private static readonly TimeSpan MinDisplay = TimeSpan.FromMilliseconds(1500);
    private long _shownAtTick;
    private DispatcherTimer? _hideTimer;

    private void ShowSnackbar(ObservableList<FileOperation> operations)
    {
        var presenter = snackbarService.GetSnackbarPresenter();
        if (presenter is null)
            return;

        // A new operation started - cancel any pending minimum-display hide.
        _hideTimer?.Stop();
        _hideTimer = null;

        if (_snackbar is null || _content is null)
        {
            _content = new FileOpSnackbarContent();
            _snackbar = new AdbSnackbar(presenter)
            {
                Title = $"{Strings.Resources.S_FILE_OP_TOOLTIP}: {Strings.Resources.S_FILEOP_RUNNING}",
                Content = _content,
                Appearance = ControlAppearance.Secondary,
                Timeout = TimeSpan.MaxValue,
                ProgressMode = SnackProgressMode.ExternalProgress,
                ProgressMaximum = 1.0,
            };
            _snackbar.ProgressValue = _subscribedQueue?.Progress ?? 0.0;
        }

        _content.OperationsSource = operations;

        if (operations.FirstOrDefault() is { } firstOp)
            suppressedDevices.Add(firstOp.Device.SerialNumber);

        if (!_isShowing)
        {
            _isShowing = true;
            _shownAtTick = Environment.TickCount64;
            _ = presenter.ImmediatelyDisplay(_snackbar);
        }
    }

    private void HideSnackbar()
    {
        if (!_isShowing)
            return;

        // Enforce a minimum on-screen time: if it hasn't been shown long enough, defer the hide.
        var remaining = MinDisplay.TotalMilliseconds - (Environment.TickCount64 - _shownAtTick);
        if (remaining > 0)
        {
            _hideTimer?.Stop();
            _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(remaining) };
            _hideTimer.Tick += (_, _) =>
            {
                _hideTimer?.Stop();
                _hideTimer = null;
                HideNow();
            };
            _hideTimer.Start();
            return;
        }

        HideNow();
    }

    private void HideNow()
    {
        if (!_isShowing)
            return;

        // Clear the flag even if the presenter is momentarily unavailable, so the snackbar can't get stuck
        // "showing" forever and block future operations from displaying.
        _isShowing = false;
        if (snackbarService.GetSnackbarPresenter() is { } presenter)
            _ = presenter.HideCurrent();
    }
}
