using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.ViewModels;
using AlphaOmega.Debug;
using AlphaOmega.Debug.Manifest;
using SkiaSharp;

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
/// throttled (same as thumbnail pulls), otherwise up to <see cref="AppSettings.MaxSimultaneousOps"/>.
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

    /// <summary>Raised after each queued icon attempt so the progress UI can keep its timeout alive.</summary>
    public static event Action? IconLoadProgressTick;

    private static int ProgressActiveCount;

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

    private sealed class LoadRequest(
        string PullKey,
        LogicalDeviceViewModel Device,
        string ApkPath,
        string? PackageName,
        Action<BitmapSource?>? OnReady,
        bool LabelOnly = false)
    {
        public string PullKey { get; } = PullKey;
        public LogicalDeviceViewModel Device { get; } = Device;
        public string ApkPath { get; } = ApkPath;
        public string? PackageName { get; set; } = PackageName;
        public Action<BitmapSource?>? OnReady { get; set; } = OnReady;
        public bool LabelOnly { get; } = LabelOnly;
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
    public static void CancelPending()
    {
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
    }

    public static void BeginLoad(
        LogicalDeviceViewModel device,
        string apkPath,
        string? packageName = null,
        Action<BitmapSource?>? onReady = null,
        bool priority = false)
    {
        if (device is null || string.IsNullOrEmpty(apkPath) || !CanLoadOnDevice(device.ID))
        {
            onReady?.Invoke(null);
            return;
        }

        if (Data.DeviceCts.IsCancellationRequested)
        {
            onReady?.Invoke(null);
            return;
        }

        packageName ??= TryResolvePackageName(apkPath);
        if (!string.IsNullOrEmpty(packageName))
        {
            var cached = TryGetCachedIcon(device, packageName);
            if (cached is not null)
            {
                onReady?.Invoke(cached);
                return;
            }
        }

        // Dedupe by package when known so app-drive and file-path loads share one pull.
        var pullKey = $"{device.SerialNumber}|{packageName ?? apkPath}";
        lock (PendingLock)
        {
            if (!PendingLoads.Add(pullKey))
            {
                AttachOnReady(pullKey, packageName, onReady, priority);
                return;
            }
        }

        Enqueue(new LoadRequest(pullKey, device, apkPath, packageName, onReady), priority);
    }

    public static void BeginLoadForFile(FileClass file, bool priority = false)
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

    public static void BeginLoadForPackage(Package package, bool priority = false)
    {
        if (package is null || string.IsNullOrEmpty(package.Path) || !IsEnabled)
            return;

        if (Data.DevicesObject?.Current is not { } device || !CanLoadOnDevice(device.ID))
            return;

        if (package.Icon is not null)
        {
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
                ApplyCachedLabel(device, package);
                BeginEnsureLabelForPackage(package, priority);
                return;
            }
        }

        BeginLoad(device, package.Path, package.Name, bmp =>
        {
            if (Data.DevicesObject?.Current?.SerialNumber != device.SerialNumber)
                return;

            ApplyCachedLabel(device, package);
            if (bmp is not null)
                package.Icon = bmp;

            BeginEnsureLabelForPackage(package, priority);
        }, priority);
    }

    /// <summary>
    /// Fetches the package label when missing, even if the icon is already cached/displayed.
    /// </summary>
    public static void BeginEnsureLabelForPackage(Package package, bool priority = false)
    {
        if (package is null || string.IsNullOrEmpty(package.Path) || string.IsNullOrEmpty(package.Name))
            return;

        if (Data.DevicesObject?.Current is not { } device || !CanLoadOnDevice(device.ID))
            return;

        ApplyCachedLabel(device, package);
        // Missing locale for the current UI language must re-fetch even if another locale is cached.
        if (!NeedsLabelFetch(device, package.Name))
            return;

        var pullKey = $"{device.SerialNumber}|{package.Name}|label";
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
        }, LabelOnly: true), priority);
    }

    /// <summary>
    /// Queues icon loads for packages not yet requested (visible tiles use <paramref name="priority"/> via
    /// <see cref="PackageIconViewModel"/> so off-screen work runs after in-view tiles).
    /// </summary>
    public static void BeginPreloadPackages(IEnumerable<Package> packages)
    {
        if (packages is null || !IsEnabled)
            return;

        if (Data.DevicesObject?.Current is not { } device || !CanLoadOnDevice(device.ID))
            return;

        // Labels first so names appear without waiting for every icon pull.
        foreach (var package in packages)
            BeginEnsureLabelForPackage(package, priority: false);

        foreach (var package in packages)
        {
            if (package.Icon is not null)
                continue;

            BeginLoadForPackage(package, priority: false);
        }
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

    /// <summary>True when the package already shows a usable name for the current UI language.</summary>
    private static bool IsUsableCachedLabel(string? label) => IsUsableDisplayLabel(label);

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
            BeginEnsureLabelForPackage(package, priority: false);
        }
    }

    private static bool IsSuccessfulIconExt(string? iconExt)
        => !string.IsNullOrEmpty(iconExt)
           && iconExt != FailMarker
           && iconExt.StartsWith('.');

    private static void AttachOnReady(string pullKey, string? packageName, Action<BitmapSource?>? onReady, bool priority)
    {
        if (onReady is null)
            return;

        lock (QueueLock)
        {
            foreach (var request in LoadQueue)
            {
                if (request.PullKey != pullKey)
                    continue;

                if (string.IsNullOrEmpty(request.PackageName) && !string.IsNullOrEmpty(packageName))
                    request.PackageName = packageName;

                var previous = request.OnReady;
                request.OnReady = bmp =>
                {
                    previous?.Invoke(bmp);
                    onReady(bmp);
                };

                if (priority && LoadQueue.First?.Value != request)
                {
                    LoadQueue.Remove(request);
                    LoadQueue.AddFirst(request);
                }

                return;
            }
        }

        void Handler(string serial, string cachedPackageName)
        {
            var expected = pullKey.Split('|', 2);
            if (expected.Length != 2 || serial != expected[0])
                return;

            // Match by package name, or by apk path used as interim pull key.
            if (!string.Equals(cachedPackageName, expected[1], StringComparison.Ordinal)
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

    private static void Enqueue(LoadRequest request, bool priority)
    {
        lock (QueueLock)
        {
            if (priority)
                LoadQueue.AddFirst(request);
            else
                LoadQueue.AddLast(request);

            if (WorkerRunning)
                return;

            StartWorker_NoLock();
        }
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
    {
        if (force)
        {
            Interlocked.Exchange(ref ProgressActiveCount, 0);
            try { IconLoadProgressChanged?.Invoke(false); } catch { /* ignore */ }
            return;
        }

        if (active)
        {
            if (Interlocked.CompareExchange(ref ProgressActiveCount, 1, 0) == 0)
            {
                try { IconLoadProgressChanged?.Invoke(true); } catch { /* ignore */ }
            }
            else
            {
                Interlocked.Exchange(ref ProgressActiveCount, 1);
            }
        }
        else
        {
            Interlocked.Exchange(ref ProgressActiveCount, 0);
            try { IconLoadProgressChanged?.Invoke(false); } catch { /* ignore */ }
        }
    }

    private static int GetMaxConcurrentLoads()
        => Data.Settings.LimitThumbsPullSpeed
            ? 1
            : Math.Clamp(Data.Settings.MaxSimultaneousOps, 1, AppSettings.MaxSimultaneousOpsMax);

    private static async Task ProcessQueueAsync(int generation, CancellationToken workerToken)
    {
        SetIconLoadProgress(true);
        var inFlight = new List<Task>();
        try
        {
            while (!workerToken.IsCancellationRequested)
            {
                if (generation != WorkerGeneration)
                    return;

                inFlight.RemoveAll(static t => t.IsCompleted);

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

                    inFlight.Add(ProcessRequestAsync(request, generation, workerToken));
                }

                if (inFlight.Count == 0)
                {
                    lock (QueueLock)
                    {
                        if (generation != WorkerGeneration)
                            return;

                        // New work may have arrived while we held no lock.
                        if (LoadQueue.Count > 0)
                            continue;

                        WorkerRunning = false;
                        SetIconLoadProgress(false);
                        return;
                    }
                }

                await Task.WhenAny(inFlight).ConfigureAwait(false);
            }
        }
        finally
        {
            if (inFlight.Count > 0)
            {
                try { await Task.WhenAll(inFlight).ConfigureAwait(false); }
                catch { /* per-request failures are handled inside ProcessRequestAsync */ }
            }

            lock (QueueLock)
            {
                if (generation == WorkerGeneration)
                {
                    WorkerRunning = false;
                    if (LoadQueue.Count > 0 && !Data.DeviceCts.IsCancellationRequested)
                        StartWorker_NoLock();
                    else
                        SetIconLoadProgress(false);
                }
            }
        }
    }

    private static async Task ProcessRequestAsync(
        LoadRequest request,
        int generation,
        CancellationToken workerToken)
    {
        BitmapSource? result = null;
        string? resolvedPackageName = request.PackageName;
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(workerToken, Data.DeviceCts.Token);
            if (request.LabelOnly)
            {
                resolvedPackageName = await LoadLabelAsync(
                    request.Device, request.ApkPath, request.PackageName, linked.Token).ConfigureAwait(false);
                result = null;
            }
            else
            {
                (result, resolvedPackageName) = await LoadIconAsync(
                    request.Device, request.ApkPath, request.PackageName, linked.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            result = null;
        }
        catch (Exception e)
        {
#if !DEPLOY
            DebugLog.PrintLine($"APK icon load failed for {request.ApkPath}: {e.Message}");
#endif
        }
        finally
        {
            lock (PendingLock)
                PendingLoads.Remove(request.PullKey);
        }

        if (generation != WorkerGeneration
            || workerToken.IsCancellationRequested
            || Data.DeviceCts.IsCancellationRequested)
        {
            return;
        }

        if (result is not null && !string.IsNullOrEmpty(resolvedPackageName))
            ApkIconUpdated?.Invoke(request.Device.SerialNumber, resolvedPackageName);
        else if (request.LabelOnly && !string.IsNullOrEmpty(resolvedPackageName))
            ApkIconUpdated?.Invoke(request.Device.SerialNumber, resolvedPackageName);

        IconLoadProgressTick?.Invoke();

        var onReady = request.OnReady;
        var bitmap = result;
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
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var serial = device.SerialNumber;
        var deviceId = device.ID;
        var today = DateOnly.FromDateTime(DateTime.Today);

        packageName ??= TryResolvePackageName(apkPath);
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
                        return (DecodeBitmap(warmPath), packageName);
                }

                if (cache.TryGetValue(packageName, out warm)
                    && warm.CheckedDate == today
                    && warm.IconExt == FailMarker)
                {
                    return (null, packageName);
                }
            }
        }

        // Manifest CRC listing in parallel with pulling AndroidManifest.xml + resources.arsc.
        var manifestListingTask = Task.Run(
            () => ArchiveListing.FetchZipMemberListing(deviceId, apkPath, [MANIFEST], cancellationToken),
            cancellationToken);
        var metaTask = PullManifestAndResourcesAsync(device, apkPath, cancellationToken);

        await Task.WhenAll(manifestListingTask, metaTask).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var manifestEntry = FindEntry(manifestListingTask.Result, MANIFEST);
        if (manifestEntry is null || string.IsNullOrEmpty(manifestEntry.Value.Crc))
        {
            if (!string.IsNullOrEmpty(packageName))
                MarkFetchResult(serial, packageName, "", today, FailMarker, FailMarker);
            return (null, packageName);
        }

        var manifestCrc = NormalizeCrc(manifestEntry.Value.Crc);
        var (manifestBytes, resourcesBytes) = metaTask.Result;
        if (manifestBytes is null || manifestBytes.Length == 0 || resourcesBytes is null || resourcesBytes.Length == 0)
        {
            if (!string.IsNullOrEmpty(packageName))
                MarkFetchResult(serial, packageName, manifestCrc, today, FailMarker, FailMarker);
            return (null, packageName);
        }

        packageName ??= TryReadPackageName(manifestBytes, resourcesBytes);
        if (string.IsNullOrEmpty(packageName))
            return (null, null);

        var label = TryReadPackageLabel(manifestBytes, resourcesBytes) ?? FailMarker;

        lock (GetDeviceLock(serial))
        {
            var cache = GetOrLoadCache(serial);
            if (cache.TryGetValue(packageName, out var existing)
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

                    return (DecodeBitmap(localPath), packageName);
                }
            }
        }

        var iconCandidates = await ResolveIconCandidatesAsync(
            device, apkPath, manifestBytes, resourcesBytes, cancellationToken).ConfigureAwait(false);

        var apkFiles = DiscoverApkBundleFiles(deviceId, apkPath);
        byte[] effectiveResources = resourcesBytes;

        if (iconCandidates.Count == 0 && apkFiles.Count > 1)
        {
            foreach (var splitApk in PreferApksForRead(apkFiles, apkPath).Where(p => !string.Equals(p, apkPath, StringComparison.Ordinal)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var splitResources = await PullResourcesOnlyAsync(device, splitApk, cancellationToken).ConfigureAwait(false);
                if (splitResources is null || splitResources.Length == 0)
                    continue;

                var splitCandidates = await ResolveIconCandidatesAsync(
                    device, splitApk, manifestBytes, splitResources, cancellationToken).ConfigureAwait(false);
                if (splitCandidates.Count == 0)
                    continue;

                iconCandidates = splitCandidates;
                effectiveResources = splitResources;
                break;
            }
        }

        if (iconCandidates.Count == 0)
        {
            // Splits exhausted (or single APK) — brand / string-pool / heuristics are safe now.
            iconCandidates = FindFallbackBrandIconPaths(effectiveResources);
            if (iconCandidates.Count == 0)
                iconCandidates = FindLikelyIconPathsInStringPool(new ArscFile(effectiveResources));
            if (iconCandidates.Count == 0)
                iconCandidates = HeuristicIconCandidates();
        }

        // Adaptive wrappers often live only in a density split — always probe common paths.
        iconCandidates =
        [
            "res/mipmap-anydpi-v26/ic_launcher.xml",
            "res/drawable-anydpi-v26/ic_launcher.xml",
            "res/mipmap-anydpi-v26/ic_launcher_round.xml",
            "res/drawable-anydpi-v26/ic_launcher_round.xml",
            .. iconCandidates,
        ];

        string? iconMember = null;
        var iconSourceApk = apkPath;
        if (iconCandidates.Count > 0)
        {
            if (iconCandidates.Count > MaxIconCandidatesToProbe)
                iconCandidates = iconCandidates.Take(MaxIconCandidatesToProbe).ToList();

            foreach (var candidateApk in PreferApksForRead(apkFiles, apkPath))
            {
                var candidateListing = ArchiveListing.FetchZipMemberListing(
                    deviceId, candidateApk, iconCandidates, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                iconMember = PickBestIconMember(iconCandidates, candidateListing);
                if (iconMember is not null)
                {
                    iconSourceApk = candidateApk;
                    break;
                }
            }
        }

        if (iconMember is null)
        {
            foreach (var candidateApk in PreferApksForRead(apkFiles, apkPath))
            {
                var discovered = DiscoverIconMembersOnDevice(deviceId, candidateApk, cancellationToken);
                if (discovered.Count == 0)
                    continue;

                // Keep adaptive XML — RankIconCandidates is raster-only and would drop wrappers.
                iconCandidates = RankDiscoveredIconCandidates(discovered.Select(e => e.Path));
                iconMember = PickBestIconMember(iconCandidates, discovered);
                if (iconMember is not null)
                {
                    iconSourceApk = candidateApk;
                    break;
                }
            }
        }

        if (iconMember is null)
        {
            MarkFetchResult(serial, packageName, manifestCrc, today, FailMarker, label);
            return (null, packageName);
        }

        await using var iconStream = await AdbHelper.ReadFileAsStreamAsync(
            device, FileHelper.ConcatPaths(iconSourceApk, iconMember), cancellationToken).ConfigureAwait(false);
        if (iconStream is null || iconStream.Length == 0)
        {
            MarkFetchResult(serial, packageName, manifestCrc, today, FailMarker, label);
            return (null, packageName);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var memberBytes = ToByteArray(iconStream);
        if (memberBytes is null || memberBytes.Length == 0)
        {
            MarkFetchResult(serial, packageName, manifestCrc, today, FailMarker, label);
            return (null, packageName);
        }

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

                xmlCache = await PreloadXmlResourcesAsync(
                    device, apkBundle, composeResources, memberBytes, cancellationToken).ConfigureAwait(false);
                return xmlCache;
            }

            Func<int, byte[]?> ResolveXml = id =>
                xmlCache is not null && xmlCache.TryGetValue(id, out var bytes) ? bytes : null;

            if (ApkVectorIconRenderer.IsVectorDrawable(memberBytes))
            {
                xmlCache = await PreloadXmlResourcesAsync(
                    device, apkBundle, resourcesForIcon, memberBytes, cancellationToken).ConfigureAwait(false);

                using var rendered = ApkVectorIconRenderer.TryRenderToSkBitmap(
                    memberBytes, size: 192, background: SKColors.Transparent,
                    resolveColor: ResolveColor, resolveXmlResource: ResolveXml);
                if (rendered is not null && !IsDegenerateIcon(rendered))
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
                }
            }
            else
            {
                await GetXmlCacheAsync().ConfigureAwait(false);
                bitmap = await TryComposeAdaptiveIconAsync(
                    device, apkBundle, memberBytes, composeArsc, composeResources, cancellationToken,
                    ResolveXml, packageName).ConfigureAwait(false);

                // If adaptive composition fails, fall back to the best foreground raster
                // composited on white (Android adaptive icons are always opaque).
                if (bitmap is null)
                {
                    var layers = ResolveAdaptiveLayers(memberBytes, composeArsc, composeResources);
                    var fgOnly = RankIconCandidates(layers.ForegroundImages);
                    if (fgOnly.Count == 0)
                    {
                        // AccuBattery / El Al: adaptive layers unresolved — use mipmap rasters from icon ref.
                        var iconRef = AxmlManifestReader.TryGetApplicationAttribute(manifestBytes, AxmlManifestReader.AttrIcon)
                                      ?? FindApplicationAttributeFromAxml(manifestBytes, "icon");
                        if (!string.IsNullOrWhiteSpace(iconRef))
                            fgOnly = RankIconCandidates(ResolveIconRefToPaths(iconRef, composeArsc, composeResources));
                    }

                    if (fgOnly.Count > 0)
                    {
                        foreach (var candidateApk in apkBundle)
                        {
                            var fgListing = ArchiveListing.FetchZipMemberListing(
                                deviceId, candidateApk, fgOnly.Take(MaxIconCandidatesToProbe).ToList(), cancellationToken);
                            var fgMember = PickBestIconMember(fgOnly, fgListing);
                            if (fgMember is null)
                                continue;

                            await using var fgStream = await AdbHelper.ReadFileAsStreamAsync(
                                device, FileHelper.ConcatPaths(candidateApk, fgMember), cancellationToken).ConfigureAwait(false);
                            var fgBytes = ToByteArray(fgStream);
                            if (fgBytes is null || fgBytes.Length == 0)
                                continue;

                            using var fgSk = DecodeSkBitmap(fgBytes);
                            if (fgSk is null)
                                continue;

                            // Prefer opaque white plate under transparent adaptive foregrounds
                            // (healthdata / Google One) over writing a raw transparent PNG.
                            var bgColor = layers.BackgroundColor ?? SKColors.White;
                            bitmap = CompositeOnOpaqueBackground(fgSk, 192, bgColor);
                            if (bitmap is not null)
                                break;

                            memberBytes = fgBytes;
                            iconMember = fgMember;
                            writeRawRaster = true;
                            break;
                        }
                    }
                }
            }

            // RedAlert / density-split apps: adaptive fg is layer-list with split-only rasters.
            if (bitmap is null && !writeRawRaster)
            {
                // Probe enough candidates — mipmap-* paths often miss while drawable-xxhdpi hits.
                const int heuristicProbe = 48;
                var heuristic = HeuristicIconCandidates();
                foreach (var candidateApk in PreferApksForRead(apkBundle, apkPath))
                {
                    var listing = ArchiveListing.FetchZipMemberListing(
                        deviceId, candidateApk, heuristic.Take(heuristicProbe).ToList(), cancellationToken);
                    var member = PickBestIconMember(heuristic, listing);
                    if (member is null)
                        continue;

                    await using var stream = await AdbHelper.ReadFileAsStreamAsync(
                        device, FileHelper.ConcatPaths(candidateApk, member), cancellationToken).ConfigureAwait(false);
                    var bytes = ToByteArray(stream);
                    if (bytes is null || bytes.Length == 0)
                        continue;

                    using var sk = DecodeSkBitmap(bytes);
                    if (sk is null || IsDegenerateIcon(sk))
                        continue;

                    bitmap = CompositeOnOpaqueBackground(sk, 192, SKColors.White);
                    if (bitmap is not null)
                        break;

                    memberBytes = bytes;
                    iconMember = member;
                    writeRawRaster = true;
                    break;
                }
            }

            if (bitmap is null && !writeRawRaster)
            {
                MarkFetchResult(serial, packageName, manifestCrc, today, FailMarker, label);
                return (null, packageName);
            }

            iconExt = writeRawRaster
                ? (Path.GetExtension(iconMember) is { Length: > 0 } ext ? ext : ".png")
                : ".png";
        }
        else
        {
            iconExt = Path.GetExtension(iconMember);
            if (string.IsNullOrEmpty(iconExt))
                iconExt = ".png";
            writeRawRaster = true;
        }

        var localDir = GetLocalIconDirectory(serial);
        Directory.CreateDirectory(localDir);
        var localFile = GetLocalIconPath(serial, packageName, iconExt);

        if (bitmap is not null && !writeRawRaster)
        {
            await SaveBitmapAsPngAsync(bitmap, localFile, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // Upscale tiny system rasters (ANGLE 48×48) so list thumbnails are not muddy.
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
                MarkFetchResult(serial, packageName, manifestCrc, today, FailMarker, label);
                return (null, packageName);
            }

            bitmap = DecodeBitmap(localFile);
            if (bitmap is null)
            {
                try { File.Delete(localFile); } catch { /* ignore */ }
                MarkFetchResult(serial, packageName, manifestCrc, today, FailMarker, label);
                return (null, packageName);
            }

            // Reject near-solid white / empty rasters (adaptive fg-only leftovers, etc.).
            using (var sk = DecodeSkBitmap(memberBytes))
            {
                if (sk is not null && IsDegenerateIcon(sk))
                {
                    try { File.Delete(localFile); } catch { /* ignore */ }
                    MarkFetchResult(serial, packageName, manifestCrc, today, FailMarker, label);
                    return (null, packageName);
                }
            }
        }

        MarkFetchResult(serial, packageName, manifestCrc, today, iconExt, label);
        return (bitmap, packageName);
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
        CancellationToken cancellationToken)
    {
        string? stagingRoot = null;
        try
        {
            var (root, contentRoot) = await Task.Run(
                () => ArchiveExtract.ExtractZipMembersToStaging(
                    device.ID, apkPath, [MANIFEST, RESOURCES], cancellationToken),
                cancellationToken).ConfigureAwait(false);
            stagingRoot = root;

            var manifestTask = AdbHelper.ReadFileAsStreamAsync(
                device, FileHelper.ConcatPaths(contentRoot, MANIFEST), cancellationToken);
            var resourcesTask = AdbHelper.ReadFileAsStreamAsync(
                device, FileHelper.ConcatPaths(contentRoot, RESOURCES), cancellationToken);

            await Task.WhenAll(manifestTask, resourcesTask).ConfigureAwait(false);

            return (ToByteArray(manifestTask.Result), ToByteArray(resourcesTask.Result));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
#if !DEPLOY
            DebugLog.PrintLine($"APK meta pull failed for {apkPath}: {e.Message}");
#endif
            return (null, null);
        }
        finally
        {
            if (stagingRoot is not null)
                ArchiveExtract.CleanupStaging(device.ID, stagingRoot, CancellationToken.None);
        }
    }

    /// <summary>
    /// Sibling APKs in an app install dir (<c>base.apk</c> + <c>split_config.*dpi*.apk</c>).
    /// </summary>
    private static List<string> DiscoverApkBundleFiles(string deviceId, string apkPath)
    {
        var result = new List<string> { apkPath };
        try
        {
            var parent = FileHelper.GetParentPath(apkPath);
            if (string.IsNullOrEmpty(parent))
                return result;

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
    /// Prefer higher-density config splits, then base.apk, then others.
    /// </summary>
    private static List<string> PreferApksForRead(IReadOnlyList<string> apkFiles, string baseApk)
    {
        return apkFiles
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(DensitySplitRank)
            .ThenBy(p => string.Equals(p, baseApk, StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(p => p, StringComparer.Ordinal)
            .ToList();
    }

    private static int DensitySplitRank(string apkPath)
    {
        var name = Path.GetFileName(apkPath).ToLowerInvariant();
        if (name.Contains("xxxhdpi")) return 5;
        if (name.Contains("xxhdpi")) return 4;
        if (name.Contains("xhdpi")) return 3;
        if (name.Contains("hdpi")) return 2;
        if (name.Contains("mdpi")) return 1;
        if (name.Contains("split_config") || name.Contains("config.")) return 0;
        return -1; // base / unknown
    }

    private static async Task<byte[]?> PullResourcesOnlyAsync(
        LogicalDeviceViewModel device,
        string apkPath,
        CancellationToken cancellationToken)
    {
        string? stagingRoot = null;
        try
        {
            var (root, contentRoot) = await Task.Run(
                () => ArchiveExtract.ExtractZipMembersToStaging(
                    device.ID, apkPath, [RESOURCES], cancellationToken),
                cancellationToken).ConfigureAwait(false);
            stagingRoot = root;

            await using var stream = await AdbHelper.ReadFileAsStreamAsync(
                device, FileHelper.ConcatPaths(contentRoot, RESOURCES), cancellationToken).ConfigureAwait(false);
            return ToByteArray(stream);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (stagingRoot is not null)
                ArchiveExtract.CleanupStaging(device.ID, stagingRoot, CancellationToken.None);
        }
    }

    private static async Task<byte[]?> ReadMemberFromBundleAsync(
        LogicalDeviceViewModel device,
        IReadOnlyList<string> apkFiles,
        string member,
        CancellationToken cancellationToken)
    {
        member = ArchivePath.NormalizeInternal(member);
        foreach (var apk in apkFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var stream = await AdbHelper.ReadFileAsStreamAsync(
                    device, FileHelper.ConcatPaths(apk, member), cancellationToken).ConfigureAwait(false);
                var bytes = ToByteArray(stream);
                if (bytes is { Length: > 0 })
                    return bytes;
            }
            catch
            {
                // try next apk
            }
        }

        return null;
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

    private static async Task<List<string>> ResolveIconCandidatesAsync(
        LogicalDeviceViewModel device,
        string apkPath,
        byte[] manifestBytes,
        byte[] resourcesBytes,
        CancellationToken cancellationToken)
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
                return FindLikelyIconPathsInStringPool(arsc);

            // Resolve the manifest icon id only — do not fall back to string-pool or brand
            // guesses here. Density-split APKs often own the real adaptive wrapper while the
            // base arsc lists the id as INVALID (Zoom). Brand key hints like "gs_" previously
            // matched inside "settings_*" and returned notification dots before splits ran.
            var paths = ResolveIconRefToPathsStrict(iconRef, arsc, resourcesBytes);
            if (paths.Count == 0)
                return [];

            // Adaptive wrappers (anydpi / *launcher* XML) before density rasters.
            var adaptivePreferred = paths
                .Where(IsAdaptiveWrapperPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var xmlMember in adaptivePreferred)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await using var xmlStream = await AdbHelper.ReadFileAsStreamAsync(
                    device, FileHelper.ConcatPaths(apkPath, xmlMember), cancellationToken).ConfigureAwait(false);
                var xmlBytes = ToByteArray(xmlStream);
                if (xmlBytes is null || xmlBytes.Length == 0)
                    continue;

                if (ApkVectorIconRenderer.IsAdaptiveIcon(xmlBytes))
                    return [xmlMember];
            }

            // Prefer pre-rendered density rasters (bit / AccuBattery) over distorting vectors.
            // Skip bare *_background layers (Strava pride themes).
            var rasters = RankIconCandidates(paths.Where(p =>
                !p.Contains("_background.", StringComparison.OrdinalIgnoreCase)
                && !p.Contains("_background_", StringComparison.OrdinalIgnoreCase)));
            if (rasters.Count > 0)
                return rasters;

            // Any remaining XML from the icon ref (including obfuscated names like res/qq.xml).
            var xmlMembers = paths
                .Where(p => p.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var xmlMember in xmlMembers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await using var xmlStream = await AdbHelper.ReadFileAsStreamAsync(
                    device, FileHelper.ConcatPaths(apkPath, xmlMember), cancellationToken).ConfigureAwait(false);
                var xmlBytes = ToByteArray(xmlStream);
                if (xmlBytes is null || xmlBytes.Length == 0)
                    continue;

                // Keep the adaptive wrapper — never return bare foreground vectors (white-on-transparent).
                if (ApkVectorIconRenderer.IsAdaptiveIcon(xmlBytes))
                    return [xmlMember];

                if (ApkVectorIconRenderer.IsVectorDrawable(xmlBytes))
                    return [xmlMember];
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

    private static List<string> ResolveIconRefToPaths(string iconRef, ArscFile arsc, byte[] resourcesBytes)
    {
        var paths = ResolveIconRefToPathsStrict(iconRef, arsc, resourcesBytes);
        if (paths.Count > 0)
            return paths;

        return FindLikelyIconPathsInStringPool(arsc);
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
    /// adaptive XML for the same id (Strava pride/road/gravel). Prefer the adaptive wrapper.
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

    private static List<string> ResolveAdaptiveIconMembers(byte[] xmlBytes, ArscFile arsc, byte[] resourcesBytes)
    {
        var layers = ResolveAdaptiveLayers(xmlBytes, arsc, resourcesBytes);
        if (layers.ForegroundImages.Count > 0)
            return layers.ForegroundImages;
        if (layers.BackgroundImages.Count > 0)
            return layers.BackgroundImages;

        return FindLikelyIconPathsInStringPool(arsc);
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
                foreach (var rawPath in GetResourcePaths(arsc, id)
                             .Concat(ArscResourceResolver.ResolvePaths(resourcesBytes, id)))
                {
                    var path = ArchivePath.NormalizeInternal(rawPath);
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

            // Prefer real rasters over animated-vector XML siblings (AccuBattery: aw vs aw.xml).
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

        // Calculator etc.: foreground is an inline <vector> under <layer-list>, plus a
        // transparent @android:color banner ref. Do not replace that with string-pool
        // "likely" rasters — those block TryRenderInlineAdaptiveLayer.
        if (fg.Images.Count == 0 && fg.Xmls.Count == 0
            && !ApkVectorIconRenderer.HasInlineVectorUnderLayer(xmlBytes, "foreground"))
        {
            var likely = FindLikelyIconPathsInStringPool(arsc);
            fg = (
                likely.Where(IsImagePath).ToList(),
                [],
                likely.Where(p => p.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)).ToList(),
                fg.Color);
        }

        return new AdaptiveLayers(fg.Images, fg.ImageLayers, fg.Xmls, bg.Images, bg.Xmls, bg.Color);
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
        var layers = ResolveAdaptiveLayers(adaptiveXmlBytes, arsc, resourcesBytes);
        const int size = 192;
        SKColor? ResolveColor(int id) => TryGetResourceColor(arsc, resourcesBytes, id);

        // Adaptive wrappers rarely carry fillColor — preload gradients from layer vectors too.
        var xmlCache = new Dictionary<int, byte[]>();
        async Task EnsureFillXmlAsync(byte[]? drawableBytes)
        {
            if (drawableBytes is null || drawableBytes.Length == 0)
                return;

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
            size, cancellationToken, ResolveColor, resolveXmlResource, resourcesBytes).ConfigureAwait(false);

        using var bgLayer = await LoadAdaptiveLayerAsync(
            device, apkFiles, layers.BackgroundImages, layers.BackgroundXmls, size, cancellationToken,
            ResolveColor, resolveXmlResource, resourcesBytes).ConfigureAwait(false);

        // Inline <vector> under <background>/<foreground> (Clock face; Calculator pad under layer-list).
        using var inlineBg = ApkVectorIconRenderer.TryRenderInlineAdaptiveLayer(
            adaptiveXmlBytes, "background", size, SKColors.Transparent, ResolveColor, resolveXmlResource);
        using var inlineFg = ApkVectorIconRenderer.TryRenderInlineAdaptiveLayer(
            adaptiveXmlBytes, "foreground", size, SKColors.Transparent, ResolveColor, resolveXmlResource);

        // Prefer inline artwork when present — drawable siblings are often transparent placeholders
        // (Calculator launcher_calculator_banner → @android:color/transparent).
        var bg = inlineBg ?? bgLayer;
        var fg = inlineFg ?? fgLayer;
        SKBitmap? overlayFg = null;

        // Deskclock live hands use <rotate>/<layer-list> which we cannot render — always synthesize 3:00.
        if (IsDeskclockPackage(packageName))
        {
            overlayFg = CreateClockHandsAtThree(size);
            fg = overlayFg;
        }

        try
        {
            // Background-only adaptive is incomplete — fall through to density rasters.
            if (fg is null && !IsDeskclockPackage(packageName))
                return null;

            if (bg is null && fg is null && layers.BackgroundColor is null)
                return null;

            using var canvasBitmap = new SKBitmap(size, size, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            using var canvas = new SKCanvas(canvasBitmap);
            canvas.Clear(layers.BackgroundColor ?? SKColors.White);

            if (bg is not null)
                canvas.DrawBitmap(bg, new SKRect(0, 0, size, size));

            if (fg is not null)
            {
                // Clock hands are already positioned; only recenter corner-biased artwork.
                if (IsDeskclockPackage(packageName) || !IsCornerBiasedIcon(fg))
                {
                    canvas.DrawBitmap(fg, new SKRect(0, 0, size, size));
                }
                else
                {
                    using var centeredFg = RecenterOpaqueContent(fg);
                    canvas.DrawBitmap(centeredFg ?? fg, new SKRect(0, 0, size, size));
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
        byte[]? resourcesBytes = null)
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
                        resolveColor, resolveXmlResource, resourcesBytes).ConfigureAwait(false);
                    if (layer is null)
                        continue;

                    // Density rasters are often 324²/432² — scale to the compose canvas first.
                    // Drawing into SKRect(0,0,192,192) on a larger bitmap pinned the date glyph
                    // into the top-left as a tiny "badge" instead of a full-size "31".
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
            xmls, size, cancellationToken, resolveColor, resolveXmlResource, resourcesBytes).ConfigureAwait(false);
    }

    private static SKBitmap? EnsureSkBitmapSize(SKBitmap source, int size)
    {
        if (source.Width == size && source.Height == size)
            return source.Copy();

        return source.Resize(new SKImageInfo(size, size), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
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
        byte[]? resourcesBytes = null)
    {
        var imageCandidates = RankIconCandidates(images);
        if (imageCandidates.Count > 0)
        {
            if (imageCandidates.Count > MaxIconCandidatesToProbe)
                imageCandidates = imageCandidates.Take(MaxIconCandidatesToProbe).ToList();

            foreach (var apkPath in apkFiles)
            {
                var listing = ArchiveListing.FetchZipMemberListing(
                    device.ID, apkPath, imageCandidates, cancellationToken);
                var member = PickBestIconMember(imageCandidates, listing);
                if (member is null)
                    continue;

                await using var stream = await AdbHelper.ReadFileAsStreamAsync(
                    device, FileHelper.ConcatPaths(apkPath, member), cancellationToken).ConfigureAwait(false);
                var bytes = ToByteArray(stream);
                if (bytes is not null && bytes.Length > 0)
                {
                    var bmp = DecodeSkBitmap(bytes);
                    if (bmp is null)
                        continue;

                    if (bmp.Width == size && bmp.Height == size)
                        return bmp;

                    var scaled = EnsureSkBitmapSize(bmp, size);
                    bmp.Dispose();
                    if (scaled is not null)
                        return scaled;
                }
            }
        }

        foreach (var xmlMember in xmls.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = await ReadMemberFromBundleAsync(device, apkFiles, xmlMember, cancellationToken).ConfigureAwait(false);
            if (bytes is null || bytes.Length == 0)
                continue;

            if (ApkVectorIconRenderer.IsVectorDrawable(bytes))
            {
                var rendered = ApkVectorIconRenderer.TryRenderToSkBitmap(
                    bytes, size, background: SKColors.Transparent, resolveColor, resolveXmlResource);
                if (rendered is not null)
                    return rendered;
            }

            // RedAlert etc.: <layer-list><item android:drawable="@…"/></layer-list>
            if (resourcesBytes is not null)
            {
                var layerListInner = await TryLoadLayerListDrawableAsync(
                    device, apkFiles, bytes, resourcesBytes, size, cancellationToken,
                    resolveColor, resolveXmlResource).ConfigureAwait(false);
                if (layerListInner is not null)
                    return layerListInner;
            }

            // Wallet etc.: <inset android:drawable="@…"/> wrapping the real vector.
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

            // Wallet etc.: insetLeft/Right/Top/Bottom are parent fractions (often ~18–26%).
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
                    foreach (var path in ArscResourceResolver.ResolvePaths(resourcesBytes, id))
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
    /// Resolves <c>android:inset*</c> to pixels. Supports complex fractions (Wallet 26%) and
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

                foreach (var path in ArscResourceResolver.ResolvePaths(resourcesBytes, id)
                             .Concat(GetResourcePaths(new ArscFile(resourcesBytes), id)))
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
    /// True when opaque ink sits in a corner rather than filling the canvas (maxcom/ambient).
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
    /// (maxcom / ambient streaming).
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
    /// True when the bitmap is empty/transparent, or a near-solid blank light tile with no real artwork.
    /// White logos on transparency (Termux) and white-bg icons with color accents (Google One) are kept.
    /// </summary>
    private static bool IsDegenerateIcon(SKBitmap bitmap)
    {
        if (bitmap.Width <= 0 || bitmap.Height <= 0)
            return true;

        var stride = bitmap.RowBytes;
        var buffer = new byte[stride * bitmap.Height];
        System.Runtime.InteropServices.Marshal.Copy(bitmap.GetPixels(), buffer, 0, buffer.Length);

        long opaque = 0, light = 0, colored = 0, dark = 0, samples = 0;
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
                if (r > 230 && g > 230 && b > 230)
                    light++;
                else if (r < 40 && g < 40 && b < 40)
                    dark++;
                else
                    colored++;
            }
        }

        if (samples == 0 || opaque * 20 < samples) // <5% opaque
            return true;

        // Real artwork: color accents, dark ink on light tiles, etc.
        if (colored >= Math.Max(3, opaque / 50)
            || dark >= Math.Max(3, opaque / 50))
            return false;

        // Near-solid light fill covering most of the canvas — blank tile.
        return opaque * 2 >= samples && light * 20 >= opaque * 19;
    }

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
        => path.Contains("ic_foreground", StringComparison.OrdinalIgnoreCase)
           || path.Contains("ic_launcher_foreground", StringComparison.OrdinalIgnoreCase)
           || path.Contains("icon_launcher", StringComparison.OrdinalIgnoreCase)
           || path.Contains("/ic_launcher.", StringComparison.OrdinalIgnoreCase)
           || path.Contains("/ic_launcher_round.", StringComparison.OrdinalIgnoreCase)
           || path.Contains("/icon.", StringComparison.OrdinalIgnoreCase)
           || path.Contains("launcher", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// When android:icon points at an empty/invalid resource, prefer product brand vectors
    /// (e.g. Contact Keys <c>gs_android_security_privacy_vd_theme_24</c>) over Material chrome.
    /// </summary>
    private static List<string> FindFallbackBrandIconPaths(byte[] resourcesBytes)
    {
        var paths = ArscResourceResolver.FindDrawablePathsByKeyHints(
            resourcesBytes,
            "security_privacy",
            "gs_shield_vd",
            "gs_encrypted_vd_theme",
            "gs_android_security");

        if (paths.Count > 0)
        {
            return paths
                .OrderBy(p => p.Contains("security_privacy", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(p => p.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ToList();
        }

        // Broader gs_*_vd drawables, excluding navigation/chrome glyphs.
        paths = ArscResourceResolver.FindDrawablePathsByKeyHints(resourcesBytes, "gs_");
        return paths
            .Where(p =>
            {
                ReadOnlySpan<string> skip =
                [
                    "chevron", "arrow", "delete", "close", "check", "keyboard", "question",
                    "sim_card", "qr_code",
                ];
                foreach (var s in skip)
                {
                    if (p.Contains(s, StringComparison.OrdinalIgnoreCase))
                        return false;
                }
                return p.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);
            })
            .Take(8)
            .ToList();
    }

    private static string? TryReadPackageLabel(byte[] manifestBytes, byte[] resourcesBytes)
    {
        try
        {
            // Prefer AlphaOmega's named android:label when present — the binary resource-map
            // walker can match a wrong early element (e.g. a settings activity label).
            // Fall back to binary AXML when AO drops the attribute (AccuBattery / El Al).
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
    /// AccuBattery and similar apps omit <c>android:label</c> on <c>&lt;application&gt;</c>
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

        // Boolean attrs misread as labels (El Al → "true").
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
    /// El Al / AccuBattery store PNG/WebP without an extension (<c>res/raw/aay</c>, root <c>aw</c>).
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
        string[] names = ["ic_launcher", "ic_launcher_round", "ic_launcher_foreground", "icon_launcher", "icon"];
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

        return RankIconCandidates(result);
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
            .Where(p => IsImagePath(p)
                        || IsExtensionlessRasterCandidate(p)
                        || p.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
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

    private static string? PickBestIconMember(IReadOnlyList<string> rankedCandidates, IReadOnlyList<ArchiveEntry> listing)
    {
        if (rankedCandidates.Count == 0 || listing.Count == 0)
            return null;

        var byPath = listing
            .Where(e => !e.IsDirectory)
            .GroupBy(e => ArchivePath.NormalizeInternal(e.Path), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var present = rankedCandidates.Where(byPath.ContainsKey).ToList();
        if (present.Count == 0)
            return null;

        return present
            .OrderByDescending(IconCandidateScore)
            .ThenByDescending(DensityRank)
            .ThenByDescending(path => byPath[path].Size)
            .First();
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
            if (Path.GetExtension(localPath).Equals(".webp", StringComparison.OrdinalIgnoreCase))
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
