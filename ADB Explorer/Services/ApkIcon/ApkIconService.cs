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

    private static readonly Encoding CsvEncoding = new UTF8Encoding(true);
    private static readonly ConcurrentDictionary<string, object> DeviceLocks = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Dictionary<string, ApkIconCacheEntry>> DeviceCaches = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, byte> ClockHandsPersistInFlight = new(StringComparer.Ordinal);
    private static readonly SKSamplingOptions PixelCopySampling = new(SKFilterMode.Nearest, SKMipmapMode.None);
    private static readonly SKSamplingOptions ScaleSampling = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    public static event Action<string, string>? ApkIconUpdated;

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

    private static readonly AsyncLocal<ApkIconExtractSession?> CurrentExtractSession = new();

    /// <summary>
    /// Queue ordering for APK icon/label loads. Higher values are dequeued first.
    /// </summary>
    public enum ApkLoadPriority : byte
    {
        Background = 0,
        Visible = 1,
        Selected = 2,
    }

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
        , ApkLoadTiming? timing = null
        )
    {
        if (device is null || string.IsNullOrEmpty(apkPath) || !CanLoadOnDevice(device.ID))
        {
            Mark(timing, "BeginLoad aborted (device/path/disabled)");
            onReady?.Invoke(null);
            return;
        }

        if (Data.DeviceCts.IsCancellationRequested)
        {
            Mark(timing, "BeginLoad aborted (cancelled)");
            onReady?.Invoke(null);
            return;
        }

        if (IsLoadingStopped)
        {
            Mark(timing, "BeginLoad aborted (loading stopped)");
            onReady?.Invoke(null);
            return;
        }

        packageName ??= TryResolvePackageName(apkPath);
        if (!IsListedPackageApk(packageName, apkPath))
        {
            Mark(timing, "BeginLoad aborted (not listed package APK)");
            BitmapSource? cached = null;
            if (!string.IsNullOrEmpty(packageName))
                cached = TryGetStoredIcon(device, packageName);

            onReady?.Invoke(cached);
            return;
        }

        if (!string.IsNullOrEmpty(packageName))
        {
            var cached = TryGetCachedIcon(device, packageName);
            if (cached is not null)
            {
                Mark(timing, "cache hit (unexpected during force reload)");
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
                Mark(timing, "attached to in-flight/queued request");
                AttachOnReady(pullKey, packageName, onReady, priority);
                return;
            }
        }

        if (upgradeLabelOnly)
        {
            Mark(timing, "upgrading pending label-only → icon load");
            UpgradeLabelOnlyToIcon(labelKey!, pullKey, device, apkPath, packageName, onReady, priority, timing);
            return;
        }

        var request = new LoadRequest(pullKey, device, apkPath, packageName, onReady, LabelOnly: false, priority, Quiet: quiet)
        {
            Timing = timing,
        }
        ;
        Enqueue(request, priority);
    }

    public static void BeginLoadForFile(FileClass file, ApkLoadPriority priority = ApkLoadPriority.Background)
    {
        if (file is null || !file.IsApk || file.ApkIcon is not null || !IsEnabled)
            return;

        var package = TryResolvePackage(file.FullPath);
        var icon = package?.Icon;
        if (icon is null)
            return;

        // Icon bindings call this from a getter — apply after the current layout pass.
        App.SafeBeginInvoke(() =>
        {
            if (file.ApkIcon is null)
                file.ApplyApkIcon(icon);
        });
    }

    public static void BeginLoadForPackage(Package package, ApkLoadPriority priority = ApkLoadPriority.Background)
    {
        if (package is null || string.IsNullOrEmpty(package.Path) || !IsEnabled)
        {
            if (package is not null)
                package.IconLoadCompleted = true;
            return;
        }

        if (Data.ActiveDevice is not { } device)
        {
            package.IconLoadCompleted = true;
            return;
        }

        package.DeviceSerial ??= device.SerialNumber;

        if (!Data.FileActions.IsAppDrive)
        {
            ApplyCacheToPackages([package]);
            return;
        }

        if (!CanLoadOnDevice(device.ID))
        {
            package.IconLoadCompleted = true;
            return;
        }

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
            if (Data.ActiveDevice?.SerialNumber != device.SerialNumber)
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

        if (Data.ActiveDevice is not { } device)
            return;

        ApplyCachedLabel(device, package);
        if (!Data.FileActions.IsAppDrive || !CanLoadOnDevice(device.ID))
            return;
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
                        if (Data.ActiveDevice?.SerialNumber != device.SerialNumber)
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
                    if (Data.ActiveDevice?.SerialNumber != device.SerialNumber)
                        return;
                    ApplyCachedLabel(device, package);
                }, priority);
                return;
            }
        }

        Enqueue(new LoadRequest(pullKey, device, package.Path, package.Name, _ =>
        {
            if (Data.ActiveDevice?.SerialNumber != device.SerialNumber)
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

        if (Data.ActiveDevice is not { } device || !CanLoadOnDevice(device.ID))
            return;

        // Single path per package: icon load fetches the label; label-only only when icon is done.
        foreach (var package in packages)
            BeginLoadForPackage(package, ApkLoadPriority.Background);
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

        if (Data.ActiveDevice is not { } device || !CanLoadOnDevice(device.ID))
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
                        if (Data.ActiveDevice?.SerialNumber == device.SerialNumber)
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
                    if (Data.ActiveDevice?.SerialNumber == device.SerialNumber)
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
        while (AdbService.IsCommandActive && DateTime.UtcNow < deadline)
            Thread.Sleep(50);

        if (AdbService.IsCommandActive)
            timing.Mark($"adb drain timeout after {(int)timeout.TotalSeconds}s (still active)");
        else
            timing.Mark("adb drain complete");
    }
#endif

    private static string LabelPullKey(string serial, string packageName)
        => $"{serial}|{packageName}|label";

    /// <summary>
    /// Looks up an installed package for <paramref name="apkPath"/>: the listed base APK,
    /// or another APK in the same install directory (split configs).
    /// Uses a string parent (not <see cref="FileHelper.GetParentPath"/>) so <c>.apk</c>
    /// archive detection cannot issue a device <c>stat</c> on the UI thread.
    /// </summary>
    public static string? TryResolvePackageName(string apkPath)
        => TryResolvePackage(apkPath)?.Name;

    private static Package? TryResolvePackage(string apkPath)
    {
        if (string.IsNullOrEmpty(apkPath) || Data.Packages is null || Data.Packages.Count == 0)
            return null;

        var parent = GetUnixParentPath(apkPath);
        foreach (var package in Data.Packages)
        {
            if (string.IsNullOrEmpty(package.Path))
                continue;

            if (string.Equals(package.Path, apkPath, StringComparison.Ordinal))
                return package;

            if (parent is not null
                && string.Equals(GetUnixParentPath(package.Path), parent, StringComparison.Ordinal))
                return package;
        }

        return null;
    }

    private static string? GetUnixParentPath(string path)
    {
        var end = path.Length;
        while (end > 1 && path[end - 1] == '/')
            end--;

        var slash = path.LastIndexOf('/', end - 1);
        if (slash < 0)
            return null;
        if (slash == 0)
            return "/";

        return path[..slash];
    }

    /// <summary>
    /// True when <paramref name="apkPath"/> is the <c>pm</c>-listed APK for
    /// <paramref name="packageName"/>. Split/sibling APKs must not unzip into the shared cache.
    /// </summary>
    private static bool IsListedPackageApk(string? packageName, string apkPath)
    {
        if (string.IsNullOrEmpty(packageName) || string.IsNullOrEmpty(apkPath) || Data.Packages is not { Count: > 0 })
            return false;

        foreach (var package in Data.Packages)
        {
            if (string.Equals(package.Name, packageName, StringComparison.Ordinal)
                && string.Equals(package.Path, apkPath, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
