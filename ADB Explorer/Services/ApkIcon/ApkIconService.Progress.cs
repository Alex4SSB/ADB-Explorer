namespace ADB_Explorer.Services;

public static partial class ApkIconService
{
    /// <summary>Raised when APK icon queue work starts (<c>true</c>) or the queue goes idle (<c>false</c>).</summary>
    public static event Action<bool>? IconLoadProgressChanged;

    /// <summary>
    /// Raised when a real icon pull is in the queue (not label-only backfill).
    /// Scroll often re-fetches missing labels after icons are done; that must not flash the thumb tooltip.
    /// </summary>
    public static event Action<bool>? IconPullProgressChanged;

    /// <summary>Raised after each queued icon attempt so the progress UI can keep its timeout alive.</summary>
    public static event Action? IconLoadProgressTick;

    private static int ProgressActiveCount;

    private static int IconPullProgressActiveCount;

    /// <summary>
    /// Bumped to cancel a pending debounced progress-hide. Virtualization and label-only
    /// follow-ups often restart the worker a few hundred ms after the queue first empties;
    /// collapsing the status spinner across that gap restarts its stroke animation and looks like flicker.
    /// </summary>
    private static int ProgressHideGeneration;

    private static int IconPullProgressHideGeneration;

    /// <summary>Hold the progress indicator briefly after the queue empties so batch gaps do not flicker.</summary>
    private static readonly TimeSpan ProgressHideDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>True while any icon/label load is queued or running (includes the hide debounce window).</summary>
    public static bool IsLoadInProgress => Volatile.Read(ref ProgressActiveCount) != 0;

    /// <summary>True while an icon (non-label-only) pull is active or in the UI hide debounce window.</summary>
    public static bool IsIconPullInProgress => Volatile.Read(ref IconPullProgressActiveCount) != 0;

    private static void SetIconLoadProgress(bool active, bool force = false)
        => SetDebouncedProgress(ProgressKind.AnyQueueWork, active, force);

    private static void SetIconPullProgress(bool active, bool force = false)
        => SetDebouncedProgress(ProgressKind.IconPull, active, force);

    private enum ProgressKind
    {
        AnyQueueWork,
        IconPull,
    }

    private static void SetDebouncedProgress(ProgressKind kind, bool active, bool force)
    {
        if (force)
        {
            BumpHideGeneration(kind);
            if (ExchangeActive(kind, 0) != 0)
                RaiseProgressChanged(kind, false);
            return;
        }

        if (active)
        {
            BumpHideGeneration(kind);
            if (CompareExchangeActive(kind, 1, 0) == 0)
                RaiseProgressChanged(kind, true);
            else
                ExchangeActive(kind, 1);
            return;
        }

        var generation = BumpHideGeneration(kind);
        _ = HideDebouncedProgressWhenIdleAsync(kind, generation);
    }

    private static int BumpHideGeneration(ProgressKind kind)
    {
        if (kind is ProgressKind.IconPull)
            return Interlocked.Increment(ref IconPullProgressHideGeneration);
        return Interlocked.Increment(ref ProgressHideGeneration);
    }

    private static int ReadHideGeneration(ProgressKind kind)
    {
        if (kind is ProgressKind.IconPull)
            return Volatile.Read(ref IconPullProgressHideGeneration);
        return Volatile.Read(ref ProgressHideGeneration);
    }

    private static int ExchangeActive(ProgressKind kind, int value)
    {
        if (kind is ProgressKind.IconPull)
            return Interlocked.Exchange(ref IconPullProgressActiveCount, value);
        return Interlocked.Exchange(ref ProgressActiveCount, value);
    }

    private static int CompareExchangeActive(ProgressKind kind, int value, int comparand)
    {
        if (kind is ProgressKind.IconPull)
            return Interlocked.CompareExchange(ref IconPullProgressActiveCount, value, comparand);
        return Interlocked.CompareExchange(ref ProgressActiveCount, value, comparand);
    }

    private static void RaiseProgressChanged(ProgressKind kind, bool active)
    {
        try
        {
            if (kind is ProgressKind.IconPull)
                IconPullProgressChanged?.Invoke(active);
            else
                IconLoadProgressChanged?.Invoke(active);
        }
        catch
        {
            /* ignore */
        }
    }

    private static async Task HideDebouncedProgressWhenIdleAsync(ProgressKind kind, int generation)
    {
        try
        {
            await Task.Delay(ProgressHideDelay).ConfigureAwait(false);
        }
        catch
        {
            return;
        }

        if (generation != ReadHideGeneration(kind))
            return;

        if (kind is ProgressKind.AnyQueueWork)
        {
            lock (QueueLock)
            {
                if (generation != ReadHideGeneration(kind))
                    return;

                if (WorkerRunning || LoadQueue.Count > 0)
                    return;
            }
        }

        if (generation != ReadHideGeneration(kind))
            return;

        if (CompareExchangeActive(kind, 0, 1) != 1)
            return;

        if (generation != ReadHideGeneration(kind))
        {
            ExchangeActive(kind, 1);
            return;
        }

        RaiseProgressChanged(kind, false);
    }

    private static void MaybeHideIconPullProgress(
        ref bool pullProgressShown,
        List<(Task Task, bool IsIconPull)> inFlight)
    {
        if (!pullProgressShown)
            return;

        if (inFlight.Any(static t => !t.Task.IsCompleted && t.IsIconPull))
            return;

        lock (QueueLock)
        {
            if (LoadQueue.Any(static r => !r.LabelOnly))
                return;
        }

        SetIconPullProgress(false);
        pullProgressShown = false;
    }
}
