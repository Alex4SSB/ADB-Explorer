using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.ViewModels;
using AlphaOmega.Debug;
using AlphaOmega.Debug.Manifest;
using SkiaSharp;
using System.Diagnostics;
using System.Text;

namespace ADB_Explorer.Services;

/// <summary>
/// Lazy-loads launcher icons from device-side <c>.apk</c> files via targeted <c>unzip</c>
/// plus AlphaOmega parsing of <c>AndroidManifest.xml</c> / <c>resources.arsc</c>
/// (including adaptive-icon XML → foreground/background rasters).
/// Cache is keyed by Android package name (shared across app-drive and file paths);
/// invalidation uses the AndroidManifest CRC-32 and a date-only CSV stamp
/// (not <see cref="AppSettings.ThumbsAge"/>).
/// Loads respect <see cref="Data.DeviceCts"/> and are cleared by <see cref="CancelPending"/>.
/// Concurrency follows <see cref="AppSettings.LimitThumbsPullSpeed"/>: one at a time when
/// throttled, otherwise up to <see cref="IconLoadConcurrencyCap"/> (capped well below
/// <see cref="AppSettings.MaxSimultaneousOps"/> because each load fans out into several adb processes).
/// Queue order is <see cref="ApkLoadPriority"/>: Selected → Visible → Background.
/// </summary>
public static partial class ApkIconService
{
    private const string CSV_FILE = "apkIconInfo.csv";
    private const string ICONS_SUBFOLDER = "ApkIcons";
    private const string MANIFEST = "AndroidManifest.xml";
    private const string RESOURCES = "resources.arsc";
    private const string CsvDateFormat = "yyyy-MM-dd";
    /// <summary>CSV sentinel: icon or label fetch failed today — skip retry until the date changes.</summary>
    private const string FailMarker = "!";
    private const int MaxIconCandidatesToProbe = 12;

    private static readonly Encoding CsvEncoding = new UTF8Encoding(true);
    private static readonly ConcurrentDictionary<string, object> DeviceLocks = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Dictionary<string, ApkIconCacheEntry>> DeviceCaches = new(StringComparer.Ordinal);
    private static readonly HashSet<string> PendingLoads = new(StringComparer.Ordinal);
    private static readonly Lock PendingLock = new();

    private static readonly Lock QueueLock = new();
    private static readonly LinkedList<LoadRequest> LoadQueue = new();
    private static bool WorkerRunning;
    private static int WorkerGeneration;
    private static CancellationTokenSource? WorkerCts;

    private static readonly string[] ImageExtensions = [".png", ".webp", ".jpg", ".jpeg"];
    private static readonly string[] DensityOrder =
    [
        "xxxhdpi", "xxhdpi", "xhdpi", "hdpi", "tvdpi", "mdpi", "ldpi",
    ];

    public static event Action<string, string>? ApkIconUpdated;

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

    /// <summary>
    /// When true, <see cref="BeginLoad"/> / preload / scroll priority updates enqueue nothing.
    /// Set by <see cref="StopAllLoading"/>; cleared by <see cref="CancelPending"/> on navigate/disconnect
    /// (not by force-reload, which keeps the stop so only the timed package runs).
    /// </summary>
    public static bool IsLoadingStopped => Volatile.Read(ref LoadingStopped) != 0;

    private static int LoadingStopped;

    /// <param name="IconExt">
    /// File extension only (<c>.webp</c>/<c>.png</c>), <see cref="FailMarker"/> if fetch failed today,
    /// or empty if not yet attempted. Local file is always <c>{package}{IconExt}</c>.
    /// </param>
    /// <param name="Label">
    /// Localized labels: <c>lang=text;lang2=text2</c> (accumulates as the app UI language changes),
    /// <see cref="FailMarker"/> if the current locale failed today, or null/empty if unknown.
    /// Legacy bare labels are attributed to <see cref="GetAppLocaleKey"/> (never <c>*</c>).
    /// </param>
    private readonly record struct ApkIconCacheEntry(
        string ManifestCrc,
        DateOnly CheckedDate,
        string IconExt,
        string? Label);

    private static bool _uiLanguageHooked;

#if DEBUG
    private static readonly AsyncLocal<ApkLoadTiming?> CurrentTiming = new();
#endif
    private static readonly AsyncLocal<ApkIconExtractSession?> CurrentExtractSession = new();

#if DEBUG
    /// <summary>
    /// Records a step on the active force-reload timing log (no-op when none is active).
    /// Call from ADB / archive / sync helpers so every device round-trip is measured.
    /// </summary>
    public static void MarkLoadStep(string step) => CurrentTiming.Value?.Mark(step);
#endif

    /// <summary>
    /// Queue ordering for APK icon/label loads. Higher values are dequeued first.
    /// </summary>
    public enum ApkLoadPriority : byte
    {
        Background = 0,
        Visible = 1,
        Selected = 2,
    }

    private sealed class LoadRequest(
        string PullKey,
        LogicalDeviceViewModel Device,
        string ApkPath,
        string? PackageName,
        Action<BitmapSource?>? OnReady,
        bool LabelOnly = false,
        ApkLoadPriority Priority = ApkLoadPriority.Background)
    {
        public string PullKey { get; } = PullKey;
        public LogicalDeviceViewModel Device { get; } = Device;
        public string ApkPath { get; } = ApkPath;
        public string? PackageName { get; set; } = PackageName;
        public Action<BitmapSource?>? OnReady { get; set; } = OnReady;
        public bool LabelOnly { get; set; } = LabelOnly;
        public ApkLoadPriority Priority { get; set; } = Priority;
#if DEBUG
        public ApkLoadTiming? Timing { get; set; }
#endif
    }

#if DEBUG
    /// <summary>Step log for DEBUG force-reload; thread-safe for parallel sub-tasks.</summary>
    private sealed class ApkLoadTiming(string packageName, string apkPath)
    {
        private readonly object _lock = new();
        private readonly Stopwatch _sw = Stopwatch.StartNew();
        private readonly DateTime _startedAt = DateTime.Now;
        private readonly List<(long AtMs, long StepMs, string Step)> _steps = [];
        private long _lastMark;

        public void Mark(string step)
        {
            lock (_lock)
            {
                var at = _sw.ElapsedMilliseconds;
                var stepMs = at - _lastMark;
                _lastMark = at;
                _steps.Add((at, stepMs, step));
            }
        }

        public string Format(bool success, string? note = null)
        {
            lock (_lock)
            {
                _sw.Stop();
                var sb = new StringBuilder();
                sb.AppendLine($"APK icon reload — {packageName}");
                sb.AppendLine($"Path: {apkPath}");
                sb.AppendLine($"Started: {_startedAt:yyyy-MM-dd HH:mm:ss.fff}");
                sb.AppendLine($"Result: {(success ? "ok" : "failed")}{(string.IsNullOrEmpty(note) ? "" : $" ({note})")}");
                sb.AppendLine("Steps (step ms / cumulative ms):");
                foreach (var (atMs, stepMs, step) in _steps)
                    sb.AppendLine($"  +{stepMs,7} ms  (t={atMs,7})  {step}");
                sb.AppendLine($"Total: {_sw.ElapsedMilliseconds} ms");
                return sb.ToString();
            }
        }
    }
#endif

    /// <summary>
    /// <see cref="AppSettings.ThumbnailMode.OnConnect"/> is treated as
    /// <see cref="AppSettings.ThumbnailMode.OnPhotoDir"/> for APK icons
    /// (preload on app-drive open only — never on device connect).
    /// </summary>
    public static AppSettings.ThumbnailMode EffectiveThumbsMode
    {
        get
        {
            var mode = Data.Settings.ThumbsMode;
            return mode is AppSettings.ThumbnailMode.OnConnect
                ? AppSettings.ThumbnailMode.OnPhotoDir
                : mode;
        }
    }

    public static bool IsEnabled => EffectiveThumbsMode is not AppSettings.ThumbnailMode.Off;

    public static bool ShouldPreloadOnAppDrive =>
        EffectiveThumbsMode is AppSettings.ThumbnailMode.OnPhotoDir;

    public static bool CanLoadOnDevice(string deviceId)
        => IsEnabled && ShellCommands.UnzipExists(deviceId);

    /// <summary>
    /// Drops queued work and cancels the in-flight load (call when <see cref="Data.DeviceCts"/> is cancelled).
    /// </summary>
    /// <param name="clearLoadingStopped">
    /// When true (default), allows new loads again — use on navigate / device change.
    /// Pass false from <see cref="StopAllLoading"/> / force-reload so scroll cannot restart the queue.
    /// </param>
    public static void CancelPending(bool clearLoadingStopped = true)
    {
        if (clearLoadingStopped)
            Volatile.Write(ref LoadingStopped, 0);

        CancellationTokenSource? toCancel;
        lock (QueueLock)
        {
            foreach (var request in LoadQueue)
            {
                lock (PendingLock)
                    PendingLoads.Remove(request.PullKey);
            }

            LoadQueue.Clear();
            WorkerRunning = false;
            WorkerGeneration++;
            toCancel = WorkerCts;
            WorkerCts = null;
        }

        try { toCancel?.Cancel(); }
        catch (ObjectDisposedException) { /* replaced */ }

        try { toCancel?.Dispose(); }
        catch { /* ignore */ }

        SetIconLoadProgress(false, force: true);
        SetIconPullProgress(false, force: true);
    }

    public static void BeginLoad(
        LogicalDeviceViewModel device,
        string apkPath,
        string? packageName = null,
        Action<BitmapSource?>? onReady = null,
        ApkLoadPriority priority = ApkLoadPriority.Background)
        => BeginLoadCore(device, apkPath, packageName, onReady, priority);

    private static void BeginLoadCore(
        LogicalDeviceViewModel device,
        string apkPath,
        string? packageName,
        Action<BitmapSource?>? onReady,
        ApkLoadPriority priority
#if DEBUG
        , ApkLoadTiming? timing = null
#endif
        )
    {
        if (device is null || string.IsNullOrEmpty(apkPath) || !CanLoadOnDevice(device.ID))
        {
#if DEBUG
            timing?.Mark("BeginLoad aborted (device/path/disabled)");
#endif
            onReady?.Invoke(null);
            return;
        }

        if (Data.DeviceCts.IsCancellationRequested)
        {
#if DEBUG
            timing?.Mark("BeginLoad aborted (cancelled)");
#endif
            onReady?.Invoke(null);
            return;
        }

        if (IsLoadingStopped)
        {
#if DEBUG
            timing?.Mark("BeginLoad aborted (loading stopped)");
#endif
            onReady?.Invoke(null);
            return;
        }

        packageName ??= TryResolvePackageName(apkPath);
        if (!string.IsNullOrEmpty(packageName))
        {
            var cached = TryGetCachedIcon(device, packageName);
            if (cached is not null)
            {
#if DEBUG
                timing?.Mark("cache hit (unexpected during force reload)");
#endif
                onReady?.Invoke(cached);
                return;
            }
        }

        // Dedupe by package when known so app-drive and file-path loads share one pull.
        // Icon loads also fetch the label, so a pending label-only request is upgraded in place.
        var pullKey = $"{device.SerialNumber}|{packageName ?? apkPath}";
        var labelKey = string.IsNullOrEmpty(packageName) ? null : LabelPullKey(device.SerialNumber, packageName);

        var upgradeLabelOnly = false;
        lock (PendingLock)
        {
            if (labelKey is not null && PendingLoads.Contains(labelKey) && !PendingLoads.Contains(pullKey))
            {
                upgradeLabelOnly = true;
            }
            else if (!PendingLoads.Add(pullKey))
            {
#if DEBUG
                timing?.Mark("attached to in-flight/queued request");
#endif
                AttachOnReady(pullKey, packageName, onReady, priority);
                return;
            }
        }

        if (upgradeLabelOnly)
        {
#if DEBUG
            timing?.Mark("upgrading pending label-only → icon load");
            UpgradeLabelOnlyToIcon(labelKey!, pullKey, device, apkPath, packageName, onReady, priority, timing);
#else
            UpgradeLabelOnlyToIcon(labelKey!, pullKey, device, apkPath, packageName, onReady, priority);
#endif
            return;
        }

        var request = new LoadRequest(pullKey, device, apkPath, packageName, onReady, LabelOnly: false, priority)
#if DEBUG
        {
            Timing = timing,
        }
#endif
        ;
        Enqueue(request, priority);
    }

    public static void BeginLoadForFile(FileClass file, ApkLoadPriority priority = ApkLoadPriority.Background)
    {
        if (file is null || !file.IsApk || file.ApkIcon is not null || !IsEnabled)
            return;

        if (Data.DevicesObject?.Current is not { } device || !CanLoadOnDevice(device.ID))
            return;

        var packageName = TryResolvePackageName(file.FullPath);
        if (!string.IsNullOrEmpty(packageName))
        {
            var cached = TryGetCachedIcon(device, packageName);
            if (cached is not null)
            {
                file.ApplyApkIcon(cached);
                return;
            }
        }

        BeginLoad(device, file.FullPath, packageName, bmp =>
        {
            if (bmp is null || Data.DevicesObject?.Current?.SerialNumber != device.SerialNumber)
                return;

            file.ApplyApkIcon(bmp);
        }, priority);
    }

    public static void BeginLoadForPackage(Package package, ApkLoadPriority priority = ApkLoadPriority.Background)
    {
        if (package is null || string.IsNullOrEmpty(package.Path) || !IsEnabled)
        {
            if (package is not null)
                package.IconLoadCompleted = true;
            return;
        }

        if (Data.DevicesObject?.Current is not { } device || !CanLoadOnDevice(device.ID))
        {
            package.IconLoadCompleted = true;
            return;
        }

        // Force-reload / StopAllLoading: do not enqueue and do not treat as a finished miss
        // (otherwise the tile flips to the green Bugdroid instead of staying grayscale).
        if (IsLoadingStopped)
            return;

        if (package.Icon is not null)
        {
            package.IconLoadCompleted = true;
            ApplyCachedLabel(device, package);
            BeginEnsureLabelForPackage(package, priority);
            return;
        }

        if (!string.IsNullOrEmpty(package.Name))
        {
            var cached = TryGetCachedIcon(device, package.Name);
            if (cached is not null)
            {
                ApplyCachedLabel(device, package);
                package.Icon = cached;
                BeginEnsureLabelForPackage(package, priority);
                return;
            }

            if (IsIconFailedToday(device, package.Name))
            {
                package.IconLoadCompleted = true;
                ApplyCachedLabel(device, package);
                BeginEnsureLabelForPackage(package, priority);
                return;
            }
        }

        // Full icon pull also writes the label — do not enqueue a separate label-only job.
        BeginLoad(device, package.Path, package.Name, bmp =>
        {
            if (Data.DevicesObject?.Current?.SerialNumber != device.SerialNumber)
                return;

            ApplyCachedLabel(device, package);
            if (bmp is not null)
                package.Icon = bmp;
            else if (!IsLoadingStopped)
                package.IconLoadCompleted = true;
        }, priority);
    }

    /// <summary>
    /// Fetches the package label when missing, even if the icon is already cached/displayed.
    /// When the icon still needs loading, delegates to <see cref="BeginLoadForPackage"/> so
    /// manifest/resources are pulled only once for both icon and name.
    /// </summary>
    public static void BeginEnsureLabelForPackage(Package package, ApkLoadPriority priority = ApkLoadPriority.Background)
    {
        if (package is null || string.IsNullOrEmpty(package.Path) || string.IsNullOrEmpty(package.Name))
            return;

        if (IsLoadingStopped)
            return;

        if (Data.DevicesObject?.Current is not { } device || !CanLoadOnDevice(device.ID))
            return;

        ApplyCachedLabel(device, package);
        // Missing locale for the current UI language must re-fetch even if another locale is cached.
        if (!NeedsLabelFetch(device, package.Name))
            return;

        // Icon load already pulls the label — piggy-back instead of a second pull.
        if (package.Icon is null && !IsIconFailedToday(device, package.Name))
        {
            var iconKey = $"{device.SerialNumber}|{package.Name}";
            lock (PendingLock)
            {
                if (PendingLoads.Contains(iconKey))
                {
                    AttachOnReady(iconKey, package.Name, _ =>
                    {
                        if (Data.DevicesObject?.Current?.SerialNumber != device.SerialNumber)
                            return;
                        ApplyCachedLabel(device, package);
                    }, priority);
                    return;
                }
            }

            BeginLoadForPackage(package, priority);
            return;
        }

        var pullKey = LabelPullKey(device.SerialNumber, package.Name);
        lock (PendingLock)
        {
            if (!PendingLoads.Add(pullKey))
            {
                AttachOnReady(pullKey, package.Name, _ =>
                {
                    if (Data.DevicesObject?.Current?.SerialNumber != device.SerialNumber)
                        return;
                    ApplyCachedLabel(device, package);
                }, priority);
                return;
            }
        }

        Enqueue(new LoadRequest(pullKey, device, package.Path, package.Name, _ =>
        {
            if (Data.DevicesObject?.Current?.SerialNumber != device.SerialNumber)
                return;
            ApplyCachedLabel(device, package);
        }, LabelOnly: true, priority), priority);
    }

    /// <summary>
    /// Queues loads for packages not yet requested. Visible tiles and selection raise priority
    /// via <see cref="UpdatePackageLoadPriorities"/> / <see cref="PackageIconViewModel"/>.
    /// </summary>
    public static void BeginPreloadPackages(IEnumerable<Package> packages)
    {
        if (packages is null || !IsEnabled || IsLoadingStopped)
            return;

        if (Data.DevicesObject?.Current is not { } device || !CanLoadOnDevice(device.ID))
            return;

        // Single path per package: icon load fetches the label; label-only only when icon is done.
        foreach (var package in packages)
            BeginLoadForPackage(package, ApkLoadPriority.Background);
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

        if (!IsEnabled || Data.DevicesObject?.Current is not { } device || !CanLoadOnDevice(device.ID))
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
    /// Stops the worker, drops the queue, and blocks further icon/label loads (including
    /// scroll/visibility kicks) until App Drive is left or the device changes.
    /// </summary>
    public static void StopAllLoading()
    {
        Volatile.Write(ref LoadingStopped, 1);
        CancelPending(clearLoadingStopped: false);
    }

#if DEBUG
    /// <summary>
    /// Clears cache for <paramref name="package"/> and runs a dedicated <see cref="LoadIconAsync"/>
    /// on a long-running thread (bypasses the shared queue so cancelled preload work cannot
    /// starve or re-warm the cache before measurement). Every nested ADB/file call is timed via
    /// <see cref="MarkLoadStep"/>.
    /// </summary>
    public static void ForceReloadPackage(Package package, Action<string>? onCompleted = null)
    {
        if (package is null || string.IsNullOrEmpty(package.Path) || string.IsNullOrEmpty(package.Name))
        {
            onCompleted?.Invoke("No package / path");
            return;
        }

        if (Data.DevicesObject?.Current is not { } device || !CanLoadOnDevice(device.ID))
        {
            onCompleted?.Invoke("Device unavailable or APK icons disabled");
            return;
        }

        var timing = new ApkLoadTiming(package.Name, package.Path);
        var apkPath = package.Path;
        var packageName = package.Name;

        timing.Mark("CancelPending (stop competing loads)");
        // Keep / set stopped so scroll and tile materialization cannot enqueue other packages.
        Volatile.Write(ref LoadingStopped, 1);
        CancelPending(clearLoadingStopped: false);

        // Cancelled adb processes may still hold the thread pool for a long time; wait them out
        // so a late MarkFetchResult cannot re-warm the cache before our timed load.
        timing.Mark("wait for adb command drain");
        WaitForAdbIdle(TimeSpan.FromSeconds(20), timing);

        timing.Mark("invalidate cache + clear UI (post-drain)");
        InvalidatePackageCache(device, packageName);
        // Clear completed first so the Icon=null notify already binds the grayscale placeholder.
        package.IconLoadCompleted = false;
        package.Icon = null;
        package.Label = null;

        _ = Task.Factory.StartNew(async () =>
        {
            CurrentTiming.Value = timing;
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(Data.DeviceCts.Token);
                timing.Mark("LoadIconAsync start (direct, no queue)");
                var (bmp, _) = await LoadIconAsync(device, apkPath, packageName, cts.Token, timing)
                    .ConfigureAwait(false);

                timing.Mark(bmp is null ? "LoadIconAsync done (no icon)" : "LoadIconAsync done");

                App.SafeBeginInvoke(() =>
                {
                    try
                    {
                        if (Data.DevicesObject?.Current?.SerialNumber == device.SerialNumber)
                        {
                            ApplyCachedLabel(device, package);
                            if (bmp is not null)
                                package.Icon = bmp;
                            else
                                package.IconLoadCompleted = true;
                        }

                        timing.Mark(bmp is not null ? "UI apply complete" : "UI apply complete (no icon)");
                        onCompleted?.Invoke(timing.Format(bmp is not null, bmp is null ? "icon null" : null));
                    }
                    catch (Exception e)
                    {
                        timing.Mark($"UI apply exception: {e.Message}");
                        onCompleted?.Invoke(timing.Format(false, e.Message));
                    }
                });
            }
            catch (Exception e)
            {
                timing.Mark($"ForceReload exception: {e.GetType().Name}: {e.Message}");
                App.SafeBeginInvoke(() =>
                {
                    if (Data.DevicesObject?.Current?.SerialNumber == device.SerialNumber)
                    {
                        ApplyCachedLabel(device, package);
                        package.IconLoadCompleted = true;
                    }
                    onCompleted?.Invoke(timing.Format(false, e.Message));
                });
            }
            finally
            {
                CurrentTiming.Value = null;
            }
        },
        CancellationToken.None,
        TaskCreationOptions.LongRunning,
        TaskScheduler.Default).Unwrap();
    }

    private static void WaitForAdbIdle(TimeSpan timeout, ApkLoadTiming timing)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (ADBService.IsCommandActive && DateTime.UtcNow < deadline)
            Thread.Sleep(50);

        if (ADBService.IsCommandActive)
            timing.Mark($"adb drain timeout after {(int)timeout.TotalSeconds}s (still active)");
        else
            timing.Mark("adb drain complete");
    }
