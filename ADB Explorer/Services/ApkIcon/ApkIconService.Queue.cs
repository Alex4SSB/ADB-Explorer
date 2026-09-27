using AlphaOmega.Debug;
using AlphaOmega.Debug.Manifest;
using SkiaSharp;
using Wpf.Ui.Appearance;

namespace ADB_Explorer.Services;

public static partial class ApkIconService
{
    private static readonly HashSet<string> PendingLoads = new(StringComparer.Ordinal);

    private static readonly Lock PendingLock = new();

    private static readonly Lock QueueLock = new();

    private static readonly LinkedList<LoadRequest> LoadQueue = new();

    private static bool WorkerRunning;

    private static int WorkerGeneration;

    private static CancellationTokenSource? WorkerCts;

    private sealed class LoadRequest(
        string PullKey,
        LogicalDeviceViewModel Device,
        string ApkPath,
        string? PackageName,
        Action<BitmapSource?>? OnReady,
        bool LabelOnly = false,
        ApkLoadPriority Priority = ApkLoadPriority.Background,
        bool Quiet = false)
    {
        public string PullKey { get; } = PullKey;
        public LogicalDeviceViewModel Device { get; } = Device;
        public string ApkPath { get; } = ApkPath;
        public string? PackageName { get; set; } = PackageName;
        public Action<BitmapSource?>? OnReady { get; set; } = OnReady;
        public bool LabelOnly { get; set; } = LabelOnly;
        public ApkLoadPriority Priority { get; set; } = Priority;
        public bool Quiet { get; } = Quiet;
        public ApkLoadTiming? Timing { get; set; }
    }

    /// <summary>
    /// Reorders the in-flight queue so selected packages load first, then visible ones,
    /// then everything else. Also kicks loads for selected/visible items that are not yet cached.
    /// </summary>
    public static void UpdatePackageLoadPriorities(
        IEnumerable<Package>? selected,
        IEnumerable<Package>? visible)
    {
        if (IsLoadingStopped)
            return;

        if (!IsEnabled || Data.ActiveDevice is not { } device || !CanLoadOnDevice(device.ID))
            return;

        var selectedList = selected?.Where(static p => p is not null).Distinct().ToList() ?? [];
        var visibleList = visible?.Where(static p => p is not null).Distinct().ToList() ?? [];
        var selectedNames = new HashSet<string>(
            selectedList.Select(static p => p.Name).Where(static n => !string.IsNullOrEmpty(n))!,
            StringComparer.Ordinal);
        var visibleNames = new HashSet<string>(
            visibleList.Select(static p => p.Name).Where(static n => !string.IsNullOrEmpty(n))!,
            StringComparer.Ordinal);

        lock (QueueLock)
        {
            foreach (var request in LoadQueue)
            {
                if (string.IsNullOrEmpty(request.PackageName))
                    continue;

                if (selectedNames.Contains(request.PackageName))
                    request.Priority = ApkLoadPriority.Selected;
                else if (visibleNames.Contains(request.PackageName))
                    request.Priority = ApkLoadPriority.Visible;
                else
                    request.Priority = ApkLoadPriority.Background;
            }

            ResortQueue_NoLock();
        }

        foreach (var package in selectedList)
            BeginLoadForPackage(package, ApkLoadPriority.Selected);

        foreach (var package in visibleList)
        {
            if (selectedNames.Contains(package.Name))
                continue;
            BeginLoadForPackage(package, ApkLoadPriority.Visible);
        }
    }

    /// <summary>
    /// A label-only request is already pending for this package; convert it to a full icon load
    /// so manifest/resources are not pulled twice.
    /// </summary>
    private static void UpgradeLabelOnlyToIcon(
        string labelKey,
        string iconKey,
        LogicalDeviceViewModel device,
        string apkPath,
        string? packageName,
        Action<BitmapSource?>? onReady,
        ApkLoadPriority priority
        , ApkLoadTiming? timing = null
        )
    {
        LoadRequest? upgraded = null;
        lock (QueueLock)
        {
            var node = LoadQueue.First;
            while (node is not null)
            {
                if (node.Value.PullKey == labelKey)
                {
                    upgraded = node.Value;
                    LoadQueue.Remove(node);
                    break;
                }
                node = node.Next;
            }
        }

        lock (PendingLock)
        {
            PendingLoads.Remove(labelKey);
            if (!PendingLoads.Add(iconKey))
            {
                // An icon load sneaked in; chain onto it and drop the label-only work.
                if (upgraded?.OnReady is { } previous)
                {
                    AttachOnReady(iconKey, packageName, bmp =>
                    {
                        previous(bmp);
                        onReady?.Invoke(bmp);
                    }, priority);
                }
                else
                {
                    AttachOnReady(iconKey, packageName, onReady, priority);
                }
                return;
            }
        }

        if (upgraded is not null)
        {
            var previous = upgraded.OnReady;
            Enqueue(new LoadRequest(iconKey, device, apkPath, packageName, bmp =>
            {
                previous?.Invoke(bmp);
                onReady?.Invoke(bmp);
            }, LabelOnly: false, priority)
            {
                Timing = timing ?? upgraded.Timing,
            }
            , priority);
            return;
        }

        // Label-only was already running (not in queue) — start a dedicated icon load.
        Enqueue(new LoadRequest(iconKey, device, apkPath, packageName, onReady, LabelOnly: false, priority)
        {
            Timing = timing,
        }
        , priority);
    }

