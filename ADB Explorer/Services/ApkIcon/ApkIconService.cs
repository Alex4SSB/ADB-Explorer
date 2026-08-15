using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.ViewModels;
using AlphaOmega.Debug;
using AlphaOmega.Debug.Manifest;
using SkiaSharp;
using Wpf.Ui.Appearance;

namespace ADB_Explorer.Services;

/// <summary>
/// Lazy-loads launcher icons from device-side <c>.apk</c> files via targeted <c>unzip</c>
/// plus AlphaOmega parsing of <c>AndroidManifest.xml</c> / <c>resources.arsc</c>
/// (including adaptive-icon XML → foreground/background rasters).
/// Cache is keyed by Android package name (shared across app-drive and file paths);
/// invalidation uses the AndroidManifest CRC-32 and a date-only CSV stamp
/// (not <see cref="AppSettings.ThumbsAge"/>).
/// Loads respect <see cref="Data.DeviceCts"/> and are cleared by <see cref="CancelPending"/>.
/// Concurrency is <see cref="AppSettings.ThumbAndIconConcurrency"/> (⌈MaxSimultaneousOps / 6⌉)
/// because each load fans out into several adb processes.
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
    /// <summary>CSV 6th field: OEM clock face already has hands.</summary>
    private const string ClockHandsBaked = "baked";
    /// <summary>CSV 6th field: blank clock disc — overlay live hands at display time.</summary>
    private const string ClockHandsOverlay = "overlay";
    private const int MaxIconCandidatesToProbe = 12;

    private static readonly Encoding CsvEncoding = new UTF8Encoding(true);
    private static readonly ConcurrentDictionary<string, object> DeviceLocks = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Dictionary<string, ApkIconCacheEntry>> DeviceCaches = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, byte> ClockHandsPersistInFlight = new(StringComparer.Ordinal);
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
    /// <param name="ClockHands">
    /// Deskclock faces only: <c>baked</c> (OEM hands already in the PNG) or <c>overlay</c>
    /// (blank disc — draw live hands). Null/empty until first icon-view inspect.
    /// </param>
    private readonly record struct ApkIconCacheEntry(
        string ManifestCrc,
        DateOnly CheckedDate,
        string IconExt,
        string? Label,
        string? ClockHands = null);

    private static bool _uiLanguageHooked;
    private static bool _themeContrastHooked;

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
                var resultText = success ? "ok" : "failed";
                if (!string.IsNullOrEmpty(note))
                    resultText += $" ({note})";
                sb.AppendLine($"Result: {resultText}");
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
        ApkLoadPriority priority = ApkLoadPriority.Background,
        bool quiet = false)
        => BeginLoadCore(device, apkPath, packageName, onReady, priority, quiet);

    private static void BeginLoadCore(
        LogicalDeviceViewModel device,
        string apkPath,
        string? packageName,
        Action<BitmapSource?>? onReady,
        ApkLoadPriority priority,
        bool quiet = false
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

            // Date rollover: keep showing yesterday's icon while the re-check runs.
            var stored = TryGetStoredIcon(device, packageName);
            if (stored is not null)
                onReady?.Invoke(stored);

            // Overlays / no-launcher APKs must not re-unzip every launch. FailMarker is a settled miss.
            if (HasSettledIconMiss(device, packageName))
            {
                onReady?.Invoke(null);
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

        var request = new LoadRequest(pullKey, device, apkPath, packageName, onReady, LabelOnly: false, priority, Quiet: quiet)
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

            var stored = TryGetStoredIcon(device, packageName);
            if (stored is not null)
                file.ApplyApkIcon(stored);
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

        package.DeviceSerial ??= device.SerialNumber;

        // Force-reload / StopAllLoading: do not enqueue and do not treat as a finished miss
        // (otherwise the tile flips to the green Bugdroid instead of staying grayscale).
        if (IsLoadingStopped)
            return;

        ApplyCachedLabel(device, package);

        var alreadyHasIcon = package.Icon is not null;

        if (alreadyHasIcon)
        {
            package.IconLoadCompleted = true;
            if (IsIconFreshToday(device, package.Name))
            {
                BeginEnsureLabelForPackage(package, priority);
                return;
            }

            // Date rolled: keep the current tile and re-check in the background.
            // Do not raise the thumbnail tooltip — the icon is already on screen.
        }
        else if (!string.IsNullOrEmpty(package.Name))
        {
            var cached = TryGetCachedIcon(device, package.Name);
            if (cached is not null)
            {
                package.Icon = cached;
                BeginEnsureLabelForPackage(package, priority);
                return;
            }

            var stored = TryGetStoredIcon(device, package.Name);
            if (stored is not null)
                package.Icon = stored;

            if (HasSettledIconMiss(device, package.Name))
            {
                package.IconLoadCompleted = true;
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
        }, priority, quiet: alreadyHasIcon);
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
        if (package.Icon is null && !HasSettledIconMiss(device, package.Name))
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
                        return (ForDisplay(DecodeBitmap(warmPath)), packageName);
                    }
                }

                if (cache.TryGetValue(packageName, out warm)
                    && IsSettledIconMiss(warm, packageName))
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
                && IsSettledIconMiss(existing, packageName))
            {
                var missLabel = MergeLocaleLabel(existing.Label, label);
                var missUpdated = existing with
                {
                    ManifestCrc = string.IsNullOrEmpty(manifestCrc) ? existing.ManifestCrc : manifestCrc,
                    CheckedDate = today,
                    Label = missLabel,
                };
                if (!string.Equals(existing.ManifestCrc, missUpdated.ManifestCrc, StringComparison.OrdinalIgnoreCase))
                    missUpdated = missUpdated with { ClockHands = null };
                if (existing.CheckedDate != today
                    || missUpdated.Label != existing.Label
                    || missUpdated.ManifestCrc != existing.ManifestCrc)
                {
                    cache[packageName] = missUpdated;
                    WriteCache(serial, cache);
                }

#if DEBUG
                timing?.Mark("settled miss — skip icon member extract");
#endif
                return (null, packageName);
            }

            if (
#if DEBUG
                timing is null &&
#endif
                cache.TryGetValue(packageName, out existing)
                && string.Equals(existing.ManifestCrc, manifestCrc, StringComparison.OrdinalIgnoreCase)
                && IsSuccessfulIconExt(existing.IconExt)
                && !IsCalendarPackage(packageName))
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
                    return (ForDisplay(DecodeBitmap(localPath)), packageName);
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

                            // Keep declared non-white colors. Drop near-white so light FG ink
                            // (VLC) is not washed out; raster white plates are omitted in compose.
                            var bgColor = layers.BackgroundColor ?? SKColors.Transparent;
                            if (IsNearWhiteColor(bgColor))
                                bgColor = SKColors.Transparent;

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

                    bitmap = CompositeOnOpaqueBackground(sk, 192, SKColors.Transparent);
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
                            var pngPath = GetLocalIconPath(serial, packageName, ".png");
                            await SaveBitmapAsPngAsync(bitmap, pngPath, cancellationToken).ConfigureAwait(false);
                            if (!string.Equals(pngPath, localFile, StringComparison.OrdinalIgnoreCase)
                                && File.Exists(localFile))
                            {
                                try { File.Delete(localFile); } catch { /* ignore */ }
                            }

                            MarkFetchResult(serial, packageName, manifestCrc, today, ".png", label,
                                InspectClockHandsField(packageName, bitmap));
                            #if DEBUG
                            timing?.Mark("upscaled tiny raster saved");
                            #endif
                            return (ForDisplay(bitmap), packageName);
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

        MarkFetchResult(serial, packageName, manifestCrc, today, iconExt, label,
            InspectClockHandsField(packageName, bitmap));