#endif

    private static string LabelPullKey(string serial, string packageName)
        => $"{serial}|{packageName}|label";

    private static void InvalidatePackageCache(LogicalDeviceViewModel device, string packageName)
    {
        lock (GetDeviceLock(device.SerialNumber))
        {
            var cache = GetOrLoadCache(device.SerialNumber);
            if (!cache.TryGetValue(packageName, out var entry))
                return;

            if (IsSuccessfulIconExt(entry.IconExt))
            {
                var path = GetLocalIconPath(device.SerialNumber, packageName, entry.IconExt);
                try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
            }

            cache.Remove(packageName);
            WriteCache(device.SerialNumber, cache);
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
#if DEBUG
        , ApkLoadTiming? timing = null
#endif
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
#if DEBUG
            {
                Timing = timing ?? upgraded.Timing,
            }
#endif
            , priority);
            return;
        }

        // Label-only was already running (not in queue) — start a dedicated icon load.
        Enqueue(new LoadRequest(iconKey, device, apkPath, packageName, onReady, LabelOnly: false, priority)
#if DEBUG
        {
            Timing = timing,
        }
#endif
        , priority);
    }

    /// <summary>
    /// Looks up an installed package whose <see cref="Package.Path"/> matches the APK path.
    /// </summary>
    public static string? TryResolvePackageName(string apkPath)
    {
        if (string.IsNullOrEmpty(apkPath) || Data.Packages is null || Data.Packages.Count == 0)
            return null;

        foreach (var package in Data.Packages)
        {
            if (string.Equals(package.Path, apkPath, StringComparison.Ordinal))
                return package.Name;
        }

        return null;
    }

    /// <param name="packageName">Android package id used as the CSV / local-file cache key.</param>
    public static BitmapSource? TryGetCachedIcon(LogicalDeviceViewModel device, string packageName)
    {
        if (device is null || string.IsNullOrEmpty(packageName) || !IsEnabled)
            return null;

        lock (GetDeviceLock(device.SerialNumber))
        {
            var cache = GetOrLoadCache(device.SerialNumber);
            if (!cache.TryGetValue(packageName, out var entry))
                return null;

            if (entry.CheckedDate != DateOnly.FromDateTime(DateTime.Today))
                return null;

            if (!IsSuccessfulIconExt(entry.IconExt))
                return null;

            var localPath = GetLocalIconPath(device.SerialNumber, packageName, entry.IconExt);
            return File.Exists(localPath) ? DecodeBitmap(localPath) : null;
        }
    }

    public static string? TryGetCachedLabel(LogicalDeviceViewModel device, string packageName)
    {
        EnsureUiLanguageHook();

        if (device is null || string.IsNullOrEmpty(packageName))
            return null;

        lock (GetDeviceLock(device.SerialNumber))
        {
            var cache = GetOrLoadCache(device.SerialNumber);
            if (!cache.TryGetValue(packageName, out var entry))
                return null;

            var picked = PickLocalizedLabel(entry.Label);
            return IsUsableDisplayLabel(picked) ? picked : null;
        }
    }

    private static void ApplyCachedLabel(LogicalDeviceViewModel device, Package package)
    {
        if (string.IsNullOrEmpty(package.Name))
            return;

        var label = TryGetCachedLabel(device, package.Name);
        if (!string.IsNullOrWhiteSpace(label))
            package.Label = label;
    }

    private static bool IsIconFailedToday(LogicalDeviceViewModel device, string packageName)
    {
        lock (GetDeviceLock(device.SerialNumber))
        {
            var cache = GetOrLoadCache(device.SerialNumber);
            return cache.TryGetValue(packageName, out var entry)
                   && entry.CheckedDate == DateOnly.FromDateTime(DateTime.Today)
                   && entry.IconExt == FailMarker;
        }
    }

    private static bool NeedsLabelFetch(LogicalDeviceViewModel device, string packageName)
    {
        EnsureUiLanguageHook();

        lock (GetDeviceLock(device.SerialNumber))
        {
            var cache = GetOrLoadCache(device.SerialNumber);
            if (!cache.TryGetValue(packageName, out var entry))
                return true;

            var locale = GetAppLocaleKey();
            var map = ParseLocalizedLabels(entry.Label);
            if (map.TryGetValue(locale, out var forLocale))
            {
                if (IsUsableDisplayLabel(forLocale))
                    return false;

                // Failed for this locale today — do not retry until the date rolls.
                return forLocale != FailMarker
                       || entry.CheckedDate != DateOnly.FromDateTime(DateTime.Today);
            }

            // Other locales may exist — still need a fetch for the current UI language.
            return true;
        }
    }

    /// <summary>True when a display label is real (not missing, fail marker, pseudo-locale, or lossy junk).</summary>
    private static bool IsUsableDisplayLabel(string? label)
        => !string.IsNullOrWhiteSpace(label)
           && label != FailMarker
           && !ArscResourceResolver.IsPseudoAccentLabel(label)
           && !IsCorruptCachedLabel(label);

    /// <summary>
    /// Detects labels mangled by ANSI/Default round-trips (Hebrew → <c>????</c>) or bad UTF-8 (<c>U+FFFD</c>).
    /// Those must not block re-fetch from the APK.
    /// </summary>
    private static bool IsCorruptCachedLabel(string label)
    {
        if (label.Contains('\uFFFD', StringComparison.Ordinal))
            return true;

        // Multilang field: inspect each value without re-entering parse migration.
        if (label.Contains('=', StringComparison.Ordinal))
        {
            var any = false;
            var allBad = true;
            foreach (var part in SplitLocalizedLabelParts(label))
            {
                var eq = part.IndexOf('=');
                if (eq <= 0)
                    continue;
                var value = UnescapeLocalizedLabelValue(part[(eq + 1)..]);
                if (string.IsNullOrEmpty(value))
                    continue;
                any = true;
                if (!IsCorruptCachedLabel(value) && !ArscResourceResolver.IsPseudoAccentLabel(value))
                    allBad = false;
            }

            return any && allBad;
        }

        var significant = 0;
        var placeholders = 0;
        foreach (var c in label)
        {
            if (char.IsWhiteSpace(c))
                continue;

            significant++;
            if (c == '?')
                placeholders++;
        }

        return significant > 0 && placeholders * 2 >= significant;
    }

    /// <summary>
    /// Stable CSV key for the current app UI language (<c>fr</c>, <c>he</c>, <c>en</c>, …).
    /// Uses <see cref="AppSettings.ActualUICulture"/> so an unset (invariant) preference
    /// resolves to the real OS UI language — never <c>*</c> or invariant <c>iv</c>.
    /// </summary>
    private static string GetAppLocaleKey()
    {
        var culture = Data.Settings.ActualUICulture;
        if (culture.Equals(CultureInfo.InvariantCulture))
            culture = Data.Settings.OriginalUICulture;

        var lang = culture.TwoLetterISOLanguageName;
        if (string.IsNullOrEmpty(lang) || lang.Equals("iv", StringComparison.OrdinalIgnoreCase))
            return "en";

        return lang.ToLowerInvariant();
    }

    private static Dictionary<string, string> ParseLocalizedLabels(string? field)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(field) || field == FailMarker)
            return result;

        field = field.Trim();
        var localeKey = GetAppLocaleKey();

        // Legacy bare label (no lang=) → attribute to the actual UI locale.
        if (!field.Contains('=', StringComparison.Ordinal))
        {
            if (IsUsableDisplayLabel(field))
                result[localeKey] = field;
            return result;
        }

        foreach (var part in SplitLocalizedLabelParts(field))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0)
                continue;

            var key = part[..eq].Trim();
            var value = UnescapeLocalizedLabelValue(part[(eq + 1)..]);
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value))
                continue;

            // Drop Android pseudo-accent values so the locale is fetched again.
            if (ArscResourceResolver.IsPseudoAccentLabel(value))
                continue;

            result[key] = value;
        }

        // Migrate obsolete "*" keys (from an earlier invariant/default encoding) onto the real locale.
        if (result.Remove("*", out var starVal))
        {
            if (IsUsableDisplayLabel(starVal)
                && (!result.TryGetValue(localeKey, out var existing)
                    || !IsUsableDisplayLabel(existing)
                    || ArscResourceResolver.IsPseudoAccentLabel(existing)))
            {
                result[localeKey] = starVal;
            }
        }

        return result;
    }

    private static IEnumerable<string> SplitLocalizedLabelParts(string field)
    {
        var start = 0;
        for (var i = 0; i < field.Length; i++)
        {
            if (field[i] == '\\' && i + 1 < field.Length)
            {
                i++;
                continue;
            }

            if (field[i] == ';')
            {
                if (i > start)
                    yield return field[start..i];
                start = i + 1;
            }
        }

        if (start < field.Length)
            yield return field[start..];
    }

    private static string EscapeLocalizedLabelValue(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace(";", "\\;", StringComparison.Ordinal)
                .Replace("=", "\\=", StringComparison.Ordinal)
                .Replace("|", "_", StringComparison.Ordinal);

    private static string UnescapeLocalizedLabelValue(string value)
    {
        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length)
            {
                sb.Append(value[i + 1]);
                i++;
                continue;
            }

            sb.Append(value[i]);
        }

        return sb.ToString();
    }

    private static string EncodeLocalizedLabels(IReadOnlyDictionary<string, string> map)
    {
        if (map.Count == 0)
            return "";

        return string.Join(';', map
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => $"{kv.Key}={EscapeLocalizedLabelValue(kv.Value)}"));
    }

    /// <summary>Merges <paramref name="localeValue"/> for the current UI locale into an existing label field.</summary>
    private static string MergeLocaleLabel(string? existingField, string localeValue)
    {
        var map = ParseLocalizedLabels(existingField == FailMarker ? null : existingField);
        map[GetAppLocaleKey()] = localeValue;
        return EncodeLocalizedLabels(map);
    }

    private static string? PickLocalizedLabel(string? field)
    {
        if (string.IsNullOrWhiteSpace(field) || field == FailMarker)
            return null;

        var map = ParseLocalizedLabels(field);
        if (map.Count == 0)
            return null;

        var locale = GetAppLocaleKey();
        if (map.TryGetValue(locale, out var exact) && IsUsableDisplayLabel(exact))
            return exact;

        // Android legacy aliases (stored under he while resources used iw — we store app keys only).
        foreach (var tag in ArscResourceResolver.AndroidLanguageTagsFor(Data.Settings.ActualUICulture))
        {
            var lang = tag.Contains('-', StringComparison.Ordinal) ? tag.Split('-')[0] : tag;
            // Map android iw → app he for lookup if somehow stored that way.
            var appKey = lang switch
            {
                "iw" => "he",
                "in" => "id",
                "ji" => "yi",
                _ => lang,
            };
            if (map.TryGetValue(appKey, out var aliased) && IsUsableDisplayLabel(aliased))
                return aliased;
        }

        if (map.TryGetValue("en", out var english) && IsUsableDisplayLabel(english))
            return english;

        return map.Values.FirstOrDefault(IsUsableDisplayLabel);
    }

    private static void EnsureUiLanguageHook()
    {
        if (_uiLanguageHooked)
            return;

        _uiLanguageHooked = true;
        Data.Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not (nameof(AppSettings.UILanguage) or nameof(AppSettings.UICulture)))
                return;

            App.SafeBeginInvoke(OnUiLanguageChanged);
        };
    }

    private static void OnUiLanguageChanged()
    {
        if (Data.Packages is null || Data.Packages.Count == 0)
            return;

        if (Data.DevicesObject?.Current is not { } device)
            return;

        foreach (var package in Data.Packages)
        {
            ApplyCachedLabel(device, package);
            BeginEnsureLabelForPackage(package, ApkLoadPriority.Background);
        }
    }

    private static bool IsSuccessfulIconExt(string? iconExt)
        => !string.IsNullOrEmpty(iconExt)
           && iconExt != FailMarker
           && iconExt.StartsWith('.');

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
            var device = Data.DevicesObject?.Current;
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

    /// <summary>
    /// Unlike a plain file pull, a single icon load fans out into several concurrent <c>adb.exe</c>
    /// process launches (manifest zip listing, staging extraction, cleanup, and per-split retries),
    /// so reusing <see cref="AppSettings.MaxSimultaneousOps"/> as-is (default 32, up to 999) can spawn
    /// dozens to hundreds of OS processes at once, starving the UI thread and looking like a freeze.
    /// Cap far below that setting regardless of how high the user has configured it.
    /// </summary>
    private const int IconLoadConcurrencyCap = 6;

    private static int GetMaxConcurrentLoads()
        => Data.Settings.LimitThumbsPullSpeed
            ? 1
            : Math.Min(Math.Clamp(Data.Settings.MaxSimultaneousOps, 1, AppSettings.MaxSimultaneousOpsMax), IconLoadConcurrencyCap);

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

                    if (!request.LabelOnly && !pullProgressShown)
                    {
                        SetIconPullProgress(true);
                        pullProgressShown = true;
                    }

                    inFlight.Add((ProcessRequestAsync(request, generation, workerToken), !request.LabelOnly));
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

    private static async Task ProcessRequestAsync(
        LoadRequest request,
        int generation,
        CancellationToken workerToken)
    {
#if DEBUG
        var timing = request.Timing;
        timing?.Mark("worker dequeued — start ProcessRequest");
        if (timing is not null)
            CurrentTiming.Value = timing;
#endif

        BitmapSource? result = null;
        string? resolvedPackageName = request.PackageName;
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(workerToken, Data.DeviceCts.Token);
            if (request.LabelOnly)
            {
#if DEBUG
                timing?.Mark("LoadLabelAsync start");
#endif
                resolvedPackageName = await LoadLabelAsync(
                    request.Device, request.ApkPath, request.PackageName, linked.Token).ConfigureAwait(false);
#if DEBUG
                timing?.Mark("LoadLabelAsync done");
#endif
                result = null;
            }
            else
            {
#if DEBUG
                timing?.Mark("LoadIconAsync start");
                (result, resolvedPackageName) = await LoadIconAsync(
                    request.Device, request.ApkPath, request.PackageName, linked.Token, timing).ConfigureAwait(false);
                timing?.Mark(result is null ? "LoadIconAsync done (no icon)" : "LoadIconAsync done");
#else
                (result, resolvedPackageName) = await LoadIconAsync(
                    request.Device, request.ApkPath, request.PackageName, linked.Token).ConfigureAwait(false);
#endif
            }
        }
        catch (OperationCanceledException)
        {
#if DEBUG
            timing?.Mark("cancelled");
#endif
            result = null;
        }
        catch (Exception e)
        {
#if DEBUG
            timing?.Mark($"exception: {e.GetType().Name}: {e.Message}");
#endif
#if !DEPLOY
            DebugLog.PrintLine($"APK icon load failed for {request.ApkPath}: {e.Message}");
#endif
        }
        finally
        {
#if DEBUG
            if (timing is not null)
                CurrentTiming.Value = null;
#endif
            lock (PendingLock)
                PendingLoads.Remove(request.PullKey);
        }

        if (generation != WorkerGeneration
            || workerToken.IsCancellationRequested
            || Data.DeviceCts.IsCancellationRequested)
        {
#if DEBUG
            timing?.Mark("discarded (generation/cancel)");
#endif
            return;
        }

        if (result is not null && !string.IsNullOrEmpty(resolvedPackageName))
            ApkIconUpdated?.Invoke(request.Device.SerialNumber, resolvedPackageName);
        else if (request.LabelOnly && !string.IsNullOrEmpty(resolvedPackageName))
            ApkIconUpdated?.Invoke(request.Device.SerialNumber, resolvedPackageName);

        IconLoadProgressTick?.Invoke();

        var onReady = request.OnReady;
        var bitmap = result;
#if DEBUG
        timing?.Mark("dispatch OnReady to UI");
#endif
        App.SafeBeginInvoke(() => onReady?.Invoke(bitmap));
    }

    private static async Task<string?> LoadLabelAsync(
        LogicalDeviceViewModel device,
        string apkPath,
        string? packageName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var serial = device.SerialNumber;
        var today = DateOnly.FromDateTime(DateTime.Today);
        packageName ??= TryResolvePackageName(apkPath);

        if (!string.IsNullOrEmpty(packageName) && !NeedsLabelFetch(device, packageName))
            return packageName;

        var manifestListingTask = Task.Run(
            () => ArchiveListing.FetchZipMemberListing(device.ID, apkPath, [MANIFEST], cancellationToken),
            cancellationToken);
        var metaTask = PullManifestAndResourcesAsync(device, apkPath, cancellationToken);
        await Task.WhenAll(manifestListingTask, metaTask).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var manifestEntry = FindEntry(manifestListingTask.Result, MANIFEST);
        var manifestCrc = manifestEntry is null || string.IsNullOrEmpty(manifestEntry.Value.Crc)
            ? ""
            : NormalizeCrc(manifestEntry.Value.Crc);

        var (manifestBytes, resourcesBytes) = metaTask.Result;
        if (manifestBytes is null || manifestBytes.Length == 0 || resourcesBytes is null || resourcesBytes.Length == 0)
        {
            if (!string.IsNullOrEmpty(packageName))
                MarkFetchResult(serial, packageName, manifestCrc, today, iconExt: null, label: FailMarker);
            return packageName;
        }

        packageName ??= TryReadPackageName(manifestBytes, resourcesBytes);
        if (string.IsNullOrEmpty(packageName))
            return null;

        var label = TryReadPackageLabel(manifestBytes, resourcesBytes);
        MarkFetchResult(serial, packageName, manifestCrc, today, iconExt: null, label: label ?? FailMarker);
        return packageName;
    }

    private static async Task<(BitmapSource? Icon, string? PackageName)> LoadIconAsync(
        LogicalDeviceViewModel device,
        string apkPath,
        string? packageName,
        CancellationToken cancellationToken
#if DEBUG
        , ApkLoadTiming? timing = null
#endif
        )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var serial = device.SerialNumber;
        var deviceId = device.ID;
        var today = DateOnly.FromDateTime(DateTime.Today);

        packageName ??= TryResolvePackageName(apkPath);
        // Force-reload timing must never short-circuit on a cache that a cancelled load re-warmed.
#if DEBUG
        if (timing is not null)
        {
            timing.Mark("skip warm/fail cache (force-reload measurement)");
        }
        else