    private static void AttachOnReady(string pullKey, string? packageName, Action<BitmapSource?>? onReady, ApkLoadPriority priority)
    {
        if (onReady is null && priority == ApkLoadPriority.Background)
            return;

        lock (QueueLock)
        {
            foreach (var request in LoadQueue)
            {
                if (request.PullKey != pullKey)
                    continue;

                if (string.IsNullOrEmpty(request.PackageName) && !string.IsNullOrEmpty(packageName))
                    request.PackageName = packageName;

                if (onReady is not null)
                {
                    var previous = request.OnReady;
                    request.OnReady = bmp =>
                    {
                        previous?.Invoke(bmp);
                        onReady(bmp);
                    };
                }

                if (priority > request.Priority)
                {
                    request.Priority = priority;
                    LoadQueue.Remove(request);
                    InsertByPriority_NoLock(request);
                }

                return;
            }
        }

        if (onReady is null)
            return;

        void Handler(string serial, string cachedPackageName)
        {
            var parts = pullKey.Split('|');
            if (parts.Length < 2 || serial != parts[0])
                return;

            var keyPackage = parts[1];
            // Match by package name, or by apk path used as interim pull key.
            if (!string.Equals(cachedPackageName, keyPackage, StringComparison.Ordinal)
                && (string.IsNullOrEmpty(packageName) || !string.Equals(cachedPackageName, packageName, StringComparison.Ordinal)))
                return;

            ApkIconUpdated -= Handler;
            var device = Data.ActiveDevice;
            if (device is null || device.SerialNumber != serial)
            {
                onReady(null);
                return;
            }

            onReady(TryGetCachedIcon(device, cachedPackageName));
        }

        ApkIconUpdated += Handler;
    }

    private static void Enqueue(LoadRequest request, ApkLoadPriority priority)
    {
        lock (QueueLock)
        {
            request.Priority = priority;
            InsertByPriority_NoLock(request);

            if (WorkerRunning)
                return;

            StartWorker_NoLock();
        }
    }

    private static void InsertByPriority_NoLock(LoadRequest request)
    {
        // Higher priority first; within the same priority, preserve FIFO (append after equals).
        var node = LoadQueue.First;
        while (node is not null)
        {
            if (node.Value.Priority < request.Priority)
            {
                LoadQueue.AddBefore(node, request);
                return;
            }
            node = node.Next;
        }

        LoadQueue.AddLast(request);
    }

    private static void ResortQueue_NoLock()
    {
        if (LoadQueue.Count <= 1)
            return;

        var ordered = LoadQueue.OrderByDescending(static r => r.Priority).ToList();
        LoadQueue.Clear();
        foreach (var request in ordered)
            LoadQueue.AddLast(request);
    }

    private static void StartWorker_NoLock()
    {
        WorkerRunning = true;
        var generation = WorkerGeneration;
        WorkerCts = CancellationTokenSource.CreateLinkedTokenSource(Data.DeviceCts.Token);
        var token = WorkerCts.Token;
        _ = Task.Run(() => ProcessQueueAsync(generation, token), token);
    }

    private static int GetMaxConcurrentLoads()
        => Data.Settings.ThumbAndIconConcurrency;