#if DEBUG
        timing?.Mark("MarkFetchResult / cache write done");
#endif
        return (ForDisplay(bitmap), packageName);
        }
        finally
        {
            CurrentExtractSession.Value = null;
        }
    }

    /// <param name="iconExt">New icon ext, <see cref="FailMarker"/>, or null to leave the existing icon field unchanged.</param>
    /// <param name="label">New label, <see cref="FailMarker"/>, or null to leave the existing label unchanged.</param>
    /// <param name="clockHands">
    /// Deskclock inspect result (<c>baked</c>/<c>overlay</c>), or null to keep/clear with CRC.
    /// </param>
    private static void MarkFetchResult(
        string serial,
        string packageName,
        string manifestCrc,
        DateOnly today,
        string? iconExt,
        string? label,
        string? clockHands = null)
    {
        lock (GetDeviceLock(serial))
        {
            var cache = GetOrLoadCache(serial);
            cache.TryGetValue(packageName, out var existing);

            var nextIcon = iconExt ?? existing.IconExt ?? "";
            string? nextLabel;
            if (label is null)
            {
                nextLabel = existing.Label;
            }
            else
            {
                var existingLabel = existing.Label == FailMarker ? null : existing.Label;
                nextLabel = MergeLocaleLabel(existingLabel, label);
            }
            var nextCrc = string.IsNullOrEmpty(manifestCrc) ? (existing.ManifestCrc ?? "") : manifestCrc;
            string? nextHands;
            if (clockHands is not null)
                nextHands = clockHands;
            else if (!string.Equals(existing.ManifestCrc, nextCrc, StringComparison.OrdinalIgnoreCase))
                nextHands = null;
            else
                nextHands = existing.ClockHands;

            // Drop obsolete local files when the extension changes.
            if (IsSuccessfulIconExt(existing.IconExt)
                && IsSuccessfulIconExt(nextIcon)
                && !string.Equals(existing.IconExt, nextIcon, StringComparison.OrdinalIgnoreCase))
            {
                var oldPath = GetLocalIconPath(serial, packageName, existing.IconExt);
                try { if (File.Exists(oldPath)) File.Delete(oldPath); } catch { /* ignore */ }
            }

            cache[packageName] = new ApkIconCacheEntry(nextCrc, today, nextIcon, nextLabel, nextHands);
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

    private const int IconApkRankBase = 35;

    /// <summary>
    /// Adaptive layer size in dp (full bleed including mask padding).
    /// </summary>
    private const float AdaptiveIconLayerDp = 108f;

    /// <summary>
    /// Launcher-visible viewport in dp (<c>AdaptiveIconDrawable</c> uses 72 = 108×2/3).
    /// </summary>
    private const float AdaptiveIconViewportDp = 72f;

    private static async Task SaveBitmapAsPngAsync(BitmapSource bitmap, string path, CancellationToken cancellationToken)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        await using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        encoder.Save(fs);
        await fs.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    [GeneratedRegex(@"^\s*(?<Length>\d+)\s+\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}\s+(?<Name>.+\S)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex UnzipListEntryLine();
}