#endif
        if (!string.IsNullOrEmpty(packageName))
        {
            lock (GetDeviceLock(serial))
            {
                var cache = GetOrLoadCache(serial);
                if (cache.TryGetValue(packageName, out var warm)
                    && warm.CheckedDate == today
                    && IsSuccessfulIconExt(warm.IconExt))
                {
                    var warmPath = GetLocalIconPath(serial, packageName, warm.IconExt);
                    if (File.Exists(warmPath))
                    {
#if DEBUG
                        timing?.Mark("warm cache hit (local icon file)");
#endif
                        return (DecodeBitmap(warmPath), packageName);
                    }
                }

                if (cache.TryGetValue(packageName, out warm)
                    && warm.CheckedDate == today
                    && warm.IconExt == FailMarker)
                {
#if DEBUG
                    timing?.Mark("fail-marker cache hit (skip)");
#endif
                    return (null, packageName);
                }
            }
        }

        using var extractSession = new ApkIconExtractSession(device);
        CurrentExtractSession.Value = extractSession;
        try
        {
        // Manifest CRC listing in parallel with batch extract of AndroidManifest.xml + resources.arsc.
#if DEBUG
        timing?.Mark("parallel: zip listing(manifest) + batch extract manifest+arsc");
        var listingSw = Stopwatch.StartNew();
        var metaSw = Stopwatch.StartNew();
#endif
        var manifestListingTask = Task.Run(
            () =>
            {
                var listing = ArchiveListing.FetchZipMemberListing(deviceId, apkPath, [MANIFEST], cancellationToken);
#if DEBUG
                timing?.Mark($"FetchZipMemberListing(AndroidManifest.xml) finished in {listingSw.ElapsedMilliseconds} ms");
#endif
                return listing;
            },
            cancellationToken);

        await extractSession.EnsureMembersAsync(apkPath, [MANIFEST, RESOURCES], cancellationToken).ConfigureAwait(false);
#if DEBUG
        timing?.Mark($"batch manifest+arsc done in {metaSw.ElapsedMilliseconds} ms");
#endif

        await manifestListingTask.ConfigureAwait(false);
#if DEBUG
        timing?.Mark("parallel: zip listing + meta pull both complete");
#endif
        cancellationToken.ThrowIfCancellationRequested();

        var manifestEntry = FindEntry(manifestListingTask.Result, MANIFEST);
        if (manifestEntry is null || string.IsNullOrEmpty(manifestEntry.Value.Crc))
        {
            #if DEBUG
            timing?.Mark("manifest CRC missing — abort");
            #endif
            if (!string.IsNullOrEmpty(packageName))
                MarkFetchResult(serial, packageName, "", today, FailMarker, FailMarker);
            return (null, packageName);
        }

        var manifestCrc = NormalizeCrc(manifestEntry.Value.Crc);
        var manifestBytes = extractSession.TryGetCached(apkPath, MANIFEST);
        var resourcesBytes = extractSession.TryGetCached(apkPath, RESOURCES);
        if (manifestBytes is null || manifestBytes.Length == 0 || resourcesBytes is null || resourcesBytes.Length == 0)
        {
            #if DEBUG
            timing?.Mark($"meta pull empty (manifest={manifestBytes?.Length ?? 0}B, arsc={resourcesBytes?.Length ?? 0}B)");
            #endif
            if (!string.IsNullOrEmpty(packageName))
                MarkFetchResult(serial, packageName, manifestCrc, today, FailMarker, FailMarker);
            return (null, packageName);
        }

        #if DEBUG
        timing?.Mark($"parse package name + label (manifest={manifestBytes.Length}B, arsc={resourcesBytes.Length}B)");
        #endif
        packageName ??= TryReadPackageName(manifestBytes, resourcesBytes);
        if (string.IsNullOrEmpty(packageName))
        {
            #if DEBUG
            timing?.Mark("package name unresolved — abort");
            #endif
            return (null, null);
        }

        var label = TryReadPackageLabel(manifestBytes, resourcesBytes) ?? FailMarker;
        #if DEBUG
        timing?.Mark($"label={(label == FailMarker ? "fail" : "ok")}");
        #endif

        // Persist the label before icon work so a compose crash still leaves the display name.
        if (label != FailMarker)
            MarkFetchResult(serial, packageName, manifestCrc, today, iconExt: null, label);

        lock (GetDeviceLock(serial))
        {
            var cache = GetOrLoadCache(serial);
            if (
#if DEBUG
                timing is null &&
#endif
                cache.TryGetValue(packageName, out var existing)
                && string.Equals(existing.ManifestCrc, manifestCrc, StringComparison.OrdinalIgnoreCase)
                && IsSuccessfulIconExt(existing.IconExt))
            {
                var localPath = GetLocalIconPath(serial, packageName, existing.IconExt);
                if (File.Exists(localPath))
                {
                    // Append/replace only the current UI locale; keep other locales already in the CSV.
                    var updatedLabel = MergeLocaleLabel(existing.Label, label);
                    var updated = existing with
                    {
                        CheckedDate = today,
                        Label = updatedLabel,
                    };
                    if (existing.CheckedDate != today || updated.Label != existing.Label)
                    {
                        cache[packageName] = updated;
                        WriteCache(serial, cache);
                    }

#if DEBUG
                    timing?.Mark("CRC-matched local icon — skip re-extract");
#endif
                    return (DecodeBitmap(localPath), packageName);
                }
            }
#if DEBUG
            else if (timing is not null
                && cache.TryGetValue(packageName, out var crcHit)
                && string.Equals(crcHit.ManifestCrc, manifestCrc, StringComparison.OrdinalIgnoreCase)
                && IsSuccessfulIconExt(crcHit.IconExt)
                && File.Exists(GetLocalIconPath(serial, packageName, crcHit.IconExt)))
            {
                timing.Mark("CRC would match local icon — forcing re-extract for timing");
            }
#endif
        }

#if DEBUG
        timing?.Mark("ResolveIconCandidatesAsync start");
#endif
        var iconCandidates = await ResolveIconCandidatesAsync(
            device, apkPath, manifestBytes, resourcesBytes, cancellationToken
#if DEBUG
            , timing
#endif
            ).ConfigureAwait(false);
#if DEBUG
        timing?.Mark($"ResolveIconCandidatesAsync done ({iconCandidates.Count} candidates)");
#endif

        #if DEBUG
        timing?.Mark("DiscoverApkBundleFiles start");
        #endif
        var apkFiles = DiscoverApkBundleFiles(deviceId, apkPath);
        #if DEBUG
        timing?.Mark($"DiscoverApkBundleFiles done ({apkFiles.Count} apk(s))");
        #endif
        byte[] effectiveResources = resourcesBytes;

        if (iconCandidates.Count == 0 && apkFiles.Count > 1)
        {
            foreach (var splitApk in PreferApksForRead(apkFiles, apkPath).Where(p => !string.Equals(p, apkPath, StringComparison.Ordinal)))
            {
                cancellationToken.ThrowIfCancellationRequested();
#if DEBUG
                timing?.Mark($"PullResourcesOnlyAsync split: {Path.GetFileName(splitApk)}");
#endif
                var splitResources = await PullResourcesOnlyAsync(device, splitApk, cancellationToken
#if DEBUG
                    , timing
#endif
                    ).ConfigureAwait(false);
                if (splitResources is null || splitResources.Length == 0)
                    continue;

                #if DEBUG
                timing?.Mark($"ResolveIconCandidatesAsync on split {Path.GetFileName(splitApk)}");
                #endif
                var splitCandidates = await ResolveIconCandidatesAsync(
                    device, splitApk, manifestBytes, splitResources, cancellationToken
#if DEBUG
                    , timing
#endif
                    ).ConfigureAwait(false);
                if (splitCandidates.Count == 0)
                    continue;

                iconCandidates = splitCandidates;
                effectiveResources = splitResources;
                #if DEBUG
                timing?.Mark($"split candidates found ({iconCandidates.Count})");
                #endif
                break;
            }
        }

        if (iconCandidates.Count == 0)
        {
            // Splits exhausted (or single APK) — string-pool / heuristics.
            // Overlay / RRO arsc is often tiny or malformed; AlphaOmega ArscFile can throw
            // (Resolve already swallowed that). Keep fallbacks best-effort.
            #if DEBUG
            timing?.Mark("fallback string-pool / heuristic candidates");
            #endif
            try
            {
                iconCandidates = FindLikelyIconPathsInStringPool(new ArscFile(effectiveResources));
                if (iconCandidates.Count == 0)
                    iconCandidates = HeuristicIconCandidates();
            }
            catch (Exception e)
            {
#if DEBUG
                timing?.Mark($"fallback parse failed: {e.GetType().Name}: {e.Message}");
#else
                _ = e;
#endif
                iconCandidates = [];
            }

            #if DEBUG
            timing?.Mark($"fallback candidates: {iconCandidates.Count}");
            #endif
        }

        // Stock ic_launcher paths are last-resort only. Prepending them used to beat a confirmed
        // brand adaptive wrapper because both score as adaptive and
        // PickBestIconMember's stable sort keeps earlier list order — composing the leftover
        // Android Studio template instead of the real icon.
        if (!iconCandidates.Any(IsAdaptiveWrapperPath))
        {
            iconCandidates =
            [
                .. iconCandidates,
                "res/mipmap-anydpi-v26/ic_launcher.xml",
                "res/drawable-anydpi-v26/ic_launcher.xml",
                "res/mipmap-anydpi-v26/ic_launcher_round.xml",
                "res/drawable-anydpi-v26/ic_launcher_round.xml",
            ];
        }

        string? iconMember = null;
        var iconSourceApk = apkPath;
        if (iconCandidates.Count > 0)
        {
            if (iconCandidates.Count > MaxIconCandidatesToProbe)
                iconCandidates = iconCandidates.Take(MaxIconCandidatesToProbe).ToList();

            // Prefer base/density only; Prefetch stops once every candidate is found.
            #if DEBUG
            timing?.Mark($"Prefetch icon candidates ({iconCandidates.Count})");
            #endif
            await extractSession.PrefetchFromBundleAsync(apkFiles, iconCandidates, cancellationToken)
                .ConfigureAwait(false);

            foreach (var candidateApk in PreferApksForIconMember(apkFiles, apkPath))
            {
                iconMember = PickBestIconMember(
                    iconCandidates,
                    extractSession.PresentMembers(candidateApk, iconCandidates),
                    m => extractSession.TryGetCached(candidateApk, m)?.Length ?? 0);
                if (iconMember is not null)
                {
                    iconSourceApk = candidateApk;
                    #if DEBUG
                    timing?.Mark($"picked icon member: {iconMember} from {Path.GetFileName(candidateApk)}");
                    #endif
                    break;
                }
            }
        }

        if (iconMember is null)
        {
            foreach (var candidateApk in PreferApksForRead(apkFiles, apkPath))
            {
                #if DEBUG
                timing?.Mark($"DiscoverIconMembersOnDevice: {Path.GetFileName(candidateApk)}");
                #endif
                var discovered = DiscoverIconMembersOnDevice(deviceId, candidateApk, cancellationToken);
                if (discovered.Count == 0)
                    continue;

                // Keep adaptive XML — RankIconCandidates is raster-only and would drop wrappers.
                iconCandidates = RankDiscoveredIconCandidates(discovered.Select(e => e.Path));
                iconMember = PickBestIconMember(iconCandidates, discovered);
                if (iconMember is not null)
                {
                    iconSourceApk = candidateApk;
                    #if DEBUG
                    timing?.Mark($"discovered icon member: {iconMember}");
                    #endif
                    await extractSession.EnsureMembersAsync(candidateApk, [iconMember], cancellationToken)
                        .ConfigureAwait(false);
                    break;
                }
            }
        }

        if (iconMember is null)
        {
            #if DEBUG
            timing?.Mark("no icon member found — abort");
            #endif
            MarkFetchResult(serial, packageName, manifestCrc, today, FailMarker, label);
            return (null, packageName);
        }

        #if DEBUG
        timing?.Mark($"ReadFileAsStreamAsync icon: {iconMember}");
        #endif
        var memberBytes = extractSession.TryGetCached(iconSourceApk, iconMember);
        if (memberBytes is null || memberBytes.Length == 0)
        {
            await extractSession.EnsureMembersAsync(iconSourceApk, [iconMember], cancellationToken)
                .ConfigureAwait(false);
            memberBytes = extractSession.TryGetCached(iconSourceApk, iconMember);
        }

        if (memberBytes is null || memberBytes.Length == 0)
        {
            #if DEBUG
            timing?.Mark("icon bytes empty — abort");
            #endif
            MarkFetchResult(serial, packageName, manifestCrc, today, FailMarker, label);
            return (null, packageName);
        }

        cancellationToken.ThrowIfCancellationRequested();

        #if DEBUG
        timing?.Mark($"icon bytes ready ({memberBytes.Length}B, xml={iconMember.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)})");
        #endif

        BitmapSource? bitmap = null;
        string iconExt;
        var writeRawRaster = false;
        var resourcesForIcon = effectiveResources;

        if (iconMember.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        {
            SKColor? ResolveColor(int id)
            {
                var arsc = new ArscFile(resourcesForIcon);
                return TryGetResourceColor(arsc, resourcesForIcon, id);
            }

            // Prefer base arsc for layer refs (adaptive XML may be in a density split, vectors in base).
            var composeResources = resourcesBytes;
            var composeArsc = new ArscFile(composeResources);
            var apkBundle = PreferApksForRead(apkFiles, apkPath);

            Dictionary<int, byte[]>? xmlCache = null;
            async Task<Dictionary<int, byte[]>> GetXmlCacheAsync()
            {
                if (xmlCache is not null)
                    return xmlCache;

                #if DEBUG
                timing?.Mark("PreloadXmlResourcesAsync start");
                #endif
                xmlCache = await PreloadXmlResourcesAsync(
                    device, apkBundle, composeResources, memberBytes, cancellationToken).ConfigureAwait(false);
                #if DEBUG
                timing?.Mark($"PreloadXmlResourcesAsync done ({xmlCache.Count} entries)");
                #endif
                return xmlCache;
            }

            Func<int, byte[]?> ResolveXml = id =>
                xmlCache is not null && xmlCache.TryGetValue(id, out var bytes) ? bytes : null;

            if (ApkVectorIconRenderer.IsVectorDrawable(memberBytes))
            {
                #if DEBUG
                timing?.Mark("render vector drawable");
                #endif
                xmlCache = await PreloadXmlResourcesAsync(
                    device, apkBundle, resourcesForIcon, memberBytes, cancellationToken).ConfigureAwait(false);
                #if DEBUG
                timing?.Mark($"PreloadXmlResourcesAsync (vector) done ({xmlCache.Count})");
                #endif

                using var rendered = ApkVectorIconRenderer.TryRenderToSkBitmap(
                    memberBytes, size: 192, background: SKColors.Transparent,
                    resolveColor: ResolveColor, resolveXmlResource: ResolveXml);
                if (rendered is not null && IsStockAndroidStudioGreenPlate(rendered))
                {
                    // Green plate without Bugdroid — ship the complete template icon.
                    bitmap = DefaultAndroidPackageIcon.Render(192);
                    #if DEBUG
                    timing?.Mark("stock AS green plate → default package icon");
                    #endif
                }
                else if (rendered is not null && !IsDegenerateIcon(rendered))
                {
                    // Corner-biased vectors need recentering; full launcher vectors must keep placement.
                    if (IsCornerBiasedIcon(rendered))
                    {
                        using var centered = RecenterOpaqueContent(rendered);
                        bitmap = ApkVectorIconRenderer.ToBitmapSource(centered ?? rendered);
                    }
                    else
                    {
                        bitmap = ApkVectorIconRenderer.ToBitmapSource(rendered);
                    }
                    #if DEBUG
                    timing?.Mark("vector render ok");
                    #endif
                }
                else
                {
                    #if DEBUG
                    timing?.Mark("vector render failed / degenerate");
                    #endif
                }
            }
            else
            {
                await GetXmlCacheAsync().ConfigureAwait(false);
                #if DEBUG
                timing?.Mark("TryComposeAdaptiveIconAsync start");
                #endif
                bitmap = await TryComposeAdaptiveIconAsync(
                    device, apkBundle, memberBytes, composeArsc, composeResources, cancellationToken,
                    ResolveXml, packageName).ConfigureAwait(false);
                #if DEBUG
                timing?.Mark(bitmap is null ? "adaptive compose failed" : "adaptive compose ok");
                #endif

                // If adaptive composition fails, fall back to the best foreground raster
                // composited on white (Android adaptive icons are always opaque).
                if (bitmap is null)
                {
                    #if DEBUG
                    timing?.Mark("adaptive fg-raster fallback start");
                    #endif
                    var layers = ResolveAdaptiveLayers(memberBytes, composeArsc, composeResources);
                    var fgOnly = RankIconCandidates(layers.ForegroundImages);
                    if (fgOnly.Count == 0)
                    {
                        // Adaptive layers unresolved — use every
                        // density raster for the manifest icon id (PreferIconPaths would keep
                        // only the adaptive XML and RankIconCandidates would then drop it).
                        var iconRef = AxmlManifestReader.TryGetApplicationAttribute(manifestBytes, AxmlManifestReader.AttrIcon)
                                      ?? FindApplicationAttributeFromAxml(manifestBytes, "icon");
                        if (!string.IsNullOrWhiteSpace(iconRef))
                        {
                            fgOnly = RankIconCandidates(ResolveIconRefToAllPaths(iconRef, composeArsc, composeResources));

                            // Density-split arsc often owns the legacy PNG while base only
                            // lists the adaptive XML for the same icon id.
                            if (TryParseResourceId(iconRef, out var iconId))
                            {
                                foreach (var splitApk in PreferApksForIconMember(apkBundle, apkPath))
                                {
                                    if (Path.GetFileName(splitApk)
                                        .Equals("base.apk", StringComparison.OrdinalIgnoreCase))
                                        continue;

                                    var splitRes = await TryGetResourcesFromApkAsync(
                                        device, splitApk, cancellationToken).ConfigureAwait(false);
                                    if (splitRes is null || splitRes.Length == 0)
                                        continue;

                                    var splitPaths = RankIconCandidates(
                                        ArscResourceResolver.ResolvePaths(splitRes, iconId));
                                    if (splitPaths.Count == 0)
                                        continue;

                                    fgOnly = RankIconCandidates(fgOnly.Concat(splitPaths));
                                    #if DEBUG
                                    timing?.Mark($"fg-raster split paths: {splitPaths.Count}");
                                    #endif
                                    break;
                                }
                            }
                        }
                    }

                    if (fgOnly.Count > 0)
                    {
                        var fgProbe = fgOnly.Take(MaxIconCandidatesToProbe).ToList();
                        if (CurrentExtractSession.Value is { } fgSession)
                            await fgSession.PrefetchFromBundleAsync(apkBundle, fgProbe, cancellationToken)
                                .ConfigureAwait(false);

                        foreach (var candidateApk in PreferApksForIconMember(apkBundle, apkPath))
                        {
                            #if DEBUG
                            timing?.Mark($"fg batch in {Path.GetFileName(candidateApk)}");
                            #endif
                            string? fgMember;
                            if (CurrentExtractSession.Value is { } s)
                            {
                                fgMember = PickBestIconMember(
                                    fgProbe,
                                    s.PresentMembers(candidateApk, fgProbe),
                                    m => s.TryGetCached(candidateApk, m)?.Length ?? 0);
                            }
                            else
                            {
                                var fgListing = ArchiveListing.FetchZipMemberListing(
                                    deviceId, candidateApk, fgProbe, cancellationToken);
                                fgMember = PickBestIconMember(fgOnly, fgListing);
                            }

                            if (fgMember is null)
                                continue;

                            #if DEBUG
                            timing?.Mark($"read fg raster: {fgMember}");
                            #endif
                            var fgBytes = CurrentExtractSession.Value?.TryGetCached(candidateApk, fgMember);
                            if (fgBytes is null || fgBytes.Length == 0)
                            {
                                fgBytes = await ProbeApkMemberBytesAsync(
                                    device, candidateApk, fgMember, cancellationToken).ConfigureAwait(false);
                            }

                            if (fgBytes is null || fgBytes.Length == 0)
                                continue;

                            using var fgSk = DecodeSkBitmap(fgBytes);
                            if (fgSk is null)
                                continue;

                            // Prefer opaque white plate under transparent adaptive foregrounds
                            // over writing a raw transparent PNG.
                            var bgColor = layers.BackgroundColor ?? SKColors.White;
                            bitmap = CompositeOnOpaqueBackground(fgSk, 192, bgColor);
                            if (bitmap is not null)
                            {
                                #if DEBUG
                                timing?.Mark("fg-raster composite ok");
                                #endif
                                break;
                            }

                            memberBytes = fgBytes;
                            iconMember = fgMember;
                            writeRawRaster = true;
                            #if DEBUG
                            timing?.Mark("fg-raster write-raw fallback");
                            #endif
                            break;
                        }
                    }
                    else
                    {
                        #if DEBUG
                        timing?.Mark("no fg-raster candidates");
                        #endif
                    }
                }
            }

            // Density-split apps: adaptive fg is often a layer-list whose rasters live only in a split.
            if (bitmap is null && !writeRawRaster)
            {
                // RankIconCandidates is xxxhdpi-first; Take(N) alone never reaches xxhdpi-only packs.
                const int heuristicProbe = 48;
                var heuristic = HeuristicIconProbeCandidates(heuristicProbe);
                #if DEBUG
                timing?.Mark($"heuristic probe ({heuristic.Count} paths)");
                #endif
                if (CurrentExtractSession.Value is { } heurSession)
                    await heurSession.PrefetchFromBundleAsync(apkBundle, heuristic, cancellationToken)
                        .ConfigureAwait(false);

                foreach (var candidateApk in PreferApksForIconMember(apkBundle, apkPath))
                {
                    string? member;
                    if (CurrentExtractSession.Value is { } s)
                    {
                        member = PickBestIconMember(
                            heuristic,
                            s.PresentMembers(candidateApk, heuristic),
                            m => s.TryGetCached(candidateApk, m)?.Length ?? 0);
                    }
                    else
                    {
                        var listing = ArchiveListing.FetchZipMemberListing(
                            deviceId, candidateApk, heuristic, cancellationToken);
                        member = PickBestIconMember(heuristic, listing);
                    }

                    if (member is null)
                        continue;

                    #if DEBUG
                    timing?.Mark($"heuristic hit: {member} in {Path.GetFileName(candidateApk)}");
                    #endif
                    var bytes = CurrentExtractSession.Value?.TryGetCached(candidateApk, member);
                    if (bytes is null || bytes.Length == 0)
                    {
                        bytes = await ProbeApkMemberBytesAsync(
                            device, candidateApk, member, cancellationToken).ConfigureAwait(false);
                    }

                    if (bytes is null || bytes.Length == 0)
                        continue;

                    using var sk = DecodeSkBitmap(bytes);
                    if (sk is null || IsDegenerateIcon(sk))
                        continue;

                    bitmap = CompositeOnOpaqueBackground(sk, 192, SKColors.White);
                    if (bitmap is not null)
                    {
                        #if DEBUG
                        timing?.Mark("heuristic composite ok");
                        #endif
                        break;
                    }

                    memberBytes = bytes;
                    iconMember = member;
                    writeRawRaster = true;
                    #if DEBUG
                    timing?.Mark("heuristic write-raw fallback");
                    #endif
                    break;
                }
            }


            if (bitmap is null && !writeRawRaster)
            {
                #if DEBUG
                timing?.Mark("XML icon path exhausted — abort");
                #endif
                MarkFetchResult(serial, packageName, manifestCrc, today, FailMarker, label);
                return (null, packageName);
            }

            if (writeRawRaster)
            {
                iconExt = Path.GetExtension(iconMember);
                if (string.IsNullOrEmpty(iconExt))
                    iconExt = DetectRasterExtension(memberBytes) ?? ".png";
            }
            else
            {
                iconExt = ".png";
            }
        }
        else
        {
            iconExt = Path.GetExtension(iconMember);
            if (string.IsNullOrEmpty(iconExt))
                iconExt = DetectRasterExtension(memberBytes) ?? ".png";
            writeRawRaster = true;
            #if DEBUG
            timing?.Mark($"raw raster path ({iconExt})");
            #endif
        }

        // Extensionless WebP (WebView res/9M) must not be saved as .png — WIC then decodes
        // VP8L without alpha and fills transparent corners with opaque black.
        if (writeRawRaster
            && iconExt.Equals(".png", StringComparison.OrdinalIgnoreCase)
            && DetectRasterExtension(memberBytes) is { } detected
            && !detected.Equals(".png", StringComparison.OrdinalIgnoreCase))
        {
            iconExt = detected;
            #if DEBUG
            timing?.Mark($"corrected raster ext → {iconExt}");
            #endif
        }

        #if DEBUG
        timing?.Mark("save local icon file");
        #endif
        var localDir = GetLocalIconDirectory(serial);
        Directory.CreateDirectory(localDir);
        var localFile = GetLocalIconPath(serial, packageName, iconExt);

        if (bitmap is not null && !writeRawRaster)
        {
            await SaveBitmapAsPngAsync(bitmap, localFile, cancellationToken).ConfigureAwait(false);
            #if DEBUG
            timing?.Mark("SaveBitmapAsPngAsync done");
            #endif
        }
        else
        {
            // Upscale tiny system rasters so list thumbnails are not muddy.
            using (var rawSk = DecodeSkBitmap(memberBytes))
            {
                if (rawSk is not null && (rawSk.Width < 128 || rawSk.Height < 128))
                {
                    var upscaled = UpscaleSkBitmap(rawSk, 192);
                    if (upscaled is not null)
                    {
                        try
                        {
                            bitmap = ApkVectorIconRenderer.ToBitmapSource(upscaled);
                            await SaveBitmapAsPngAsync(bitmap, localFile, cancellationToken).ConfigureAwait(false);
                            MarkFetchResult(serial, packageName, manifestCrc, today, ".png", label);
                            #if DEBUG
                            timing?.Mark("upscaled tiny raster saved");
                            #endif
                            return (bitmap, packageName);
                        }
                        finally
                        {
                            upscaled.Dispose();
                        }
                    }
                }
            }

            await using (var fs = new FileStream(localFile, FileMode.Create, FileAccess.Write, FileShare.Read))
                await fs.WriteAsync(memberBytes, cancellationToken).ConfigureAwait(false);

            if (!File.Exists(localFile) || new FileInfo(localFile).Length == 0)
            {
                try { File.Delete(localFile); } catch { /* ignore */ }
                #if DEBUG
                timing?.Mark("local file write failed — abort");
                #endif
                MarkFetchResult(serial, packageName, manifestCrc, today, FailMarker, label);
                return (null, packageName);
            }

            bitmap = DecodeBitmap(localFile);
            if (bitmap is null)
            {
                try { File.Delete(localFile); } catch { /* ignore */ }
                #if DEBUG
                timing?.Mark("DecodeBitmap failed — abort");
                #endif
                MarkFetchResult(serial, packageName, manifestCrc, today, FailMarker, label);
                return (null, packageName);
            }

            // Reject near-solid white / empty rasters (adaptive fg-only leftovers, etc.).
            using (var sk = DecodeSkBitmap(memberBytes))
            {
                if (sk is not null && IsDegenerateIcon(sk))
                {
                    try { File.Delete(localFile); } catch { /* ignore */ }
                    #if DEBUG
                    timing?.Mark("degenerate icon rejected — abort");
                    #endif
                    MarkFetchResult(serial, packageName, manifestCrc, today, FailMarker, label);
                    return (null, packageName);
                }
            }

            #if DEBUG
            timing?.Mark("raw raster saved + decoded");
            #endif
        }

        MarkFetchResult(serial, packageName, manifestCrc, today, iconExt, label);
        #if DEBUG
        timing?.Mark("MarkFetchResult / cache write done");
        #endif
        return (bitmap, packageName);
        }
        finally
        {
            CurrentExtractSession.Value = null;
        }
    }

    /// <param name="iconExt">New icon ext, <see cref="FailMarker"/>, or null to leave the existing icon field unchanged.</param>
    /// <param name="label">New label, <see cref="FailMarker"/>, or null to leave the existing label unchanged.</param>
    private static void MarkFetchResult(
        string serial,
        string packageName,
        string manifestCrc,
        DateOnly today,
        string? iconExt,
        string? label)
    {
        lock (GetDeviceLock(serial))
        {
            var cache = GetOrLoadCache(serial);
            cache.TryGetValue(packageName, out var existing);

            var nextIcon = iconExt ?? existing.IconExt ?? "";
            var nextLabel = label is null
                ? existing.Label
                : MergeLocaleLabel(existing.Label == FailMarker ? null : existing.Label, label);
            var nextCrc = string.IsNullOrEmpty(manifestCrc) ? (existing.ManifestCrc ?? "") : manifestCrc;

            // Drop obsolete local files when the extension changes.
            if (IsSuccessfulIconExt(existing.IconExt)
                && IsSuccessfulIconExt(nextIcon)
                && !string.Equals(existing.IconExt, nextIcon, StringComparison.OrdinalIgnoreCase))
            {
                var oldPath = GetLocalIconPath(serial, packageName, existing.IconExt);
                try { if (File.Exists(oldPath)) File.Delete(oldPath); } catch { /* ignore */ }
            }

            cache[packageName] = new ApkIconCacheEntry(nextCrc, today, nextIcon, nextLabel);
            WriteCache(serial, cache);
        }
    }

    private static async Task<(byte[]? Manifest, byte[]? Resources)> PullManifestAndResourcesAsync(
        LogicalDeviceViewModel device,
        string apkPath,
        CancellationToken cancellationToken
#if DEBUG
        , ApkLoadTiming? timing = null
        , Stopwatch? outerSw = null
#endif
        )
    {
        string? stagingRoot = null;
        try
        {
#if DEBUG
            timing?.Mark("ExtractZipMembersToStaging(manifest+arsc) start");
            var extractSw = Stopwatch.StartNew();
#endif
            var (root, contentRoot) = await Task.Run(
                () => ArchiveExtract.ExtractZipMembersToStaging(
                    device.ID, apkPath, [MANIFEST, RESOURCES], cancellationToken),
                cancellationToken).ConfigureAwait(false);
            stagingRoot = root;
#if DEBUG
            timing?.Mark($"ExtractZipMembersToStaging done in {extractSw.ElapsedMilliseconds} ms");

            timing?.Mark("ReadFileAsStreamAsync manifest + resources start");
            var readSw = Stopwatch.StartNew();
#endif
            var manifestTask = AdbHelper.ReadFileAsStreamAsync(
                device, FileHelper.ConcatPaths(contentRoot, MANIFEST), cancellationToken);
            var resourcesTask = AdbHelper.ReadFileAsStreamAsync(
                device, FileHelper.ConcatPaths(contentRoot, RESOURCES), cancellationToken);

            await Task.WhenAll(manifestTask, resourcesTask).ConfigureAwait(false);
#if DEBUG
            timing?.Mark($"ReadFileAsStreamAsync manifest+resources done in {readSw.ElapsedMilliseconds} ms"
                + (outerSw is null ? "" : $" (meta wall {outerSw.ElapsedMilliseconds} ms)"));
#endif

            return (ToByteArray(manifestTask.Result), ToByteArray(resourcesTask.Result));
        }
        catch (OperationCanceledException)
        {
#if DEBUG
            timing?.Mark("PullManifestAndResources cancelled");
#endif
            throw;
        }
        catch (Exception e)
        {
#if DEBUG
            timing?.Mark($"PullManifestAndResources failed: {e.Message}");
#endif
#if !DEPLOY
            DebugLog.PrintLine($"APK meta pull failed for {apkPath}: {e.Message}");
#endif
            return (null, null);
        }
        finally
        {
            if (stagingRoot is not null)
            {
#if DEBUG
                timing?.Mark("CleanupStaging start");
#endif
                ArchiveExtract.CleanupStaging(device.ID, stagingRoot, CancellationToken.None);
#if DEBUG
                timing?.Mark("CleanupStaging done");
#endif
            }
        }
    }

    /// <summary>
    /// Sibling APKs in an app install dir (<c>base.apk</c> + <c>split_*.apk</c>).
    /// Shared folders such as <c>/product/overlay</c> hold many unrelated RROs — do not
    /// treat them as density splits (that previously scanned dozens of APKs per icon load).
    /// </summary>
    private static List<string> DiscoverApkBundleFiles(string deviceId, string apkPath)
    {
        var result = new List<string> { apkPath };
        try
        {
            var parent = FileHelper.GetParentPath(apkPath);
            if (string.IsNullOrEmpty(parent))
                return result;

            var siblings = new List<(string Name, string Full)>();
            foreach (var entry in ADBService.ListDirectoryEntries(deviceId, parent, CancellationToken.None))
            {
                if (entry.Type is not AbstractFile.FileType.File)
                    continue;

                var name = entry.FullName ?? "";
                if (!name.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
                    continue;

                var full = string.IsNullOrEmpty(entry.FullPath)
                    ? FileHelper.ConcatPaths(parent, name)
                    : entry.FullPath;
                siblings.Add((name, full));
            }

            // Play/system install dirs always include base.apk. Standalone APKs in shared
            // dirs (overlays, priv-app dumps) use distinct filenames and must stay alone.
            var selfName = Path.GetFileName(apkPath);
            var hasBase = selfName.Equals("base.apk", StringComparison.OrdinalIgnoreCase)
                || siblings.Any(s => s.Name.Equals("base.apk", StringComparison.OrdinalIgnoreCase));
            if (!hasBase)
                return result;

            foreach (var (name, full) in siblings)
            {
                if (!name.Equals("base.apk", StringComparison.OrdinalIgnoreCase)
                    && !name.StartsWith("split_", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (result.Any(p => string.Equals(p, full, StringComparison.Ordinal)))
                    continue;

                result.Add(full);
            }
        }
        catch
        {
            // Keep the single base path.
        }

        return result;
    }

    /// <summary>
    /// One device staging folder for an entire package icon load. All APK unzips go into the
    /// same root (no per-APK mkdir, no mid-load cleanup). Dispose removes that single folder.
    /// </summary>
    private sealed class ApkIconExtractSession(LogicalDeviceViewModel device) : IDisposable
    {
        private string? _stagingRoot;
        private readonly Dictionary<(string Apk, string Member), byte[]> _cache = new();
        private readonly HashSet<(string Apk, string Member)> _absent = new();
        /// <summary>Members present under staging from any APK (shared tree).</summary>
        private readonly HashSet<string> _onDeviceMembers = new(StringComparer.OrdinalIgnoreCase);
        private bool _disposed;

        public byte[]? TryGetCached(string apkPath, string member)
        {
            member = ArchivePath.NormalizeInternal(member);
            if (_cache.TryGetValue((apkPath, member), out var bytes))
                return bytes;

            // Shared staging reuses identical drawable paths across density splits, but
            // resources.arsc / AndroidManifest.xml differ per APK — never cross-read those.
            if (IsPerApkExclusiveMember(member))
                return null;

            // Shared staging: another APK may have supplied this path already.
            foreach (var kv in _cache)
            {
                if (string.Equals(kv.Key.Member, member, StringComparison.OrdinalIgnoreCase))
                    return kv.Value;
            }

            return null;
        }

        public bool HasMemberAnywhere(string member)
        {
            member = ArchivePath.NormalizeInternal(member);
            if (_onDeviceMembers.Contains(member))
                return true;
            foreach (var key in _cache.Keys)
            {
                if (string.Equals(key.Member, member, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        public IReadOnlyList<string> PresentMembers(string apkPath, IEnumerable<string> candidates)
        {
            List<string> found = [];
            foreach (var raw in candidates)
            {
                var member = ArchivePath.NormalizeInternal(raw);
                // Require cached bytes — _onDeviceMembers alone can list staging leftovers
                // that failed to sync, which made heuristic picks unreadable.
                if (TryGetCached(apkPath, member) is { Length: > 0 })
                    found.Add(member);
            }

            return found;
        }

        private static bool IsPerApkExclusiveMember(string member)
            => member.Equals(RESOURCES, StringComparison.OrdinalIgnoreCase)
               || member.Equals(MANIFEST, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// One <c>unzip -o -d staging</c> of pending suspects from <paramref name="apkPath"/> into
        /// the shared package staging root. Missing members are tolerated.
        /// </summary>
        public async Task EnsureMembersAsync(
            string apkPath,
            IReadOnlyList<string> members,
            CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var pending = members
                .Select(ArchivePath.NormalizeInternal)
                .Where(static m => !string.IsNullOrEmpty(m))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(m => !_cache.ContainsKey((apkPath, m))
                            && !_absent.Contains((apkPath, m))
                            // resources.arsc is per-APK; do not skip because base already staged one.
                            && (IsPerApkExclusiveMember(m) || !_onDeviceMembers.Contains(m)))
                .ToList();

            if (pending.Count == 0)
                return;

            #if DEBUG
            MarkLoadStep(
                $"batch EnsureMembers ({pending.Count}) from {Path.GetFileName(apkPath)}: {string.Join(',', pending)}");
            #endif

            var stagingRoot = EnsureStagingRoot(cancellationToken);
            await Task.Run(
                () => ArchiveExtract.ExtractZipMembersInto(
                    device.ID,
                    apkPath,
                    stagingRoot,
                    pending,
                    cancellationToken,
                    allowMissingMembers: true),
                cancellationToken).ConfigureAwait(false);

            // One find after unzip — avoid N failed sync pulls for missing members.
            var onDevice = ListRelativeFilesUnder(device.ID, stagingRoot, cancellationToken);
            foreach (var path in onDevice)
                _onDeviceMembers.Add(path);

            foreach (var member in pending)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!onDevice.Contains(member))
                {
                    _absent.Add((apkPath, member));
                    continue;
                }

                var devicePath = FileHelper.ConcatPaths(stagingRoot, member);
                await using var stream = await AdbHelper.ReadFileAsStreamAsync(
                    device, devicePath, cancellationToken).ConfigureAwait(false);
                var bytes = ToByteArray(stream);
                if (bytes is { Length: > 0 })
                    _cache[(apkPath, member)] = bytes;
                else
                    _absent.Add((apkPath, member));
            }
        }

        public async Task PrefetchFromBundleAsync(
            IReadOnlyList<string> apkFiles,
            IReadOnlyList<string> members,
            CancellationToken cancellationToken)
        {
            if (members.Count == 0 || apkFiles.Count == 0)
                return;

            var needed = members
                .Select(ArchivePath.NormalizeInternal)
                .Where(static m => !string.IsNullOrEmpty(m))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(m => !HasMemberAnywhere(m))
                .ToList();

            if (needed.Count == 0)
                return;

            foreach (var apk in PreferApksForIconMember(apkFiles))
            {
                await EnsureMembersAsync(apk, needed, cancellationToken).ConfigureAwait(false);
                needed = needed.Where(m => !HasMemberAnywhere(m)).ToList();
                if (needed.Count == 0)
                    break;
            }
        }

        public async Task<byte[]?> TryGetFromBundleAsync(
            IReadOnlyList<string> apkFiles,
            string member,
            CancellationToken cancellationToken)
        {
            member = ArchivePath.NormalizeInternal(member);
            if (string.IsNullOrEmpty(member) || apkFiles.Count == 0)
                return null;

            // TryGetCached falls back across APKs; any path works for the probe key.
            var existing = TryGetCached(apkFiles[0], member);
            if (existing is not null)
                return existing;

            await PrefetchFromBundleAsync(apkFiles, [member], cancellationToken).ConfigureAwait(false);
            return TryGetCached(apkFiles[0], member);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            if (_stagingRoot is not null)
            {
                #if DEBUG
                MarkLoadStep($"ApkIconExtractSession dispose: {_stagingRoot}");
                #endif
                try { ArchiveExtract.CleanupStaging(device.ID, _stagingRoot, CancellationToken.None); }
                catch { /* best-effort */ }
                _stagingRoot = null;
            }

            _cache.Clear();
            _absent.Clear();
            _onDeviceMembers.Clear();
        }

        private string EnsureStagingRoot(CancellationToken cancellationToken)
        {
            if (_stagingRoot is not null)
                return _stagingRoot;

            _stagingRoot = ArchiveExtract.CreateStagingRoot(device.ID, cancellationToken);
            // Single mkdir for the package; unzip -d writes members under this root.
            ShellFileOperation.MakeDirs(device.ID, [_stagingRoot]).GetAwaiter().GetResult();
            #if DEBUG
            MarkLoadStep($"package staging created: {_stagingRoot}");
            #endif
            return _stagingRoot;
        }

        private static HashSet<string> ListRelativeFilesUnder(
            string deviceId,
            string stagingRoot,
            CancellationToken cancellationToken)
        {
            var find = ShellCommands.TranslateCommand("find");
            _ = ADBService.ExecuteDeviceAdbShellCommand(
                deviceId,
                find,
                out var stdout,
                out _,
                cancellationToken,
                ADBService.EscapeAdbShellString(stagingRoot),
                "-type",
                "f");

            var prefix = stagingRoot.TrimEnd('/') + "/";
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in stdout.Split(ADBService.LINE_SEPARATORS, StringSplitOptions.RemoveEmptyEntries))
            {
                var path = line.Trim();
                if (path.StartsWith(prefix, StringComparison.Ordinal))
                    result.Add(path[prefix.Length..]);
            }

            #if DEBUG
            MarkLoadStep($"staging find under {stagingRoot}: {result.Count} file(s)");
            #endif
            return result;
        }
    }

    /// <summary>
    /// Order APKs for icon/resource reads: density splits, then <c>base.apk</c>, then
    /// other resource-ish configs. ABI / language / feature modules are last.
    /// </summary>
    private static List<string> PreferApksForRead(IReadOnlyList<string> apkFiles, string baseApk)
    {
        return apkFiles
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(IconApkRank)
            .ThenBy(p => string.Equals(p, baseApk, StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(p => p, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Density splits + base only (base first). Feature / ABI / language modules are excluded.
    /// </summary>
    private static List<string> PreferApksForIconMember(IReadOnlyList<string> apkFiles, string? baseApk = null)
    {
        baseApk ??= apkFiles.FirstOrDefault(static p =>
            Path.GetFileName(p).Equals("base.apk", StringComparison.OrdinalIgnoreCase));

        var preferred = PreferApksForRead(apkFiles, baseApk ?? apkFiles[0])
            .Where(static p => IconApkRank(p) >= 5)
            .ToList();

        if (preferred.Count == 0)
            preferred = PreferApksForRead(apkFiles, baseApk ?? apkFiles[0]);

        // Base first — adaptive XML / vectors almost always live there.
        return
        [
            .. preferred.Where(p => Path.GetFileName(p).Equals("base.apk", StringComparison.OrdinalIgnoreCase)
                || string.Equals(p, baseApk, StringComparison.Ordinal)),
            .. preferred.Where(p => !Path.GetFileName(p).Equals("base.apk", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(p, baseApk, StringComparison.Ordinal)),
        ];
    }

    private const int IconApkRankBase = 35;

    private static int IconApkRank(string apkPath)
    {
        var name = Path.GetFileName(apkPath).ToLowerInvariant();

        if (name.Contains("xxxhdpi", StringComparison.Ordinal)) return 50;
        if (name.Contains("xxhdpi", StringComparison.Ordinal)) return 40;
        if (name.Contains("xhdpi", StringComparison.Ordinal)) return 30;
        if (name.Contains("tvdpi", StringComparison.Ordinal)) return 18;
        if (name.Contains("hdpi", StringComparison.Ordinal)) return 20;
        if (name.Contains("mdpi", StringComparison.Ordinal)) return 10;
        if (name.Contains("ldpi", StringComparison.Ordinal)) return 5;

        if (name.Equals("base.apk", StringComparison.OrdinalIgnoreCase)
            || (!name.Contains("split", StringComparison.Ordinal)
                && name.EndsWith(".apk", StringComparison.Ordinal)))
            return IconApkRankBase;

        if (name.Contains("arm64", StringComparison.Ordinal)
            || name.Contains("armeabi", StringComparison.Ordinal)
            || name.Contains("x86_64", StringComparison.Ordinal)
            || name.Contains("x86", StringComparison.Ordinal))
            return -40;

        if (IsLanguageConfigSplitName(name))
            return -30;

        // Feature modules (split_OCRCoreDF.apk, split_FASOpenCVDF.apk, …) — never launcher icons.
        if (name.StartsWith("split_", StringComparison.Ordinal))
            return -20;

        if (name.Contains("config.", StringComparison.Ordinal))
            return 0;

        return 1;
    }

    private static bool IsLanguageConfigSplitName(string lowerFileName)
    {
        var marker = ".config.";
        var idx = lowerFileName.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0)
        {
            marker = "split_config.";
            idx = lowerFileName.IndexOf(marker, StringComparison.Ordinal);
            if (idx < 0)
                return false;
        }

        var locale = lowerFileName[(idx + marker.Length)..];
        if (locale.EndsWith(".apk", StringComparison.Ordinal))
            locale = locale[..^4];

        if (locale.Length is < 2 or > 12)
            return false;

        if (locale.Contains("dpi", StringComparison.Ordinal)
            || locale.Contains("arm", StringComparison.Ordinal)
            || locale.Contains("x86", StringComparison.Ordinal))
            return false;

        for (var i = 0; i < locale.Length; i++)
        {
            var c = locale[i];
            if (c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '+' or '_')
                continue;
            return false;
        }

        return true;
    }

    private static async Task<byte[]?> ReadMemberFromBundleAsync(
        LogicalDeviceViewModel device,
        IReadOnlyList<string> apkFiles,
        string member,
        CancellationToken cancellationToken)
    {
        _ = device;
        member = ArchivePath.NormalizeInternal(member);
        if (string.IsNullOrEmpty(member) || apkFiles.Count == 0)
            return null;

        if (CurrentExtractSession.Value is { } session)
            return await session.TryGetFromBundleAsync(apkFiles, member, cancellationToken).ConfigureAwait(false);

        using var fallback = new ApkIconExtractSession(device);
        return await fallback.TryGetFromBundleAsync(apkFiles, member, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads <c>resources.arsc</c> from an APK via the active extract session when possible.
    /// </summary>
    private static async Task<byte[]?> TryGetResourcesFromApkAsync(
        LogicalDeviceViewModel device,
        string apkPath,
        CancellationToken cancellationToken)
    {
        if (CurrentExtractSession.Value is { } session)
        {
            var cached = session.TryGetCached(apkPath, RESOURCES);
            if (cached is { Length: > 0 })
                return cached;

            await session.EnsureMembersAsync(apkPath, [RESOURCES], cancellationToken).ConfigureAwait(false);
            return session.TryGetCached(apkPath, RESOURCES);
        }

        return await PullResourcesOnlyAsync(device, apkPath, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Drawable paths for a resource id, consulting density-split <c>resources.arsc</c> when the
    /// base table has only a typeSpec (density split owns the file path).
    /// </summary>
    private static async Task<List<string>> ResolveDrawableFilePathsAcrossBundleAsync(
        LogicalDeviceViewModel device,
        IReadOnlyList<string> apkFiles,
        byte[] baseResources,
        int resourceId,
        CancellationToken cancellationToken)
    {
        var paths = ArscResourceResolver.ResolvePaths(baseResources, resourceId)
            .Select(ArchivePath.NormalizeInternal)
            .Where(static p => !string.IsNullOrWhiteSpace(p) && !IsColorResourcePath(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (paths.Count > 0)
            return PreferHighestDensityOnly(paths);

        // Native table empty (INVALID TYPE CONFIG) — density splits own the file. Do not trust
        // AlphaOmega ResourceMap alone; it often invents pool strings for missing configs.
        foreach (var apk in PreferApksForIconMember(apkFiles))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Path.GetFileName(apk).Equals("base.apk", StringComparison.OrdinalIgnoreCase))
                continue;

            var splitRes = await TryGetResourcesFromApkAsync(device, apk, cancellationToken).ConfigureAwait(false);
            if (splitRes is null || splitRes.Length == 0)
                continue;

            paths = ArscResourceResolver.ResolvePaths(splitRes, resourceId)
                .Select(ArchivePath.NormalizeInternal)
                .Where(static p => !string.IsNullOrWhiteSpace(p) && !IsColorResourcePath(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (paths.Count > 0)
                return PreferHighestDensityOnly(paths);
        }

        return [];
    }

    private static async Task<byte[]?> PullResourcesOnlyAsync(
        LogicalDeviceViewModel device,
        string apkPath,
        CancellationToken cancellationToken
#if DEBUG
        , ApkLoadTiming? timing = null
#endif
        )
    {
        string? stagingRoot = null;
        try
        {
#if DEBUG
            timing?.Mark($"ExtractZipMembersToStaging(resources) {Path.GetFileName(apkPath)}");
#endif
            var (root, contentRoot) = await Task.Run(
                () => ArchiveExtract.ExtractZipMembersToStaging(
                    device.ID, apkPath, [RESOURCES], cancellationToken),
                cancellationToken).ConfigureAwait(false);
            stagingRoot = root;

            await using var stream = await AdbHelper.ReadFileAsStreamAsync(
                device, FileHelper.ConcatPaths(contentRoot, RESOURCES), cancellationToken).ConfigureAwait(false);
            var bytes = ToByteArray(stream);
#if DEBUG
            timing?.Mark($"PullResourcesOnlyAsync done ({bytes?.Length ?? 0}B)");
#endif
            return bytes;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
#if DEBUG
            timing?.Mark("PullResourcesOnlyAsync failed");
#endif
            return null;
        }
        finally
        {
            if (stagingRoot is not null)
                ArchiveExtract.CleanupStaging(device.ID, stagingRoot, CancellationToken.None);
        }
    }

    private static async Task<Dictionary<int, byte[]>> PreloadXmlResourcesAsync(
        LogicalDeviceViewModel device,
        IReadOnlyList<string> apkFiles,
        byte[] resourcesBytes,
        byte[] vectorOrAdaptiveBytes,
        CancellationToken cancellationToken)
    {
        var cache = new Dictionary<int, byte[]>();
        foreach (var id in ApkVectorIconRenderer.CollectFillResourceIds(vectorOrAdaptiveBytes))
        {
            if (TryGetResourceColor(new ArscFile(resourcesBytes), resourcesBytes, id) is not null)
                continue;

            foreach (var path in ArscResourceResolver.ResolvePaths(resourcesBytes, id))
            {
                if (!path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    continue;

                var bytes = await ReadMemberFromBundleAsync(device, apkFiles, path, cancellationToken).ConfigureAwait(false);
                if (bytes is { Length: > 0 })
                {
                    cache[id] = bytes;
                    break;
                }
            }
        }

        return cache;
    }

    private static byte[]? ToByteArray(MemoryStream? stream)
    {
        if (stream is null || stream.Length == 0)
            return null;

        stream.Position = 0;
        return stream.ToArray();
    }

    /// <summary>
    /// Read one zip member via the package extract session (shared staging), never via
    /// <see cref="ArchiveExtract.ExtractSelectionForPull"/> (mkdir/mv/cleanup).
    /// </summary>
    private static async Task<byte[]?> ProbeApkMemberBytesAsync(
        LogicalDeviceViewModel device,
        string apkPath,
        string member,
        CancellationToken cancellationToken)
    {
        member = ArchivePath.NormalizeInternal(member);
        if (string.IsNullOrEmpty(member))
            return null;

        if (CurrentExtractSession.Value is { } session)
        {
            await session.EnsureMembersAsync(apkPath, [member], cancellationToken).ConfigureAwait(false);
            return session.TryGetCached(apkPath, member);
        }

        using var fallback = new ApkIconExtractSession(device);
        await fallback.EnsureMembersAsync(apkPath, [member], cancellationToken).ConfigureAwait(false);
        return fallback.TryGetCached(apkPath, member);
    }

    private static async Task<List<string>> ResolveIconCandidatesAsync(
        LogicalDeviceViewModel device,
        string apkPath,
        byte[] manifestBytes,
        byte[] resourcesBytes,
        CancellationToken cancellationToken
#if DEBUG
        , ApkLoadTiming? timing = null
#endif
        )
    {
        try
        {
            var arsc = new ArscFile(resourcesBytes);
            using var manifestStream = new MemoryStream(manifestBytes, writable: false);
            using var axml = new AxmlFile(new StreamLoader(manifestStream));

            // Prefer walking AXML attributes — ApkApplication.Icon throws on "@7F…" refs.
            var iconRef = AxmlManifestReader.TryGetApplicationAttribute(manifestBytes, AxmlManifestReader.AttrIcon)
                ?? FindApplicationAttribute(axml.RootNode, "icon")
                ?? TryGetTypedApplicationIcon(axml, arsc);

            if (string.IsNullOrEmpty(iconRef))
            {
                #if DEBUG
                timing?.Mark("ResolveIconCandidates: no icon ref → string-pool");
                #endif
                return FindLikelyIconPathsInStringPool(arsc);
            }

            #if DEBUG
            timing?.Mark($"ResolveIconCandidates: iconRef={iconRef}");
            #endif

            // Resolve the manifest icon id only — do not fall back to string-pool or brand
            // guesses here. Density-split APKs often own the real adaptive wrapper while the
            // base arsc lists the id as INVALID. Broad key-hint fallbacks previously
            // matched chrome glyphs and returned notification dots before splits ran.
            var paths = ResolveIconRefToPathsStrict(iconRef, arsc, resourcesBytes);
            if (paths.Count == 0)
            {
                #if DEBUG
                timing?.Mark("ResolveIconCandidates: strict resolve empty");
                #endif
                return [];
            }

            #if DEBUG
            timing?.Mark($"ResolveIconCandidates: {paths.Count} strict paths");
            #endif

            // Adaptive wrappers (anydpi / *launcher* XML) before density rasters.
            var adaptivePreferred = paths
                .Where(IsAdaptiveWrapperPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var xmlMember in adaptivePreferred)
            {
                cancellationToken.ThrowIfCancellationRequested();
                #if DEBUG
                timing?.Mark($"probe adaptive wrapper: {xmlMember}");
                #endif
                var xmlBytes = await ProbeApkMemberBytesAsync(device, apkPath, xmlMember, cancellationToken)
                    .ConfigureAwait(false);
                if (xmlBytes is null || xmlBytes.Length == 0)
                    continue;

                if (ApkVectorIconRenderer.IsAdaptiveIcon(xmlBytes))
                {
                    #if DEBUG
                    timing?.Mark($"adaptive wrapper confirmed: {xmlMember}");
                    #endif
                    // Always compose the adaptive wrapper. Layer names like
                    // ic_launcher_background can still hold real product art
                    // (custom plates); same-named mipmap rasters are often social badges.
                    return [xmlMember];
                }
            }

            // Prefer pre-rendered density rasters over distorting vectors.
            // Skip bare *_background layers (themed alternate plates).
            var rasters = RankIconCandidates(paths.Where(p =>
                !p.Contains("_background.", StringComparison.OrdinalIgnoreCase)
                && !p.Contains("_background_", StringComparison.OrdinalIgnoreCase)));
            if (rasters.Count > 0)
            {
                #if DEBUG
                timing?.Mark($"ResolveIconCandidates: {rasters.Count} ranked rasters");
                #endif
                return rasters;
            }

            // Any remaining XML from the icon ref (including obfuscated names like res/qq.xml).
            var xmlMembers = paths
                .Where(p => p.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var xmlMember in xmlMembers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                #if DEBUG
                timing?.Mark($"probe xml member: {xmlMember}");
                #endif
                var xmlBytes = await ProbeApkMemberBytesAsync(device, apkPath, xmlMember, cancellationToken)
                    .ConfigureAwait(false);
                if (xmlBytes is null || xmlBytes.Length == 0)
                    continue;

                // Keep the adaptive wrapper — never return bare foreground vectors (white-on-transparent).
                if (ApkVectorIconRenderer.IsAdaptiveIcon(xmlBytes))
                {
                    #if DEBUG
                    timing?.Mark($"xml adaptive confirmed: {xmlMember}");
                    #endif
                    return [xmlMember];
                }

                if (ApkVectorIconRenderer.IsVectorDrawable(xmlBytes))
                {
                    #if DEBUG
                    timing?.Mark($"xml vector confirmed: {xmlMember}");
                    #endif
                    return [xmlMember];
                }
            }

            var images = paths.Where(IsImagePath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (images.Count > 0)
                return RankIconCandidates(images);

            // String-pool adaptive wrappers as last resort.
            var poolAdaptive = FindLikelyIconPathsInStringPool(arsc)
                .Where(p => p.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (poolAdaptive.Count > 0)
                return poolAdaptive;
        }
        catch (Exception e)
        {
#if !DEPLOY
            DebugLog.PrintLine($"APK icon resolve failed for {apkPath}: {e.Message}");
#endif
        }

        return [];
    }

    private static string? TryGetTypedApplicationIcon(AxmlFile axml, ArscFile arsc)
    {
        try
        {
            var manifest = AndroidManifest.Load(axml, arsc);
            return manifest?.Application?.Node is { } node
                ? GetAttributeValue(node, "icon")
                : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Resolves <paramref name="iconRef"/> to archive members without string-pool fallbacks.
    /// Empty means the id is missing from this <c>resources.arsc</c> (often a density split owns it).
    /// </summary>
    private static List<string> ResolveIconRefToPathsStrict(string iconRef, ArscFile arsc, byte[] resourcesBytes)
    {
        iconRef = ArchivePath.NormalizeInternal(iconRef.Trim());
        if (IsImagePath(iconRef) || iconRef.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            return [iconRef];

        if (!TryParseResourceId(iconRef, out var resourceId))
            return [];

        var nativePaths = PreferIconPaths(ArscResourceResolver.ResolvePaths(resourcesBytes, resourceId));
        if (nativePaths.Count > 0)
            return nativePaths;

        return PreferIconPaths(GetResourcePaths(arsc, resourceId));
    }

    /// <summary>
    /// All file paths for an icon resource id — keeps density PNGs alongside adaptive XML
    /// (<c>mipmap/launcher_icon</c> may have broken adaptive layers but valid density PNGs).
    /// </summary>
    private static List<string> ResolveIconRefToAllPaths(string iconRef, ArscFile arsc, byte[] resourcesBytes)
    {
        iconRef = ArchivePath.NormalizeInternal(iconRef.Trim());
        if (IsImagePath(iconRef) || iconRef.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            return [iconRef];

        if (!TryParseResourceId(iconRef, out var resourceId))
            return [];

        return ArscResourceResolver.ResolvePaths(resourcesBytes, resourceId)
            .Concat(GetResourcePaths(arsc, resourceId))
            .Select(ArchivePath.NormalizeInternal)
            .Where(static p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsAdaptiveWrapperPath(string path)
    {
        if (!path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            return false;

        if (path.Contains("anydpi", StringComparison.OrdinalIgnoreCase)
            || path.Contains("adaptive", StringComparison.OrdinalIgnoreCase)
            || path.Contains("ic_launcher", StringComparison.OrdinalIgnoreCase))
            return true;

        // Named wrappers like drawable-anydpi-v26/zm_launcher.xml are covered above via anydpi.
        // Also accept non-anydpi *launcher*.xml that are not adaptive layers / splash / logos.
        if (!path.Contains("launcher", StringComparison.OrdinalIgnoreCase))
            return false;

        ReadOnlySpan<string> layerSuffixes =
        [
            "_foreground", "_background", "_splash", "_logo", "_banner", "_round_foreground",
        ];
        foreach (var suffix in layerSuffixes)
        {
            if (path.Contains(suffix, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    /// <summary>
    /// AlphaOmega ResourceMap often lists themed <c>*_background</c> rasters alongside the real
    /// adaptive XML for the same id (themed alternate plates). Prefer the adaptive wrapper.
    /// </summary>
    private static List<string> PreferIconPaths(IEnumerable<string> paths)
    {
        var list = paths
            .Select(ArchivePath.NormalizeInternal)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (list.Count <= 1)
            return list;

        var adaptiveXml = list
            .Where(IsAdaptiveWrapperPath)
            .OrderBy(p => p.Contains("default", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(p => p.Contains("anydpi", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(p => p.Length)
            .ToList();

        if (adaptiveXml.Count > 0)
            return adaptiveXml;

        // No XML — drop bare adaptive-layer backgrounds if a non-background raster exists.
        var withoutBg = list
            .Where(p => !p.Contains("_background.", StringComparison.OrdinalIgnoreCase)
                        && !p.Contains("_background_", StringComparison.OrdinalIgnoreCase))
            .ToList();

        return withoutBg.Count > 0 ? withoutBg : list;
    }

    private readonly record struct AdaptiveLayers(
        List<string> ForegroundImages,
        List<List<string>> ForegroundImageLayers,
        List<string> ForegroundXmls,
        List<string> BackgroundImages,
        List<string> BackgroundXmls,
        SKColor? BackgroundColor);

    private static AdaptiveLayers ResolveAdaptiveLayers(byte[] xmlBytes, ArscFile arsc, byte[] resourcesBytes)
    {
        using var stream = new MemoryStream(xmlBytes, writable: false);
        using var axml = new AxmlFile(new StreamLoader(stream));
        if (axml.RootNode is null)
            return new([], [], [], [], [], null);

        var foreground = new List<int>();
        var background = new List<int>();
        var other = new List<int>();
        CollectDrawableResourceIds(axml.RootNode, parentName: null, foreground, background, other);

        (List<string> Images, List<List<string>> ImageLayers, List<string> Xmls, SKColor? Color) ResolveGroup(List<int> ids)
        {
            var images = new List<string>();
            var imageLayers = new List<List<string>>();
            var xmls = new List<string>();
            SKColor? color = null;
            foreach (var id in ids)
            {
                color ??= TryGetResourceColor(arsc, resourcesBytes, id);
                var imagesForId = new List<string>();

                // Prefer native arsc paths. AlphaOmega ResourceMap often returns the wrong
                // string for sparse packages (wrong sibling drawable;
                // color resources mapped to Material state-list XMLs).
                foreach (var path in ResolveDrawableFilePaths(arsc, resourcesBytes, id))
                {
                    if (IsImagePath(path) || IsExtensionlessRasterCandidate(path))
                    {
                        images.Add(path);
                        imagesForId.Add(path);
                    }
                    else if (path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                        xmls.Add(path);
                }

                if (imagesForId.Count > 0)
                    imageLayers.Add(imagesForId.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
            }

            // Prefer real rasters over animated-vector XML siblings.
            if (images.Count > 0)
                xmls.RemoveAll(x => images.Any(img =>
                    string.Equals(Path.GetFileNameWithoutExtension(img), Path.GetFileNameWithoutExtension(x),
                        StringComparison.OrdinalIgnoreCase)));

            return (
                images.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                imageLayers,
                xmls.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                color);
        }

        var fg = ResolveGroup(foreground.Count > 0 ? foreground : other);
        var bg = ResolveGroup(background);

        // Do not invent string-pool "likely" paths for empty layers — nested adaptive wrappers
        // Broken @drawable layer ids cause compose failures. Empty layers
        // make TryComposeAdaptiveIconAsync return null so density-raster fallback can run.

        return new AdaptiveLayers(fg.Images, fg.ImageLayers, fg.Xmls, bg.Images, bg.Xmls, bg.Color);
    }

    /// <summary>
    /// When adaptive layer drawable ids are missing from the base table (density split owns
    /// the PNGs), resolve those ids across the APK bundle and merge into <paramref name="layers"/>.
    /// </summary>
    private static async Task<AdaptiveLayers> EnrichAdaptiveLayersFromSplitsAsync(
        LogicalDeviceViewModel device,
        IReadOnlyList<string> apkFiles,
        byte[] adaptiveXmlBytes,
        AdaptiveLayers layers,
        byte[] baseResources,
        CancellationToken cancellationToken)
    {
        var needFg = layers.ForegroundImages.Count == 0
                     && layers.ForegroundXmls.Count == 0
                     && layers.ForegroundImageLayers.Count == 0;
        var needBg = layers.BackgroundImages.Count == 0
                     && layers.BackgroundXmls.Count == 0
                     && layers.BackgroundColor is null;
        if (!needFg && !needBg)
            return layers;

        using var stream = new MemoryStream(adaptiveXmlBytes, writable: false);
        using var axml = new AxmlFile(new StreamLoader(stream));
        if (axml.RootNode is null)
            return layers;

        var foreground = new List<int>();
        var background = new List<int>();
        var other = new List<int>();
        CollectDrawableResourceIds(axml.RootNode, parentName: null, foreground, background, other);

        async Task<(List<string> Images, List<List<string>> ImageLayers, List<string> Xmls)> ResolveIdsAsync(
            List<int> ids)
        {
            var images = new List<string>();
            var imageLayers = new List<List<string>>();
            var xmls = new List<string>();
            foreach (var id in ids)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var imagesForId = new List<string>();
                foreach (var path in await ResolveDrawableFilePathsAcrossBundleAsync(
                             device, apkFiles, baseResources, id, cancellationToken)
                             .ConfigureAwait(false))
                {
                    if (IsImagePath(path) || IsExtensionlessRasterCandidate(path))
                    {
                        images.Add(path);
                        imagesForId.Add(path);
                    }
                    else if (path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    {
                        xmls.Add(path);
                    }
                }

                if (imagesForId.Count > 0)
                    imageLayers.Add(imagesForId.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
            }

            return (
                images.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                imageLayers,
                xmls.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
        }

        var fgImages = layers.ForegroundImages;
        var fgLayers = layers.ForegroundImageLayers;
        var fgXmls = layers.ForegroundXmls;
        if (needFg)
        {
            var fgIds = foreground.Count > 0 ? foreground : other;
            if (fgIds.Count > 0)
            {
                var resolved = await ResolveIdsAsync(fgIds).ConfigureAwait(false);
                if (resolved.Images.Count > 0 || resolved.Xmls.Count > 0)
                {
                    fgImages = resolved.Images;
                    fgLayers = resolved.ImageLayers;
                    fgXmls = resolved.Xmls;
                    #if DEBUG
                    MarkLoadStep($"adaptive fg from density split: {fgImages.Count} img, {fgXmls.Count} xml");
                    #endif
                }
            }
        }

        var bgImages = layers.BackgroundImages;
        var bgXmls = layers.BackgroundXmls;
        var bgColor = layers.BackgroundColor;
        if (needBg && background.Count > 0)
        {
            var resolved = await ResolveIdsAsync(background).ConfigureAwait(false);
            if (resolved.Images.Count > 0 || resolved.Xmls.Count > 0)
            {
                bgImages = resolved.Images;
                bgXmls = resolved.Xmls;
                #if DEBUG
                MarkLoadStep($"adaptive bg from density split: {bgImages.Count} img, {bgXmls.Count} xml");
                #endif
            }
        }

        return new AdaptiveLayers(fgImages, fgLayers, fgXmls, bgImages, bgXmls, bgColor);
    }

    private static SKColor? TryGetResourceColor(ArscFile arsc, byte[] resourcesBytes, int resourceId)
    {
        var framework = ApkVectorIconRenderer.TryResolveAndroidFrameworkColor(resourceId);
        if (framework is not null)
            return framework;

        var native = ArscResourceResolver.ResolveColor(resourcesBytes, resourceId);
        if (native is not null)
            return new SKColor(native.Value);

        if (!arsc.ResourceMap.TryGetValue(resourceId, out var rows) || rows is null)
            return null;

        foreach (var row in rows)
        {
            switch (row.DataType)
            {
                case ArscApi.DATA_TYPE.INT_COLOR_ARGB8:
                case ArscApi.DATA_TYPE.INT_COLOR_RGB8:
                case ArscApi.DATA_TYPE.INT_COLOR_ARGB4:
                case ArscApi.DATA_TYPE.INT_COLOR_RGB4:
                    return new SKColor(unchecked((uint)row.Raw));
            }

            if (!string.IsNullOrWhiteSpace(row.Value)
                && row.Value.StartsWith('#')
                && uint.TryParse(row.Value.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex))
            {
                if (row.Value.Length <= 7)
                    hex |= 0xFF000000;
                return new SKColor(hex);
            }
        }

        return null;
    }

    private static async Task<BitmapSource?> TryComposeAdaptiveIconAsync(
        LogicalDeviceViewModel device,
        IReadOnlyList<string> apkFiles,
        byte[] adaptiveXmlBytes,
        ArscFile arsc,
        byte[] resourcesBytes,
        CancellationToken cancellationToken,
        Func<int, byte[]?>? resolveXmlResource = null,
        string? packageName = null)
    {
        try
        {
            return await TryComposeAdaptiveIconCoreAsync(
                device, apkFiles, adaptiveXmlBytes, arsc, resourcesBytes, cancellationToken,
                resolveXmlResource, packageName).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            // Broken layer resource ids / nested adaptive XML must not
            // abort the whole icon load — density rasters / asset fallbacks are still usable.
#if DEBUG
            MarkLoadStep($"adaptive compose exception: {e.GetType().Name}: {e.Message}");
#else
            _ = e;
#endif
            return null;
        }
    }

    private static async Task<BitmapSource?> TryComposeAdaptiveIconCoreAsync(
        LogicalDeviceViewModel device,
        IReadOnlyList<string> apkFiles,
        byte[] adaptiveXmlBytes,
        ArscFile arsc,
        byte[] resourcesBytes,
        CancellationToken cancellationToken,
        Func<int, byte[]?>? resolveXmlResource,
        string? packageName)
    {
        var layers = ResolveAdaptiveLayers(adaptiveXmlBytes, arsc, resourcesBytes);
        layers = await EnrichAdaptiveLayersFromSplitsAsync(
            device, apkFiles, adaptiveXmlBytes, layers, resourcesBytes, cancellationToken)
            .ConfigureAwait(false);

        // Final thumbnail size. Layers are kept at ≥108/72 of that so the launcher viewport
        // crop downsamples once instead of downscale-to-192 then upscale×1.5 (blur).
        const int size = 192;
        var layerSize = AdaptiveIconLayerRasterSize(size);
        SKColor? ResolveColor(int id) => TryGetResourceColor(arsc, resourcesBytes, id);

        // Batch-extract adaptive layers once. Prefer highest-density rasters only — pulling every
        // mdpi…xxxhdpi variant wastes sync round-trips.
        var rasterSuspects = PreferHighestDensityOnly(
            layers.ForegroundImages
                .Concat(layers.BackgroundImages)
                .Concat(layers.ForegroundImageLayers.SelectMany(static l => l)));
        var suspectPaths = layers.ForegroundXmls
            .Concat(layers.BackgroundXmls)
            .Concat(rasterSuspects)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (CurrentExtractSession.Value is { } session && suspectPaths.Count > 0)
        {
            #if DEBUG
            MarkLoadStep($"adaptive PrefetchFromBundle ({suspectPaths.Count}): {string.Join(',', suspectPaths)}");
            #endif
            await session.PrefetchFromBundleAsync(apkFiles, suspectPaths, cancellationToken).ConfigureAwait(false);
        }

        // Adaptive wrappers rarely carry fillColor — preload gradients from layer vectors too.
        var xmlCache = new Dictionary<int, byte[]>();
        async Task EnsureFillXmlAsync(byte[]? drawableBytes)
        {
            if (drawableBytes is null || drawableBytes.Length == 0)
                return;

            List<string> fillPaths = [];
            foreach (var id in ApkVectorIconRenderer.CollectFillResourceIds(drawableBytes))
            {
                if (xmlCache.ContainsKey(id))
                    continue;
                if (TryGetResourceColor(arsc, resourcesBytes, id) is not null)
                    continue;

                foreach (var path in ArscResourceResolver.ResolvePaths(resourcesBytes, id))
                {
                    if (path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                        fillPaths.Add(ArchivePath.NormalizeInternal(path));
                }
            }

            if (fillPaths.Count > 0 && CurrentExtractSession.Value is { } fillSession)
                await fillSession.PrefetchFromBundleAsync(apkFiles, fillPaths, cancellationToken).ConfigureAwait(false);

            foreach (var id in ApkVectorIconRenderer.CollectFillResourceIds(drawableBytes))
            {
                if (xmlCache.ContainsKey(id))
                    continue;
                if (TryGetResourceColor(arsc, resourcesBytes, id) is not null)
                    continue;

                foreach (var path in ArscResourceResolver.ResolvePaths(resourcesBytes, id))
                {
                    if (!path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var bytes = await ReadMemberFromBundleAsync(device, apkFiles, path, cancellationToken).ConfigureAwait(false);
                    if (bytes is { Length: > 0 })
                    {
                        xmlCache[id] = bytes;
                        break;
                    }
                }
            }
        }

        await EnsureFillXmlAsync(adaptiveXmlBytes).ConfigureAwait(false);

        foreach (var xmlPath in layers.ForegroundXmls.Concat(layers.BackgroundXmls))
        {
            var layerBytes = await ReadMemberFromBundleAsync(device, apkFiles, xmlPath, cancellationToken).ConfigureAwait(false);
            await EnsureFillXmlAsync(layerBytes).ConfigureAwait(false);
        }

        if (resolveXmlResource is not null)
        {
            // Merge caller cache (if any) without overwriting layer fills we just loaded.
            foreach (var id in ApkVectorIconRenderer.CollectFillResourceIds(adaptiveXmlBytes))
            {
                if (xmlCache.ContainsKey(id))
                    continue;
                var existing = resolveXmlResource(id);
                if (existing is { Length: > 0 })
                    xmlCache[id] = existing;
            }
        }

        resolveXmlResource = id => xmlCache.TryGetValue(id, out var b) ? b : null;

        using var fgLayer = await LoadAdaptiveLayerStackAsync(
            device, apkFiles, layers.ForegroundImageLayers, layers.ForegroundImages, layers.ForegroundXmls,
            layerSize, cancellationToken, ResolveColor, resolveXmlResource, resourcesBytes,
            keepOversizedRaster: true).ConfigureAwait(false);

        using var bgLayer = await LoadAdaptiveLayerAsync(
            device, apkFiles, layers.BackgroundImages, layers.BackgroundXmls, layerSize, cancellationToken,
            ResolveColor, resolveXmlResource, resourcesBytes, keepOversizedRaster: true).ConfigureAwait(false);

        // Inline <vector> under <background>/<foreground> (Clock face; pad under layer-list).
        using var inlineBg = ApkVectorIconRenderer.TryRenderInlineAdaptiveLayer(
            adaptiveXmlBytes, "background", layerSize, SKColors.Transparent, ResolveColor, resolveXmlResource);
        using var inlineFg = ApkVectorIconRenderer.TryRenderInlineAdaptiveLayer(
            adaptiveXmlBytes, "foreground", layerSize, SKColors.Transparent, ResolveColor, resolveXmlResource);

        // Prefer inline artwork when present — drawable siblings are often transparent placeholders
        // (transparent banner placeholders under layer-list).
        var bg = inlineBg ?? bgLayer;
        var fg = inlineFg ?? fgLayer;
        SKBitmap? overlayFg = null;

        // Deskclock live hands use <rotate>/<layer-list> which we cannot render — always synthesize 3:00.
        if (IsDeskclockPackage(packageName))
        {
            overlayFg = CreateClockHandsAtThree(layerSize);
            fg = overlayFg;
        }
        else if (fg is not null && IsEmptyTransparentLayer(fg))
        {
            // Empty/transparent stock foreground (ic_launcher_foreground is a no-op
            // path) — treat as absent so background-only product art can win.
            // Do NOT use IsDegenerateIcon here: sparse light glyphs
            // valid artwork that only covers a few percent of the canvas.
            fg = null;
        }

        try
        {
            if (bg is null && fg is null && layers.BackgroundColor is null)
                return null;

            // Background-only is valid when the background carries the launcher art
            // (custom ic_launcher_background + empty foreground).
            // Stock Android Studio green alone is only half the template — use the full default.
            if (fg is null
                && !IsDeskclockPackage(packageName)
                && bg is not null
                && IsStockAndroidStudioGreenPlate(bg))
            {
                return DefaultAndroidPackageIcon.Render(size);
            }

            if (fg is null
                && !IsDeskclockPackage(packageName)
                && (bg is null || IsDegenerateIcon(bg)))
                return null;

            using var canvasBitmap = new SKBitmap(size, size, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            using var canvas = new SKCanvas(canvasBitmap);
            canvas.Clear(layers.BackgroundColor ?? SKColors.White);

            // Match AdaptiveIconDrawable: show the center 72/108 of each 108dp layer.
            // Source rect crop (not post-scale) so xxxhdpi → thumbnail is a single downsample.
            if (bg is not null)
                DrawAdaptiveIconViewport(canvas, bg, size);

            if (fg is not null)
            {
                // Clock hands are already positioned; only recenter corner-biased artwork.
                if (IsDeskclockPackage(packageName) || !IsCornerBiasedIcon(fg))
                {
                    DrawAdaptiveIconViewport(canvas, fg, size);
                }
                else
                {
                    using var centeredFg = RecenterOpaqueContent(fg);
                    DrawAdaptiveIconViewport(canvas, centeredFg ?? fg, size);
                }
            }

            if (IsDegenerateIcon(canvasBitmap))
                return null;

            return ApkVectorIconRenderer.ToBitmapSource(canvasBitmap);
        }
        finally
        {
            overlayFg?.Dispose();
        }
    }

    private static bool IsDeskclockPackage(string? packageName)
        => !string.IsNullOrEmpty(packageName)
           && (packageName.Contains("deskclock", StringComparison.OrdinalIgnoreCase)
               || packageName.Equals("com.google.android.deskclock", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Adaptive layer size in dp (full bleed including mask padding).
    /// </summary>
    private const float AdaptiveIconLayerDp = 108f;

    /// <summary>
    /// Launcher-visible viewport in dp (<c>AdaptiveIconDrawable</c> uses 72 = 108×2/3).
    /// </summary>
    private const float AdaptiveIconViewportDp = 72f;

    /// <summary>
    /// Minimum raster edge for adaptive layers so a 72/108 crop still has ≥ <paramref name="outputSize"/> pixels.
    /// </summary>
    private static int AdaptiveIconLayerRasterSize(int outputSize)
        => Math.Max(outputSize, (int)Math.Ceiling(outputSize * AdaptiveIconLayerDp / AdaptiveIconViewportDp));

    /// <summary>
    /// Draws the center 72/108 of <paramref name="layer"/> into <paramref name="outputSize"/>²
    /// (one resample; keeps xxxhdpi sharp).
    /// </summary>
    private static void DrawAdaptiveIconViewport(SKCanvas canvas, SKBitmap layer, int outputSize)
    {
        var srcW = layer.Width;
        var srcH = layer.Height;
        if (srcW <= 0 || srcH <= 0 || outputSize <= 0)
            return;

        var visibleW = srcW * AdaptiveIconViewportDp / AdaptiveIconLayerDp;
        var visibleH = srcH * AdaptiveIconViewportDp / AdaptiveIconLayerDp;
        var src = new SKRect(
            (srcW - visibleW) / 2f,
            (srcH - visibleH) / 2f,
            (srcW + visibleW) / 2f,
            (srcH + visibleH) / 2f);
        canvas.DrawBitmap(layer, src, new SKRect(0, 0, outputSize, outputSize));
    }

    /// <summary>
    /// Static white clock hands at 3:00 (hour → 3, minute → 12) for adaptive live-clock icons.
    /// </summary>
    private static SKBitmap CreateClockHandsAtThree(int size)
    {
        var bitmap = new SKBitmap(size, size, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        var cx = size / 2f;
        var cy = size / 2f;
        using var paint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };

        // Minute hand (12 o'clock): vertical bar from center upward.
        var minuteW = size * 0.055f;
        var minuteH = size * 0.34f;
        canvas.DrawRoundRect(
            new SKRoundRect(new SKRect(cx - minuteW / 2f, cy - minuteH, cx + minuteW / 2f, cy + minuteW / 2f), minuteW / 2f),
            paint);

        // Hour hand (3 o'clock): thicker horizontal bar from center to the right.
        var hourW = size * 0.28f;
        var hourH = size * 0.07f;
        canvas.DrawRoundRect(
            new SKRoundRect(new SKRect(cx - hourH / 2f, cy - hourH / 2f, cx + hourW, cy + hourH / 2f), hourH / 2f),
            paint);

        // Center cap.
        var hub = size * 0.045f;
        canvas.DrawCircle(cx, cy, hub, paint);

        return bitmap;
    }

    private static BitmapSource? CompositeOnOpaqueBackground(SKBitmap foreground, int size, SKColor background)
    {
        using var canvasBitmap = new SKBitmap(size, size, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using var canvas = new SKCanvas(canvasBitmap);
        canvas.Clear(background);
        canvas.DrawBitmap(foreground, new SKRect(0, 0, size, size));
        if (IsDegenerateIcon(canvasBitmap))
            return null;
        return ApkVectorIconRenderer.ToBitmapSource(canvasBitmap);
    }

    private static SKBitmap? UpscaleSkBitmap(SKBitmap source, int size)
    {
        if (source.Width == size && source.Height == size)
            return source.Copy();

        return source.Resize(new SKImageInfo(size, size), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
    }

    private static async Task<SKBitmap?> LoadAdaptiveLayerStackAsync(
        LogicalDeviceViewModel device,
        IReadOnlyList<string> apkFiles,
        IReadOnlyList<List<string>> imageLayers,
        IReadOnlyList<string> flatImages,
        IReadOnlyList<string> xmls,
        int size,
        CancellationToken cancellationToken,
        Func<int, SKColor?>? resolveColor = null,
        Func<int, byte[]?>? resolveXmlResource = null,
        byte[]? resourcesBytes = null,
        bool keepOversizedRaster = false)
    {
        // Calendar etc.: adaptive foreground is a layer-list of distinct drawables (plate + "31").
        if (imageLayers.Count > 1)
        {
            SKBitmap? composed = null;
            try
            {
                foreach (var layerImages in imageLayers)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var layer = await LoadAdaptiveLayerAsync(
                        device, apkFiles, layerImages, [], size, cancellationToken,
                        resolveColor, resolveXmlResource, resourcesBytes, keepOversizedRaster).ConfigureAwait(false);
                    if (layer is null)
                        continue;

                    // Density rasters are often 324²/432² — normalize before stacking.
                    using var sized = EnsureSkBitmapSize(layer, size);
                    if (sized is null)
                        continue;

                    if (composed is null)
                    {
                        composed = sized.Copy();
                        continue;
                    }

                    using var canvas = new SKCanvas(composed);
                    // White-on-black date plates: punch black to alpha. Transparent white
                    // glyphs (calendar_date_* with alpha) draw as-is.
                    if (IsMostlyDarkPlate(sized))
                    {
                        using var ink = KnockoutNearBlackKeepLight(sized);
                        if (ink is not null)
                            canvas.DrawBitmap(ink, new SKRect(0, 0, size, size));
                    }
                    else
                    {
                        canvas.DrawBitmap(sized, new SKRect(0, 0, size, size));
                    }
                }

                if (composed is not null)
                    return composed;
            }
            catch
            {
                composed?.Dispose();
                throw;
            }
        }

        return await LoadAdaptiveLayerAsync(
            device, apkFiles, flatImages.Count > 0 ? flatImages : imageLayers.SelectMany(x => x).ToList(),
            xmls, size, cancellationToken, resolveColor, resolveXmlResource, resourcesBytes,
            keepOversizedRaster).ConfigureAwait(false);
    }

    private static SKBitmap? EnsureSkBitmapSize(SKBitmap source, int size)
    {
        if (source.Width == size && source.Height == size)
            return source.Copy();

        return source.Resize(new SKImageInfo(size, size), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
    }

    /// <summary>
    /// Keeps xxxhdpi (etc.) intact when composing with a viewport crop; only upscales undersized rasters.
    /// Returns <paramref name="source"/> itself when kept or already exact — caller must not dispose it then.
    /// </summary>
    private static SKBitmap? FitAdaptiveRaster(SKBitmap source, int minSize, bool keepOversized)
    {
        if (source.Width == minSize && source.Height == minSize)
            return source;

        if (keepOversized && source.Width >= minSize && source.Height >= minSize)
            return source;

        return EnsureSkBitmapSize(source, minSize);
    }

    /// <summary>
    /// Treats near-black pixels as transparent so white-on-black date glyphs can overlay a plate.
    /// </summary>
    private static SKBitmap? KnockoutNearBlackKeepLight(SKBitmap source)
    {
        if (source.Width <= 0 || source.Height <= 0)
            return null;

        var result = new SKBitmap(source.Width, source.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        var stride = source.RowBytes;
        var buffer = new byte[stride * source.Height];
        System.Runtime.InteropServices.Marshal.Copy(source.GetPixels(), buffer, 0, buffer.Length);

        for (var i = 0; i + 3 < buffer.Length; i += 4)
        {
            var b = buffer[i];
            var g = buffer[i + 1];
            var r = buffer[i + 2];
            var a = buffer[i + 3];
            if (a < 16 || (r < 40 && g < 40 && b < 40))
            {
                buffer[i] = 0;
                buffer[i + 1] = 0;
                buffer[i + 2] = 0;
                buffer[i + 3] = 0;
                continue;
            }

            // Keep light ink (Calendar "31") fully opaque white.
            buffer[i] = 255;
            buffer[i + 1] = 255;
            buffer[i + 2] = 255;
            buffer[i + 3] = a;
        }

        System.Runtime.InteropServices.Marshal.Copy(buffer, 0, result.GetPixels(), buffer.Length);
        return result;
    }

    private static bool IsMostlyDarkPlate(SKBitmap bitmap)
    {
        if (bitmap.Width <= 0 || bitmap.Height <= 0)
            return false;

        var stride = bitmap.RowBytes;
        var buffer = new byte[stride * bitmap.Height];
        System.Runtime.InteropServices.Marshal.Copy(bitmap.GetPixels(), buffer, 0, buffer.Length);

        long opaque = 0, dark = 0, light = 0;
        for (var y = 0; y < bitmap.Height; y += 4)
        {
            var row = y * stride;
            for (var x = 0; x < bitmap.Width; x += 4)
            {
                var i = row + x * 4;
                if (i + 3 >= buffer.Length)
                    continue;
                var a = buffer[i + 3];
                if (a < 16)
                    continue;

                opaque++;
                var b = buffer[i];
                var g = buffer[i + 1];
                var r = buffer[i + 2];
                if (r < 40 && g < 40 && b < 40)
                    dark++;
                else if (r > 200 && g > 200 && b > 200)
                    light++;
            }
        }

        return opaque > 0 && dark * 5 >= opaque * 4 && light >= Math.Max(3, opaque / 200);
    }

    private static async Task<SKBitmap?> LoadAdaptiveLayerAsync(
        LogicalDeviceViewModel device,
        IReadOnlyList<string> apkFiles,
        IReadOnlyList<string> images,
        IReadOnlyList<string> xmls,
        int size,
        CancellationToken cancellationToken,
        Func<int, SKColor?>? resolveColor = null,
        Func<int, byte[]?>? resolveXmlResource = null,
        byte[]? resourcesBytes = null,
        bool keepOversizedRaster = false)
    {
        // Highest-density first; do not probe every density via archive-path ExtractSelectionForPull.
        var imageCandidates = PreferHighestDensityOnly(RankIconCandidates(images));
        if (imageCandidates.Count > 0)
        {
            if (CurrentExtractSession.Value is { } session)
            {
                await session.PrefetchFromBundleAsync(apkFiles, imageCandidates, cancellationToken)
                    .ConfigureAwait(false);

                foreach (var candidateApk in PreferApksForIconMember(apkFiles))
                {
                    var member = PickBestIconMember(
                        imageCandidates,
                        session.PresentMembers(candidateApk, imageCandidates),
                        m => session.TryGetCached(candidateApk, m)?.Length ?? 0);
                    if (member is null)
                        continue;

                    var cached = session.TryGetCached(candidateApk, member);
                    if (cached is null || cached.Length == 0)
                        continue;

                    var bmp = DecodeSkBitmap(cached);
                    if (bmp is null)
                        continue;

                    var fitted = FitAdaptiveRaster(bmp, size, keepOversizedRaster);
                    if (!ReferenceEquals(fitted, bmp))
                        bmp.Dispose();
                    if (fitted is not null)
                        return fitted;
                }
            }

            foreach (var member in imageCandidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var bytes = await ReadMemberFromBundleAsync(device, apkFiles, member, cancellationToken)
                    .ConfigureAwait(false);
                if (bytes is null || bytes.Length == 0)
                    continue;

                var bmp = DecodeSkBitmap(bytes);
                if (bmp is null)
                    continue;

                var fitted = FitAdaptiveRaster(bmp, size, keepOversizedRaster);
                if (!ReferenceEquals(fitted, bmp))
                    bmp.Dispose();
                if (fitted is not null)
                    return fitted;
            }
        }

        foreach (var xmlMember in xmls.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = await ReadMemberFromBundleAsync(device, apkFiles, xmlMember, cancellationToken).ConfigureAwait(false);
            if (bytes is null || bytes.Length == 0)
                continue;

            // Nested adaptive wrappers are not layer drawables (string-pool misfires).
            if (ApkVectorIconRenderer.IsAdaptiveIcon(bytes))
                continue;

            // <color android:color="@color/…"/> solid adaptive backgrounds.
            var colorLayer = ApkVectorIconRenderer.TryRenderColorDrawable(bytes, size, resolveColor);
            if (colorLayer is not null)
                return colorLayer;

            if (ApkVectorIconRenderer.IsVectorDrawable(bytes))
            {
                var rendered = ApkVectorIconRenderer.TryRenderToSkBitmap(
                    bytes, size, background: SKColors.Transparent, resolveColor, resolveXmlResource);
                if (rendered is not null)
                    return rendered;
            }

            // Layer-list: <item android:drawable="@…"/> (often density-split rasters).
            if (resourcesBytes is not null)
            {
                var layerListInner = await TryLoadLayerListDrawableAsync(
                    device, apkFiles, bytes, resourcesBytes, size, cancellationToken,
                    resolveColor, resolveXmlResource).ConfigureAwait(false);
                if (layerListInner is not null)
                    return layerListInner;
            }

            // <inset android:drawable="@…"/> wrapping the real vector.
            if (resourcesBytes is not null)
            {
                var insetInner = await TryLoadInsetDrawableAsync(
                    device, apkFiles, bytes, resourcesBytes, size, cancellationToken,
                    resolveColor, resolveXmlResource).ConfigureAwait(false);
                if (insetInner is not null)
                    return insetInner;
            }

            var gradient = ApkVectorIconRenderer.TryRenderGradientDrawable(bytes, size);
            if (gradient is not null)
                return gradient;
        }

        return null;
    }

    /// <summary>
    /// Keep one path per basename — the highest-density folder (xxxhdpi ≻ … ≻ mdpi).
    /// </summary>
    private static List<string> PreferHighestDensityOnly(IEnumerable<string> paths)
    {
        return paths
            .Select(ArchivePath.NormalizeInternal)
            .Where(static p => !string.IsNullOrEmpty(p))
            .GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g
                .OrderByDescending(DensityRank)
                .ThenBy(static p => p, StringComparer.OrdinalIgnoreCase)
                .First())
            .OrderByDescending(DensityRank)
            .ThenBy(static p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static async Task<SKBitmap?> TryLoadInsetDrawableAsync(
        LogicalDeviceViewModel device,
        IReadOnlyList<string> apkFiles,
        byte[] insetXmlBytes,
        byte[] resourcesBytes,
        int size,
        CancellationToken cancellationToken,
        Func<int, SKColor?>? resolveColor,
        Func<int, byte[]?>? resolveXmlResource)
    {
        try
        {
            using var stream = new MemoryStream(insetXmlBytes, writable: false);
            using var axml = new AxmlFile(new StreamLoader(stream));
            if (axml.RootNode?.NodeName.Equals("inset", StringComparison.OrdinalIgnoreCase) != true)
                return null;

            string? drawableRef = null;
            foreach (var value in EnumerateAllAttributeValues(axml.RootNode)
                         .Concat(EnumerateAttributeValues(axml.RootNode, "drawable")))
            {
                if (value.StartsWith('@'))
                {
                    drawableRef = value;
                    break;
                }
            }

            if (drawableRef is null || !TryParseResourceId(drawableRef, out var id))
                return null;

            // insetLeft/Right/Top/Bottom are parent fractions (often ~18–26%).
            var left = ResolveInsetPixels(axml.RootNode, "insetLeft", size);
            var right = ResolveInsetPixels(axml.RootNode, "insetRight", size);
            var top = ResolveInsetPixels(axml.RootNode, "insetTop", size);
            var bottom = ResolveInsetPixels(axml.RootNode, "insetBottom", size);
            if (left == 0 && right == 0 && top == 0 && bottom == 0)
            {
                var uniform = ResolveInsetPixels(axml.RootNode, "inset", size);
                left = right = top = bottom = uniform;
            }

            var dest = new SKRect(left, top, size - right, size - bottom);
            if (dest.Width < 1 || dest.Height < 1)
                dest = new SKRect(0, 0, size, size);

            SKBitmap? inner = null;
            try
            {
                var cached = resolveXmlResource?.Invoke(id);
                if (cached is { Length: > 0 } && ApkVectorIconRenderer.IsVectorDrawable(cached))
                {
                    inner = ApkVectorIconRenderer.TryRenderToSkBitmap(
                        cached, size, SKColors.Transparent, resolveColor, resolveXmlResource);
                }

                if (inner is null)
                {
                    foreach (var path in await ResolveDrawableFilePathsAcrossBundleAsync(
                                 device, apkFiles, resourcesBytes, id, cancellationToken)
                                 .ConfigureAwait(false))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var memberBytes = await ReadMemberFromBundleAsync(
                            device, apkFiles, path, cancellationToken).ConfigureAwait(false);
                        if (memberBytes is null || memberBytes.Length == 0)
                            continue;

                        if (IsImagePath(path) || IsExtensionlessRasterCandidate(path))
                        {
                            inner = DecodeSkBitmap(memberBytes);
                            if (inner is not null)
                                break;
                        }

                        if (ApkVectorIconRenderer.IsVectorDrawable(memberBytes))
                        {
                            inner = ApkVectorIconRenderer.TryRenderToSkBitmap(
                                memberBytes, size, SKColors.Transparent, resolveColor, resolveXmlResource);
                            if (inner is not null)
                                break;
                        }
                    }
                }

                if (inner is null)
                    return null;

                // No effective inset — return the inner drawable as-is.
                if (Math.Abs(dest.Left) < 0.5f && Math.Abs(dest.Top) < 0.5f
                    && Math.Abs(dest.Right - size) < 0.5f && Math.Abs(dest.Bottom - size) < 0.5f)
                {
                    var pass = inner;
                    inner = null;
                    return pass;
                }

                var result = new SKBitmap(size, size, SKColorType.Bgra8888, SKAlphaType.Unpremul);
                using var canvas = new SKCanvas(result);
                canvas.Clear(SKColors.Transparent);
                canvas.DrawBitmap(inner, dest);
                return result;
            }
            finally
            {
                inner?.Dispose();
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    /// <summary>
    /// Resolves <c>android:inset*</c> to pixels. Supports complex fractions and
    /// plain floats / percentages.
    /// </summary>
    private static float ResolveInsetPixels(XmlNode node, string attributeName, int parentSize)
    {
        foreach (var raw in EnumerateAttributeValues(node, attributeName))
        {
            if (TryParseInsetToPixels(raw, parentSize, out var px))
                return px;
        }

        return 0f;
    }

    private static bool TryParseInsetToPixels(string raw, int parentSize, out float pixels)
    {
        pixels = 0f;
        if (string.IsNullOrWhiteSpace(raw) || parentSize <= 0)
            return false;

        raw = raw.Trim();
        if (raw.EndsWith('%'))
        {
            if (!float.TryParse(raw[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var pct))
                return false;
            pixels = parentSize * (pct / 100f);
            return true;
        }

        // AlphaOmega often emits TYPE_FRACTION as the raw complex int (e.g. 558268976 = 26%).
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bits)
            || (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(raw.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bits)))
        {
            var fraction = ComplexUnitToFloat(unchecked((uint)bits));
            if (fraction > 0f && fraction < 1f)
            {
                pixels = parentSize * fraction;
                return true;
            }

            if (fraction >= 1f && fraction < parentSize)
            {
                pixels = fraction;
                return true;
            }
        }

        if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            && float.IsFinite(value))
        {
            if (value > 0f && value < 1f)
            {
                pixels = parentSize * value;
                return true;
            }

            if (value >= 1f && value < parentSize)
            {
                pixels = value;
                return true;
            }
        }

        return false;
    }

    /// <summary>Android <c>TypedValue.complexToFloat</c> for fraction / dimension complex values.</summary>
    private static float ComplexUnitToFloat(uint data)
    {
        var mantissa = (int)((data & 0xFFFFFF00u) >> 8);
        if ((mantissa & 0x800000) != 0)
            mantissa |= unchecked((int)0xFF000000);

        var radix = (data >> 4) & 0xF;
        var mult = radix switch
        {
            0 => 1f,
            1 => 1f / 128f,
            2 => 1f / 32768f,
            3 => 1f / 8388608f,
            _ => 1f,
        };
        return mantissa * mult;
    }

    private static async Task<SKBitmap?> TryLoadLayerListDrawableAsync(
        LogicalDeviceViewModel device,
        IReadOnlyList<string> apkFiles,
        byte[] layerListXmlBytes,
        byte[] resourcesBytes,
        int size,
        CancellationToken cancellationToken,
        Func<int, SKColor?>? resolveColor,
        Func<int, byte[]?>? resolveXmlResource)
    {
        try
        {
            using var stream = new MemoryStream(layerListXmlBytes, writable: false);
            using var axml = new AxmlFile(new StreamLoader(stream));
            if (axml.RootNode?.NodeName.Equals("layer-list", StringComparison.OrdinalIgnoreCase) != true)
                return null;

            var drawableIds = new List<int>();
            CollectDrawableResourceIds(axml.RootNode, parentName: null, [], [], drawableIds);
            if (drawableIds.Count == 0)
                return null;

            foreach (var id in drawableIds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var cached = resolveXmlResource?.Invoke(id);
                if (cached is { Length: > 0 } && ApkVectorIconRenderer.IsVectorDrawable(cached))
                {
                    var rendered = ApkVectorIconRenderer.TryRenderToSkBitmap(
                        cached, size, SKColors.Transparent, resolveColor, resolveXmlResource);
                    if (rendered is not null)
                        return rendered;
                }

                var paths = await ResolveDrawableFilePathsAcrossBundleAsync(
                    device, apkFiles, resourcesBytes, id, cancellationToken).ConfigureAwait(false);
                if (paths.Count == 0)
                    continue;

                if (CurrentExtractSession.Value is { } session)
                {
                    await session.PrefetchFromBundleAsync(apkFiles, paths, cancellationToken)
                        .ConfigureAwait(false);
                }

                foreach (var path in paths)
                {
                    var memberBytes = await ReadMemberFromBundleAsync(device, apkFiles, path, cancellationToken)
                        .ConfigureAwait(false);
                    if (memberBytes is null || memberBytes.Length == 0)
                        continue;

                    if (IsImagePath(path) || IsExtensionlessRasterCandidate(path))
                    {
                        var bmp = DecodeSkBitmap(memberBytes);
                        if (bmp is not null)
                            return bmp;
                    }

                    if (ApkVectorIconRenderer.IsVectorDrawable(memberBytes))
                    {
                        var rendered = ApkVectorIconRenderer.TryRenderToSkBitmap(
                            memberBytes, size, SKColors.Transparent, resolveColor, resolveXmlResource);
                        if (rendered is not null)
                            return rendered;
                    }
                }
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    /// <summary>
    /// True when opaque ink sits in a corner rather than filling the canvas.
    /// </summary>
    private static bool IsCornerBiasedIcon(SKBitmap bitmap)
    {
        if (bitmap.Width <= 0 || bitmap.Height <= 0)
            return false;

        var stride = bitmap.RowBytes;
        var buffer = new byte[stride * bitmap.Height];
        System.Runtime.InteropServices.Marshal.Copy(bitmap.GetPixels(), buffer, 0, buffer.Length);

        var minX = bitmap.Width;
        var minY = bitmap.Height;
        var maxX = -1;
        var maxY = -1;
        long opaque = 0;

        for (var y = 0; y < bitmap.Height; y += 2)
        {
            var row = y * stride;
            for (var x = 0; x < bitmap.Width; x += 2)
            {
                if (buffer[row + x * 4 + 3] < 16)
                    continue;
                opaque++;
                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }
        }

        if (opaque == 0 || maxX < minX)
            return false;

        var bw = maxX - minX + 1;
        var bh = maxY - minY + 1;
        // Content covers most of the canvas — not corner-biased.
        if (bw * 2 >= bitmap.Width && bh * 2 >= bitmap.Height)
            return false;

        var cx = (minX + maxX) / 2f;
        var cy = (minY + maxY) / 2f;
        return Math.Abs(cx - bitmap.Width / 2f) > bitmap.Width * 0.12f
               || Math.Abs(cy - bitmap.Height / 2f) > bitmap.Height * 0.12f;
    }

    /// <summary>
    /// Recenters opaque ink when vector group transforms leave artwork in a corner
    /// corner-biased artwork.
    /// </summary>
    private static SKBitmap? RecenterOpaqueContent(SKBitmap source)
    {
        if (source.Width <= 0 || source.Height <= 0)
            return null;

        var stride = source.RowBytes;
        var buffer = new byte[stride * source.Height];
        System.Runtime.InteropServices.Marshal.Copy(source.GetPixels(), buffer, 0, buffer.Length);

        var minX = source.Width;
        var minY = source.Height;
        var maxX = -1;
        var maxY = -1;

        for (var y = 0; y < source.Height; y++)
        {
            var row = y * stride;
            for (var x = 0; x < source.Width; x++)
            {
                if (buffer[row + x * 4 + 3] < 16)
                    continue;

                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }
        }

        if (maxX < minX || maxY < minY)
            return null;

        var contentCx = (minX + maxX) / 2f;
        var contentCy = (minY + maxY) / 2f;
        var canvasCx = source.Width / 2f;
        var canvasCy = source.Height / 2f;
        var dx = canvasCx - contentCx;
        var dy = canvasCy - contentCy;

        // Ignore tiny optical offsets.
        if (Math.Abs(dx) < source.Width * 0.04f && Math.Abs(dy) < source.Height * 0.04f)
            return null;

        var result = new SKBitmap(source.Width, source.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using var canvas = new SKCanvas(result);
        canvas.Clear(SKColors.Transparent);
        canvas.DrawBitmap(source, dx, dy);
        return result;
    }

    /// <summary>
    /// Fully empty adaptive layer (stock foreground). Sparse light glyphs that only
    /// cover a few percent of the canvas are still real artwork — use
    /// <see cref="IsDegenerateIcon"/> for blank-tile detection, not for discarding layers.
    /// </summary>
    private static bool IsEmptyTransparentLayer(SKBitmap bitmap)
    {
        if (bitmap.Width <= 0 || bitmap.Height <= 0)
            return true;

        var stride = bitmap.RowBytes;
        var buffer = new byte[stride * bitmap.Height];
        System.Runtime.InteropServices.Marshal.Copy(bitmap.GetPixels(), buffer, 0, buffer.Length);

        long opaque = 0, samples = 0;
        for (var y = 0; y < bitmap.Height; y += 4)
        {
            var row = y * stride;
            for (var x = 0; x < bitmap.Width; x += 4)
            {
                var i = row + x * 4;
                if (i + 3 >= buffer.Length)
                    continue;

                samples++;
                if (buffer[i + 3] >= 16)
                    opaque++;
            }
        }

        // <0.25% opaque — empty stock templates, not sparse logos.
        return samples == 0 || opaque * 400 < samples;
    }

    /// <summary>
    /// True when the bitmap is empty/transparent, or a near-solid blank light tile with no real artwork.
    /// White logos on transparency and white-bg icons with color accents are kept.
    /// </summary>
    private static bool IsDegenerateIcon(SKBitmap bitmap)
    {
        if (bitmap.Width <= 0 || bitmap.Height <= 0)
            return true;

        var stride = bitmap.RowBytes;
        var buffer = new byte[stride * bitmap.Height];
        System.Runtime.InteropServices.Marshal.Copy(bitmap.GetPixels(), buffer, 0, buffer.Length);

        long opaque = 0, light = 0, colored = 0, dark = 0, stockGreen = 0, samples = 0;
        for (var y = 0; y < bitmap.Height; y += 4)
        {
            var row = y * stride;
            for (var x = 0; x < bitmap.Width; x += 4)
            {
                var i = row + x * 4;
                if (i + 3 >= buffer.Length)
                    continue;

                samples++;
                var b = buffer[i];
                var g = buffer[i + 1];
                var r = buffer[i + 2];
                var a = buffer[i + 3];
                if (a < 16)
                    continue;

                opaque++;
                if (IsNearStockAndroidStudioGreen(r, g, b))
                    stockGreen++;
                else if (r > 230 && g > 230 && b > 230)
                    light++;
                else if (r < 40 && g < 40 && b < 40)
                    dark++;
                else
                    colored++;
            }
        }

        if (samples == 0 || opaque * 20 < samples) // <5% opaque
            return true;

        // Leftover Android Studio ic_launcher_background (#3DDC84) with no foreground.
        if (opaque * 20 >= samples * 19 && stockGreen * 20 >= opaque * 19)
            return true;

        // Stock Bugdroid foreground alone: mostly transparent, opaque ink near-white.
        if (opaque * 4 < samples
            && light * 10 >= opaque * 9
            && colored < Math.Max(3, opaque / 20)
            && dark < 3
            && stockGreen == 0)
            return true;

        // Real artwork: color accents, dark ink on light tiles, etc.
        if (colored >= Math.Max(3, opaque / 50)
            || dark >= Math.Max(3, opaque / 50))
            return false;

        // Near-solid light fill covering most of the canvas — blank tile.
        return opaque * 2 >= samples && light * 20 >= opaque * 19;
    }

    /// <summary>Near-solid Android Studio template green <c>#3DDC84</c> plate (no Bugdroid).</summary>
    private static bool IsStockAndroidStudioGreenPlate(SKBitmap bitmap)
    {
        if (bitmap.Width <= 0 || bitmap.Height <= 0)
            return false;

        var stride = bitmap.RowBytes;
        var buffer = new byte[stride * bitmap.Height];
        System.Runtime.InteropServices.Marshal.Copy(bitmap.GetPixels(), buffer, 0, buffer.Length);

        long opaque = 0, stockGreen = 0, samples = 0;
        for (var y = 0; y < bitmap.Height; y += 4)
        {
            var row = y * stride;
            for (var x = 0; x < bitmap.Width; x += 4)
            {
                var i = row + x * 4;
                if (i + 3 >= buffer.Length)
                    continue;

                samples++;
                if (buffer[i + 3] < 16)
                    continue;

                opaque++;
                if (IsNearStockAndroidStudioGreen(buffer[i + 2], buffer[i + 1], buffer[i]))
                    stockGreen++;
            }
        }

        return samples > 0
               && opaque * 20 >= samples * 19
               && stockGreen * 20 >= opaque * 19;
    }

    /// <summary>Android Studio template green <c>#3DDC84</c> (±slop for resample).</summary>
    private static bool IsNearStockAndroidStudioGreen(byte r, byte g, byte b)
        => Math.Abs(r - 61) <= 28 && Math.Abs(g - 220) <= 28 && Math.Abs(b - 132) <= 28;

    private static SKBitmap? DecodeSkBitmap(byte[] bytes)
    {
        try
        {
            return SKBitmap.Decode(bytes);
        }
        catch
        {
            return null;
        }
    }

    private static List<string> FindLikelyIconPathsInStringPool(ArscFile arsc)
    {
        var strings = arsc.ValueStringPool?.Strings;
        if (strings is null || strings.Length == 0)
            return [];

        return strings
            .Where(s => !string.IsNullOrEmpty(s)
                        && s.StartsWith("res/", StringComparison.OrdinalIgnoreCase)
                        && (IsImagePath(s) || s.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                        && IsLikelyLauncherPath(s))
            .Select(ArchivePath.NormalizeInternal)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            // Prefer adaptive wrappers (for bg+fg compositing), then dense rasters.
            .OrderBy(p => p.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) && p.Contains("anydpi", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(p => IsImagePath(p) ? 0 : 1)
            .ThenByDescending(DensityRank)
            .ToList();
    }

    private static bool IsLikelyLauncherPath(string path)
    {
        // Stock AS layer XML halves are incomplete alone (green plate or transparent Bugdroid).
        // Prefer compositing / DefaultAndroidPackageIcon over either half as a final icon.
        if (IsStockAndroidStudioLayerPath(path))
            return false;

        return path.Contains("ic_foreground", StringComparison.OrdinalIgnoreCase)
               || path.Contains("icon_launcher", StringComparison.OrdinalIgnoreCase)
               || path.Contains("/ic_launcher.", StringComparison.OrdinalIgnoreCase)
               || path.Contains("/ic_launcher_round.", StringComparison.OrdinalIgnoreCase)
               || path.Contains("/icon.", StringComparison.OrdinalIgnoreCase)
               || path.Contains("launcher", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Leftover Android Studio <c>ic_launcher_background</c> / <c>ic_launcher_foreground</c> XML
    /// (not density PNGs that happen to share the name).
    /// </summary>
    private static bool IsStockAndroidStudioLayerPath(string path)
    {
        if (!path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            return false;

        var name = Path.GetFileNameWithoutExtension(path);
        return name.Equals("ic_launcher_background", StringComparison.OrdinalIgnoreCase)
               || name.Equals("ic_launcher_foreground", StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryReadPackageLabel(byte[] manifestBytes, byte[] resourcesBytes)
    {
        try
        {
            // Prefer AlphaOmega's named android:label when present — the binary resource-map
            // walker can match a wrong early element (e.g. a settings activity label).
            // Fall back to binary AXML when AO drops the attribute.
            var labelRef = FindNamedApplicationAttribute(manifestBytes, "label")
                           ?? AxmlManifestReader.TryGetApplicationAttribute(
                               manifestBytes, AxmlManifestReader.AttrLabel)
                           ?? FindLauncherActivityLabelFromBytes(manifestBytes);

            if (string.IsNullOrWhiteSpace(labelRef))
                return null;

            labelRef = labelRef.Trim();
            if (!labelRef.StartsWith('@'))
                return IsPlausibleAppLabel(labelRef) ? labelRef : null;

            if (!TryParseResourceId(labelRef, out var resourceId))
                return null;

            // Sparse-aware resolver — AlphaOmega ResourceMap is unreliable for many APKs.
            var resolved = ArscResourceResolver.ResolveString(
                resourcesBytes, resourceId, Data.Settings.ActualUICulture);
            if (!string.IsNullOrWhiteSpace(resolved) && IsPlausibleAppLabel(resolved))
                return resolved;

            // Last resort: Latin-only majority from ResourceMap (ignore non-Latin pollution).
            var arsc = new ArscFile(resourcesBytes);
            if (!arsc.ResourceMap.TryGetValue(resourceId, out var rows) || rows is null || rows.Count == 0)
                return null;

            var candidates = rows
                .Select(r => r.Value?.Trim())
                .Where(v => !string.IsNullOrWhiteSpace(v) && !v.StartsWith("res/", StringComparison.OrdinalIgnoreCase))
                .Cast<string>()
                .ToList();

            return PickBestAppLabel(candidates, requireLatin: true);
        }
        catch
        {
            return null;
        }
    }

    private static string? FindNamedApplicationAttribute(byte[] manifestBytes, string attributeName)
    {
        try
        {
            using var manifestStream = new MemoryStream(manifestBytes, writable: false);
            using var axml = new AxmlFile(new StreamLoader(manifestStream));
            return FindApplicationAttribute(axml.RootNode, attributeName);
        }
        catch
        {
            return null;
        }
    }

    private static string? FindApplicationAttributeFromAxml(byte[] manifestBytes, string attributeName)
    {
        try
        {
            using var manifestStream = new MemoryStream(manifestBytes, writable: false);
            using var axml = new AxmlFile(new StreamLoader(manifestStream));
            return FindApplicationAttribute(axml.RootNode, attributeName)
                   ?? (attributeName.Equals("label", StringComparison.OrdinalIgnoreCase)
                       ? FindLauncherActivityLabel(axml.RootNode)
                       : null);
        }
        catch
        {
            return null;
        }
    }

    private static string? FindLauncherActivityLabelFromBytes(byte[] manifestBytes)
    {
        try
        {
            using var manifestStream = new MemoryStream(manifestBytes, writable: false);
            using var axml = new AxmlFile(new StreamLoader(manifestStream));
            return FindLauncherActivityLabel(axml.RootNode);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Some apps omit <c>android:label</c> on <c>&lt;application&gt;</c>
    /// and only label the MAIN/LAUNCHER activity. Also accept nameless <c>@7F12…</c> string refs
    /// when AlphaOmega drops the attribute name.
    /// </summary>
    private static string? FindLauncherActivityLabel(XmlNode? root)
    {
        if (root is null)
            return null;

        foreach (var activity in EnumerateNodesNamed(root, "activity", "activity-alias"))
        {
            if (!ActivityHasLauncherIntent(activity))
                continue;

            var label = GetAttributeValue(activity, "label");
            if (!string.IsNullOrWhiteSpace(label))
                return label;

            // AlphaOmega sometimes drops the attribute name — accept string refs or literals.
            foreach (var value in EnumerateAllAttributeValues(activity))
            {
                if (value.StartsWith('@') && TryParseResourceId(value, out var id))
                {
                    // Prefer string resources (type 0x12 / 0x13 typical) over drawables.
                    var type = (id >> 16) & 0xFF;
                    if (type is >= 0x0B and <= 0x14)
                        return value;
                }

                if (IsPlausibleAppLabel(value)
                    && !value.Contains('.', StringComparison.Ordinal)
                    && !value.StartsWith('@'))
                    return value;
            }
        }

        return null;
    }

    private static bool ActivityHasLauncherIntent(XmlNode activity)
    {
        if (activity.ChildNodes is null)
            return false;

        foreach (var children in activity.ChildNodes.Values)
        {
            foreach (var child in children)
            {
                if (!child.NodeName.Equals("intent-filter", StringComparison.OrdinalIgnoreCase))
                    continue;

                var hasMain = false;
                var hasLauncher = false;
                foreach (var intentChild in EnumerateChildNodes(child))
                {
                    var name = GetAttributeValue(intentChild, "name") ?? "";
                    if (intentChild.NodeName.Equals("action", StringComparison.OrdinalIgnoreCase)
                        && name.Equals("android.intent.action.MAIN", StringComparison.OrdinalIgnoreCase))
                        hasMain = true;
                    if (intentChild.NodeName.Equals("category", StringComparison.OrdinalIgnoreCase)
                        && name.Equals("android.intent.category.LAUNCHER", StringComparison.OrdinalIgnoreCase))
                        hasLauncher = true;
                }

                if (hasMain && hasLauncher)
                    return true;
            }
        }

        return false;
    }

    private static IEnumerable<XmlNode> EnumerateNodesNamed(XmlNode root, params string[] names)
    {
        var set = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<XmlNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (set.Contains(node.NodeName ?? ""))
                yield return node;

            if (node.ChildNodes is null)
                continue;

            foreach (var children in node.ChildNodes.Values)
            {
                foreach (var child in children)
                    stack.Push(child);
            }
        }
    }

    private static IEnumerable<XmlNode> EnumerateChildNodes(XmlNode node)
    {
        if (node.ChildNodes is null)
            yield break;

        foreach (var children in node.ChildNodes.Values)
        {
            foreach (var child in children)
                yield return child;
        }
    }

    private static IEnumerable<string> EnumerateAllAttributeValues(XmlNode node)
    {
        if (node.Attributes is null)
            yield break;

        foreach (var (_, values) in node.Attributes)
        {
            if (values is null)
                continue;
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    yield return value;
            }
        }
    }

    /// <summary>
    /// AlphaOmega's ResourceMap often lists every locale (and sometimes polluted values) for one id.
    /// Prefer a stable Latin/default display name and reject format strings / class names.
    /// </summary>
    private static string? PickBestAppLabel(IReadOnlyList<string> candidates, bool requireLatin = false)
    {
        var valid = candidates.Where(IsPlausibleAppLabel).ToList();
        if (valid.Count == 0)
            return null;

        var latin = valid.Where(IsMostlyLatin).ToList();
        if (requireLatin && latin.Count == 0)
            return null;

        var pool = latin.Count > 0 ? latin : valid;

        return pool
            .GroupBy(s => s, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key.Length)
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.Key)
            .FirstOrDefault();
    }

    private static bool IsPlausibleAppLabel(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        value = value.Trim();
        if (value.Length is < 1 or > 80)
            return false;

        // Format strings / placeholders (Play services prompts, etc.)
        if (value.Contains('%', StringComparison.Ordinal))
            return false;

        if (value.StartsWith("res/", StringComparison.OrdinalIgnoreCase))
            return false;

        if (value.Length <= 2)
            return false;

        // Resource typed-value mistakes (e.g. "65536").
        if (value.Length <= 8 && value.All(char.IsAsciiDigit))
            return false;

        // Boolean attrs misread as labels ("true").
        if (value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("false", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || value.Equals("no", StringComparison.OrdinalIgnoreCase))
            return false;

        // Fully-qualified Java/Kotlin type names mistakenly mapped under the label id.
        var dotParts = value.Split('.');
        if (dotParts.Length >= 3
            && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '$')
            && dotParts[0] is "com" or "org" or "net" or "io" or "android" or "java" or "kotlin")
            return false;

        if (value.Contains("android.", StringComparison.OrdinalIgnoreCase)
            && value.Contains('.', StringComparison.Ordinal)
            && !value.Contains(' ', StringComparison.Ordinal))
            return false;

        // Do not reject PascalCase tokens — system overlays often ship the resource name
        // as the English label (SetupWizardOverlay). Filtering them sent ResolveString
        // into ResourceMap fishing, which preferred spaced Latin (e.g. Estonian).

        return true;
    }

    private static bool IsMostlyLatin(string value)
    {
        var letters = 0;
        var latin = 0;
        foreach (var c in value)
        {
            if (!char.IsLetter(c))
                continue;

            letters++;
            if (c <= 0x024F) // Basic Latin + Latin Extended
                latin++;
        }

        return letters == 0 || latin * 2 >= letters;
    }

    private static async Task SaveBitmapAsPngAsync(BitmapSource bitmap, string path, CancellationToken cancellationToken)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        await using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        encoder.Save(fs);
        await fs.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void CollectDrawableResourceIds(
        XmlNode node,
        string? parentName,
        List<int> foreground,
        List<int> background,
        List<int> other)
    {
        var nodeName = node.NodeName ?? "";
        var inFg = IsForegroundContext(nodeName, parentName);
        var inBg = IsBackgroundContext(nodeName, parentName);

        // Named attrs (drawable/src) plus nameless @7F… values AlphaOmega sometimes emits.
        foreach (var value in EnumerateAllAttributeValues(node)
                     .Concat(EnumerateAttributeValues(node, "drawable"))
                     .Concat(EnumerateAttributeValues(node, "src")))
        {
            if (!TryParseResourceId(value, out var id))
                continue;

            if (inFg)
                foreground.Add(id);
            else if (inBg)
                background.Add(id);
            else
                other.Add(id);
        }

        if (node.ChildNodes is null)
            return;

        foreach (var children in node.ChildNodes.Values)
        {
            foreach (var child in children)
                CollectDrawableResourceIds(child, nodeName, foreground, background, other);
        }
    }

    private static bool IsForegroundContext(string nodeName, string? parentName)
        => nodeName.Equals("foreground", StringComparison.OrdinalIgnoreCase)
           || (parentName?.Equals("foreground", StringComparison.OrdinalIgnoreCase) ?? false);

    private static bool IsBackgroundContext(string nodeName, string? parentName)
        => nodeName.Equals("background", StringComparison.OrdinalIgnoreCase)
           || (parentName?.Equals("background", StringComparison.OrdinalIgnoreCase) ?? false);

    private static string? FindApplicationAttribute(XmlNode? root, string attributeName)
    {
        if (root is null)
            return null;

        if (root.NodeName.Equals("application", StringComparison.OrdinalIgnoreCase))
            return GetAttributeValue(root, attributeName);

        if (root.ChildNodes is null)
            return null;

        foreach (var children in root.ChildNodes.Values)
        {
            foreach (var child in children)
            {
                var found = FindApplicationAttribute(child, attributeName);
                if (found is not null)
                    return found;
            }
        }

        return null;
    }

    private static string? GetAttributeValue(XmlNode node, string attributeName)
    {
        foreach (var value in EnumerateAttributeValues(node, attributeName))
            return value;

        return null;
    }

    private static IEnumerable<string> EnumerateAttributeValues(XmlNode node, string attributeName)
    {
        if (node.Attributes is null)
            yield break;

        foreach (var (key, values) in node.Attributes)
        {
            if (!AttributeNameMatches(key, attributeName) || values is null)
                continue;

            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    yield return value;
            }
        }
    }

    private static bool AttributeNameMatches(string key, string attributeName)
    {
        if (key.Equals(attributeName, StringComparison.OrdinalIgnoreCase))
            return true;

        // android:icon / {http://schemas.android.com/apk/res/android}icon
        var suffix = ":" + attributeName;
        return key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
               || key.EndsWith("}" + attributeName, StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> GetResourcePaths(ArscFile arsc, int resourceId)
    {
        if (!arsc.ResourceMap.TryGetValue(resourceId, out var rows) || rows is null || rows.Count == 0)
            return [];

        return rows
            .Select(row => row.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => ArchivePath.NormalizeInternal(value.Trim()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Drawable/mipmap file paths for a resource id. Native arsc first — AlphaOmega's
    /// <see cref="ArscFile.ResourceMap"/> frequently maps the wrong pool string.
    /// </summary>
    private static List<string> ResolveDrawableFilePaths(ArscFile arsc, byte[] resourcesBytes, int resourceId)
    {
        var native = ArscResourceResolver.ResolvePaths(resourcesBytes, resourceId)
            .Select(ArchivePath.NormalizeInternal)
            .Where(static p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        IEnumerable<string> candidates = native.Count > 0 ? native : GetResourcePaths(arsc, resourceId);

        return candidates
            .Select(ArchivePath.NormalizeInternal)
            .Where(static p => !string.IsNullOrWhiteSpace(p) && !IsColorResourcePath(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsColorResourcePath(string path)
        => path.Contains("/color/", StringComparison.OrdinalIgnoreCase)
           || path.Contains(@"\color\", StringComparison.OrdinalIgnoreCase)
           || path.StartsWith("res/color", StringComparison.OrdinalIgnoreCase);

    private static bool TryParseResourceId(string value, out int resourceId)
    {
        resourceId = 0;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        value = value.Trim();
        if (value.StartsWith('@'))
            value = value[1..];

        // Named refs like @mipmap/ic_launcher are not resolved here.
        if (value.Contains('/'))
            return false;

        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return int.TryParse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out resourceId);

        return int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out resourceId);
    }

    private static bool IsImagePath(string path)
    {
        var ext = Path.GetExtension(path);
        return ImageExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Obfuscated APK members (WebView <c>res/9M</c>) omit extensions; sniff container magic.
    /// </summary>
    private static string? DetectRasterExtension(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 12
            && bytes[0] == (byte)'R'
            && bytes[1] == (byte)'I'
            && bytes[2] == (byte)'F'
            && bytes[3] == (byte)'F'
            && bytes[8] == (byte)'W'
            && bytes[9] == (byte)'E'
            && bytes[10] == (byte)'B'
            && bytes[11] == (byte)'P')
        {
            return ".webp";
        }

        if (bytes.Length >= 8
            && bytes[0] == 0x89
            && bytes[1] == (byte)'P'
            && bytes[2] == (byte)'N'
            && bytes[3] == (byte)'G'
            && bytes[4] == 0x0D
            && bytes[5] == 0x0A
            && bytes[6] == 0x1A
            && bytes[7] == 0x0A)
        {
            return ".png";
        }

        if (bytes.Length >= 3
            && bytes[0] == 0xFF
            && bytes[1] == 0xD8
            && bytes[2] == 0xFF)
        {
            return ".jpg";
        }

        return null;
    }

    private static bool FileLooksLikeWebp(string localPath)
    {
        try
        {
            using var fs = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            Span<byte> hdr = stackalloc byte[12];
            var read = fs.Read(hdr);
            return DetectRasterExtension(hdr[..read]) == ".webp";
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Some packs store PNG/WebP without an extension (<c>res/raw/…</c> or root entries).
    /// </summary>
    private static bool IsExtensionlessRasterCandidate(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains(' ', StringComparison.Ordinal))
            return false;
        if (path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrEmpty(Path.GetExtension(path)))
            return false;

        var name = Path.GetFileName(path);
        return name.Length is >= 1 and <= 64
               && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
    }

    /// <summary>
    /// Last-resort discovery when manifest/arsc resolution finds nothing.
    /// </summary>
    private static List<ArchiveEntry> DiscoverIconMembersOnDevice(
        string deviceId,
        string apkPath,
        CancellationToken cancellationToken)
    {
        var unzip = ShellCommands.TranslateCommand("unzip");
        var apkEsc = ADBService.EscapeAdbShellString(apkPath);
        var script =
            $"{unzip} -l {apkEsc} 2>/dev/null | grep -Ei 'res/(mipmap|drawable)[^/]*/[^/]*(launcher|app_icon|ic_launcher)[^/]*\\.(png|webp|xml)$' | head -n 60";

        _ = ADBService.ExecuteDeviceAdbShellCommand(
            deviceId,
            "sh",
            out var stdout,
            out _,
            cancellationToken,
            "-c",
            ADBService.EscapeAdbShellString(script));

        var result = new List<ArchiveEntry>();
        foreach (var rawLine in stdout.Split(ADBService.LINE_SEPARATORS, StringSplitOptions.RemoveEmptyEntries))
        {
            var match = UnzipListEntryLine().Match(rawLine);
            if (!match.Success)
                continue;

            var name = ArchivePath.NormalizeInternal(match.Groups["Name"].Value.TrimEnd());
            if (string.IsNullOrEmpty(name))
                continue;

            long.TryParse(match.Groups["Length"].Value, out var size);
            result.Add(new ArchiveEntry(name, IsDirectory: false, size, Modified: null));
        }

        return result;
    }

    [GeneratedRegex(@"^\s*(?<Length>\d+)\s+\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}\s+(?<Name>.+\S)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex UnzipListEntryLine();

    private static List<string> HeuristicIconCandidates()
    {
        string[] names = ["ic_launcher", "ic_launcher_round", "ic_launcher_foreground", "icon_launcher", "icon", "launcher_icon"];
        // Drawable densities first — some apps ship launcher PNGs only under drawable-*.
        string[] folders =
        [
            "drawable-xxxhdpi-v4", "drawable-xxxhdpi", "drawable-xxhdpi-v4", "drawable-xxhdpi",
            "drawable-xhdpi-v4", "drawable-xhdpi",
            "mipmap-xxxhdpi-v4", "mipmap-xxxhdpi", "mipmap-xxhdpi-v4", "mipmap-xxhdpi",
            "mipmap-xhdpi-v4", "mipmap-xhdpi", "mipmap-hdpi-v4", "mipmap-hdpi",
        ];
        string[] extensions = [".png", ".webp"];

        var result = new List<string>();
        foreach (var folder in folders)
        {
            foreach (var name in names)
            {
                foreach (var ext in extensions)
                    result.Add($"res/{folder}/{name}{ext}");
            }
        }

        // Flutter apps often keep launcher art under assets/.
        result.Add("assets/flutter_assets/images/ic_launcher.png");
        result.Add("assets/flutter_assets/images/ic_launcher.webp");
        result.Add("assets/flutter_assets/AppIcon.png");

        return RankIconCandidates(result);
    }

    /// <summary>
    /// Heuristic paths for probing: top densities per basename so xxhdpi-only packs are not
    /// skipped when xxxhdpi variants dominate a flat density-sorted <c>Take(N)</c>.
    /// </summary>
    private static List<string> HeuristicIconProbeCandidates(int maxPaths)
    {
        const int densitiesPerName = 4;
        // Keep per-name density picks in group order — do not re-sort by density before Take,
        // or only xxxhdpi paths survive.
        return HeuristicIconCandidates()
            .GroupBy(static p => Path.GetFileName(p) ?? p, StringComparer.OrdinalIgnoreCase)
            .SelectMany(static g => g.OrderByDescending(DensityRank).Take(densitiesPerName))
            .Take(maxPaths)
            .ToList();
    }

    private static List<string> RankIconCandidates(IEnumerable<string> candidates)
        => candidates
            .Select(ArchivePath.NormalizeInternal)
            .Where(p => IsImagePath(p) || IsExtensionlessRasterCandidate(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(DensityRank)
            .ThenByDescending(p => IsImagePath(p) ? 1 : 0)
            .ToList();

    /// <summary>
    /// Discovery ranking that keeps adaptive / launcher XML ahead of logos and density rasters.
    /// </summary>
    private static List<string> RankDiscoveredIconCandidates(IEnumerable<string> candidates)
        => candidates
            .Select(ArchivePath.NormalizeInternal)
            .Where(p => !IsStockAndroidStudioLayerPath(p)
                        && (IsImagePath(p)
                            || IsExtensionlessRasterCandidate(p)
                            || p.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(IconCandidateScore)
            .ThenByDescending(DensityRank)
            .ToList();

    private static int IconCandidateScore(string path)
    {
        if (IsAdaptiveWrapperPath(path))
            return 4;
        if (path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
            && path.Contains("anydpi", StringComparison.OrdinalIgnoreCase))
            return 3;
        if (path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            return 1;
        // Prefer full launcher rasters over *_logo / activity glyphs when XML is absent.
        if (path.Contains("_logo", StringComparison.OrdinalIgnoreCase)
            || path.Contains("_splash", StringComparison.OrdinalIgnoreCase))
            return 0;
        return 2;
    }

    private static string? PickBestIconMember(
        IReadOnlyList<string> rankedCandidates,
        IEnumerable<string> availableMembers,
        Func<string, long>? sizeOf = null)
    {
        var present = new HashSet<string>(
            availableMembers.Select(ArchivePath.NormalizeInternal),
            StringComparer.OrdinalIgnoreCase);
        if (rankedCandidates.Count == 0 || present.Count == 0)
            return null;

        // Obfuscated packs store density variants as short res/ names with no
        // mipmap-*dpi* folder — DensityRank ties at 0; prefer the largest pulled bytes.
        return rankedCandidates
            .Select(ArchivePath.NormalizeInternal)
            .Where(present.Contains)
            .OrderByDescending(IconCandidateScore)
            .ThenByDescending(DensityRank)
            // Prefer brand / resolved names over leftover Android Studio templates when tied.
            .ThenBy(StockLauncherTemplatePenalty)
            .ThenByDescending(p => sizeOf?.Invoke(p) ?? 0)
            .ThenBy(static p => p, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    /// <summary>0 = keep; 1 = demote stock <c>ic_launcher</c> / <c>ic_launcher_round</c> templates.</summary>
    private static int StockLauncherTemplatePenalty(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        if (name.Equals("ic_launcher", StringComparison.OrdinalIgnoreCase)
            || name.Equals("ic_launcher_round", StringComparison.OrdinalIgnoreCase))
            return 1;
        return 0;
    }

    private static string? PickBestIconMember(IReadOnlyList<string> rankedCandidates, IReadOnlyList<ArchiveEntry> listing)
    {
        if (rankedCandidates.Count == 0 || listing.Count == 0)
            return null;

        return PickBestIconMember(
            rankedCandidates,
            listing.Where(static e => !e.IsDirectory).Select(static e => e.Path),
            p => FindEntry(listing, p)?.Size ?? 0);
    }

    private static int DensityRank(string path)
    {
        for (var i = 0; i < DensityOrder.Length; i++)
        {
            if (path.Contains(DensityOrder[i], StringComparison.OrdinalIgnoreCase))
                return DensityOrder.Length - i;
        }

        return 0;
    }

    private static ArchiveEntry? FindEntry(IReadOnlyList<ArchiveEntry> entries, string memberName)
    {
        var normalized = ArchivePath.NormalizeInternal(memberName);
        foreach (var entry in entries)
        {
            if (entry.IsDirectory)
                continue;

            if (string.Equals(ArchivePath.NormalizeInternal(entry.Path), normalized, StringComparison.OrdinalIgnoreCase))
                return entry;
        }

        return null;
    }

    private static string NormalizeCrc(string crc)
        => crc.Trim().ToUpperInvariant();

    private static BitmapSource? DecodeBitmap(string localPath)
    {
        try
        {
            // WIC's WebP decoder drops alpha (VP8L → Bgr32 / opaque black).
            // Also sniff content: extensionless WebP was historically cached as .png.
            var isWebp = Path.GetExtension(localPath).Equals(".webp", StringComparison.OrdinalIgnoreCase)
                || FileLooksLikeWebp(localPath);
            if (isWebp)
                return DecodeWebpWithAlpha(localPath) ?? DecodeBitmapWithWic(localPath);

            return DecodeBitmapWithWic(localPath);
        }
        catch
        {
            return null;
        }
    }

    private static BitmapSource? DecodeBitmapWithWic(string localPath)
    {
        using var stream = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.StreamSource = stream;
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static BitmapSource? DecodeWebpWithAlpha(string localPath)
    {
        using var skBitmap = SKBitmap.Decode(localPath);
        if (skBitmap is null || skBitmap.Width <= 0 || skBitmap.Height <= 0)
            return null;

        using var bgra = skBitmap.ColorType == SKColorType.Bgra8888 && skBitmap.AlphaType != SKAlphaType.Opaque
            ? null
            : skBitmap.Copy(SKColorType.Bgra8888);

        var source = bgra ?? skBitmap;
        var stride = source.RowBytes;
        var height = source.Height;
        var width = source.Width;
        var buffer = new byte[stride * height];
        Marshal.Copy(source.GetPixels(), buffer, 0, buffer.Length);

        var writeable = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        writeable.WritePixels(new Int32Rect(0, 0, width, height), buffer, stride, 0);
        writeable.Freeze();
        return writeable;
    }

    private static string? TryReadPackageName(byte[] manifestBytes, byte[] resourcesBytes)
    {
        try
        {
            using var manifestStream = new MemoryStream(manifestBytes, writable: false);
            using var axml = new AxmlFile(new StreamLoader(manifestStream));
            var arsc = new ArscFile(resourcesBytes);
            var manifest = AndroidManifest.Load(axml, arsc);
            if (!string.IsNullOrWhiteSpace(manifest?.Package))
                return manifest.Package.Trim();

            return axml.RootNode is null ? null : GetAttributeValue(axml.RootNode, "package");
        }
        catch
        {
            return null;
        }
    }

    private static string SanitizePackageFileName(string packageName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = packageName.Trim().ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (invalid.Contains(chars[i]) || chars[i] is '|' or '/')
                chars[i] = '_';
        }

        var name = new string(chars);
        return string.IsNullOrWhiteSpace(name) ? "unknown" : name;
    }

    private static string GetLocalIconDirectory(string serialNumber)
        => Path.Combine(Data.AppDataPath, serialNumber, ICONS_SUBFOLDER);

    private static string GetLocalIconPath(string serialNumber, string packageName, string iconExt)
        => Path.Combine(GetLocalIconDirectory(serialNumber), SanitizePackageFileName(packageName) + iconExt);

    private static object GetDeviceLock(string serialNumber)
        => DeviceLocks.GetOrAdd(serialNumber, _ => new object());

    private static Dictionary<string, ApkIconCacheEntry> GetOrLoadCache(string serialNumber)
    {
        if (DeviceCaches.TryGetValue(serialNumber, out var cached))
            return cached;

        var loaded = ReadCache(serialNumber);
        DeviceCaches[serialNumber] = loaded;
        return loaded;
    }

    private static Dictionary<string, ApkIconCacheEntry> ReadCache(string serialNumber)
    {
        var result = new Dictionary<string, ApkIconCacheEntry>(StringComparer.Ordinal);
        var csvPath = Path.Combine(Data.AppDataPath, serialNumber, CSV_FILE);
        if (!File.Exists(csvPath))
            return result;

        string[] lines;
        try
        {
            lines = File.ReadAllLines(csvPath, CsvEncoding);
        }
        catch
        {
            return result;
        }

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var parts = line.Split('|');
            if (parts.Length < 4)
                continue;

            if (!DateOnly.TryParseExact(parts[2], CsvDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                continue;

            result[parts[0]] = new ApkIconCacheEntry(
                NormalizeCrc(parts[1]),
                date,
                NormalizeIconExtField(parts[3]),
                parts.Length >= 5 ? NormalizeLabelField(parts[4]) : null);
        }

        return result;
    }

    /// <summary>
    /// Accepts extension-only values (<c>.webp</c>), fail marker, or legacy full filenames (<c>pkg.webp</c>).
    /// </summary>
    private static string NormalizeIconExtField(string field)
    {
        if (string.IsNullOrWhiteSpace(field))
            return "";

        field = field.Trim();
        if (field == FailMarker)
            return FailMarker;

        if (field.StartsWith('.'))
            return field.ToLowerInvariant();

        var ext = Path.GetExtension(field);
        return string.IsNullOrEmpty(ext) ? "" : ext.ToLowerInvariant();
    }

    private static string? NormalizeLabelField(string field)
    {
        if (string.IsNullOrWhiteSpace(field))
            return null;

        field = field.Trim();
        if (field == FailMarker)
            return FailMarker;

        // Re-encode so legacy bare / "*" keys become the real UI locale and pseudo-accent
        // values are dropped (those locales will be re-fetched).
        var map = ParseLocalizedLabels(field);
        if (map.Count == 0)
            return null;

        return EncodeLocalizedLabels(map);
    }

    private static void WriteCache(string serialNumber, Dictionary<string, ApkIconCacheEntry> cache)
    {
        var deviceDir = Path.Combine(Data.AppDataPath, serialNumber);
        Directory.CreateDirectory(deviceDir);
        var csvPath = Path.Combine(deviceDir, CSV_FILE);
        var lines = cache.Select(kvp =>
        {
            var iconExt = string.IsNullOrEmpty(kvp.Value.IconExt) ? "" : kvp.Value.IconExt;
            var label = string.IsNullOrEmpty(kvp.Value.Label) ? "" : kvp.Value.Label.Replace('|', '_');
            return $"{kvp.Key}|{kvp.Value.ManifestCrc}|{kvp.Value.CheckedDate.ToString(CsvDateFormat, CultureInfo.InvariantCulture)}|{iconExt}|{label}";
        });
        File.WriteAllText(csvPath, string.Join(Environment.NewLine, lines), CsvEncoding);
    }
}