    private static async Task ProcessQueueAsync(int generation, CancellationToken workerToken)
    {
        SetIconLoadProgress(true);
        var pullProgressShown = false;
        var inFlight = new List<(Task Task, bool IsIconPull)>();
        try
        {
            while (!workerToken.IsCancellationRequested)
            {
                if (generation != WorkerGeneration)
                    return;

                inFlight.RemoveAll(static t => t.Task.IsCompleted);

                MaybeHideIconPullProgress(ref pullProgressShown, inFlight);

                var max = GetMaxConcurrentLoads();
                while (inFlight.Count < max && !workerToken.IsCancellationRequested)
                {
                    LoadRequest? request;
                    lock (QueueLock)
                    {
                        if (generation != WorkerGeneration)
                            return;

                        if (LoadQueue.Count == 0)
                            break;

                        request = LoadQueue.First!.Value;
                        LoadQueue.RemoveFirst();
                    }

                    if (!request.LabelOnly && !request.Quiet && !pullProgressShown)
                    {
                        SetIconPullProgress(true);
                        pullProgressShown = true;
                    }

                    inFlight.Add((ProcessRequestAsync(request, generation, workerToken), !request.LabelOnly && !request.Quiet));
                }

                if (inFlight.Count == 0)
                {
                    var queueIdle = false;
                    lock (QueueLock)
                    {
                        if (generation != WorkerGeneration)
                            return;

                        // New work may have arrived while we held no lock.
                        if (LoadQueue.Count > 0)
                            continue;

                        WorkerRunning = false;
                        queueIdle = true;
                    }

                    // Raised outside the lock: subscribers may synchronously call back into
                    // methods that also take QueueLock (e.g. via a blocking Dispatcher.Invoke
                    // from this background thread), which would deadlock against the UI thread.
                    if (queueIdle)
                    {
                        if (pullProgressShown)
                            SetIconPullProgress(false);
                        SetIconLoadProgress(false);
                        return;
                    }
                }

                await Task.WhenAny(inFlight.Select(static t => t.Task)).ConfigureAwait(false);
            }
        }
        finally
        {
            if (inFlight.Count > 0)
            {
                try { await Task.WhenAll(inFlight.Select(static t => t.Task)).ConfigureAwait(false); }
                catch { /* per-request failures are handled inside ProcessRequestAsync */ }
            }

            var queueIdle = false;
            lock (QueueLock)
            {
                if (generation == WorkerGeneration)
                {
                    WorkerRunning = false;
                    if (LoadQueue.Count > 0 && !Data.DeviceCts.IsCancellationRequested)
                        StartWorker_NoLock();
                    else
                        queueIdle = true;
                }
            }

            // Raised outside the lock; see comment above.
            if (queueIdle)
            {
                if (pullProgressShown)
                    SetIconPullProgress(false);
                SetIconLoadProgress(false);
            }
        }
    }

    private static async Task ProcessRequestAsync(
        LoadRequest request,
        int generation,
        CancellationToken workerToken)
    {
        var timing = request.Timing;
        Mark(timing, "worker dequeued — start ProcessRequest");
        if (timing is not null)
            CurrentTiming.Value = timing;

        BitmapSource? result = null;
        string? resolvedPackageName = request.PackageName;
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(workerToken, Data.DeviceCts.Token);
            if (request.LabelOnly)
            {
                Mark(timing, "LoadLabelAsync start");
                resolvedPackageName = await LoadLabelAsync(
                    request.Device, request.ApkPath, request.PackageName, linked.Token).ConfigureAwait(false);
                Mark(timing, "LoadLabelAsync done");
                result = null;
            }
            else
            {
                Mark(timing, "LoadIconAsync start");
                (result, resolvedPackageName) = await LoadIconAsync(
                    request.Device, request.ApkPath, request.PackageName, linked.Token, timing).ConfigureAwait(false);
                Mark(timing, result is null ? "LoadIconAsync done (no icon)" : "LoadIconAsync done");
            }
        }
        catch (OperationCanceledException)
        {
            Mark(timing, "cancelled");
            result = null;
        }
        catch (Exception e)
        {
            Mark(timing, $"exception: {e.GetType().Name}: {e.Message}");
#if !DEPLOY
            DebugLog.PrintLine($"APK icon load failed for {request.ApkPath}: {e.Message}");
#endif
            // Deterministic compose/extract failures must not retry on every launch.
            if (!request.LabelOnly && !string.IsNullOrEmpty(request.PackageName))
            {
                MarkFetchResult(
                    request.Device.SerialNumber,
                    request.PackageName,
                    "",
                    DateOnly.FromDateTime(DateTime.Today),
                    FailMarker,
                    label: null);
            }
        }
        finally
        {
            if (timing is not null)
                CurrentTiming.Value = null;
            lock (PendingLock)
                PendingLoads.Remove(request.PullKey);
        }

        if (generation != WorkerGeneration
            || workerToken.IsCancellationRequested
            || Data.DeviceCts.IsCancellationRequested)
        {
            Mark(timing, "discarded (generation/cancel)");
            return;
        }

        if (result is not null && !string.IsNullOrEmpty(resolvedPackageName))
            ApkIconUpdated?.Invoke(request.Device.SerialNumber, resolvedPackageName);
        else if (request.LabelOnly && !string.IsNullOrEmpty(resolvedPackageName))
            ApkIconUpdated?.Invoke(request.Device.SerialNumber, resolvedPackageName);

        IconLoadProgressTick?.Invoke();

        var onReady = request.OnReady;
        var bitmap = result;
        Mark(timing, "dispatch OnReady to UI");
        App.SafeBeginInvoke(() => onReady?.Invoke(bitmap));
    }
}
