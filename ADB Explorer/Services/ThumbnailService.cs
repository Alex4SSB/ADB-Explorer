using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services.AppInfra;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Services;

public static partial class ThumbnailService
{
    private const string DCIM_THUMBNAILS = "/sdcard/DCIM/.thumbnails";
    private const string PICTURES_THUMBNAILS = "/sdcard/Pictures/.thumbnails";
    private const string MOVIES_THUMBNAILS = "/sdcard/Movies/.thumbnails";
    private const string CSV_CACHE_FILE = "thumbnailInfo.csv";
    private const string CUSTOM_PHOTOS_SUBFOLDER = "CustomThumbs";

    private static readonly (string ThumbDir, string[] MediaPrefixes)[] ImageThumbnailSources =
    [
        (DCIM_THUMBNAILS, ["/sdcard/DCIM", "/storage/emulated/0/DCIM"]),
        (PICTURES_THUMBNAILS, ["/sdcard/Pictures", "/storage/emulated/0/Pictures"]),
    ];
    
    static readonly TimeSpan MONTH = TimeSpan.FromDays(30);
    static readonly TimeSpan WEEK = TimeSpan.FromDays(7);
    static readonly TimeSpan DAY = TimeSpan.FromDays(1);
    static readonly TimeSpan HOUR = TimeSpan.FromHours(1);

    static readonly Encoding CsvEncoding = new UTF8Encoding(true);

    public enum MediaType
    {
        images,
        video,
    }

    public record struct ThumbnailInfo(string Id,
                                       MediaType Type,
                                       Size? Resolution,
                                       TimeSpan? Duration,
                                       DateTime LastUpdate,
                                       string LocalFolder = "",
                                       double? FNumber = null,
                                       int? ISO = null,
                                       double? ExposureTime = null,
                                       int? Bitrate = null,
                                       long? FileSize = null)
    {
        const string CsvDateFormat = "yyyy-MM-dd_HH:mm";

        public readonly string ResolutionString => Resolution.HasValue ? $"{Resolution.Value.Width} \u00D7 {Resolution.Value.Height}" : "";
        public readonly string DurationString => Duration.HasValue ? $"{Duration.Value.Hours:D2}:{Duration.Value.Minutes:D2}:{Duration.Value.Seconds:D2}" : "";
        public readonly string FNumberString => FNumber.HasValue ? $"ƒ/{FNumber.Value}" : "";
        public readonly string ISOString => ISO.HasValue ? $"ISO-{ISO.Value}" : "";
        public readonly string ExposureTimeString => ExposureTime.HasValue ? $"1/{(int)(1 / ExposureTime.Value)} {Strings.Resources.S_SECONDS_SHORT.Trim("{0}")}" : "";
        public readonly string BitrateString => Bitrate.HasValue ? string.Format(Strings.Resources.S_SECONDS_SHORT,$"{string.Format(Strings.Resources.KILO, Bitrate.Value / 1024)}/") : "";

        public readonly string ToCsv() => $"{Id}|{Type}|{Resolution?.Width}|{Resolution?.Height}|{Duration?.TotalMilliseconds}|{(LastUpdate == DateTime.MinValue ? null : LastUpdate.ToString(CsvDateFormat))}|{LocalFolder}|{FNumber}|{ISO}|{ExposureTime}|{Bitrate}|{FileSize}";

        public static ThumbnailInfo? FromCsv(string csv)
        {
            var parts = csv.Split('|');
            if (parts.Length < 6)
                return null;

            string id = parts[0];
            MediaType type = Enum.Parse<MediaType>(parts[1]);
            Size? resolution = (double.TryParse(parts[2], out double width) && double.TryParse(parts[3], out double height))
                ? new Size(width, height)
                : null;

            TimeSpan? duration = double.TryParse(parts[4], out double ms)
                ? TimeSpan.FromMilliseconds(ms)
                : null;

            DateTime lastUpdate = DateTime.TryParseExact(parts[5], CsvDateFormat, null, DateTimeStyles.None, out DateTime parsedDate)
                ? parsedDate
                : DateTime.MinValue;

            string localFolder = parts.Length > 6 ? parts[6] : "";
            double? fNumber = parts.Length > 7 && double.TryParse(parts[7], out double f) ? f : null;
            int? iso = parts.Length > 8 && int.TryParse(parts[8], out int i) ? i : null;
            double? exposureTime = parts.Length > 9 && double.TryParse(parts[9], out double e) ? e : null;
            int? bitrate = parts.Length > 10 && int.TryParse(parts[10], out int b) ? b : null;
            long? fileSize = parts.Length > 11 && long.TryParse(parts[11], out long bytes) ? bytes : null;

            return new(id, type, resolution, duration, lastUpdate, localFolder, fNumber, iso, exposureTime, bitrate, fileSize);
        }

        public readonly bool IsOverdue
        {
            get
            {
                var age = DateTime.Now - LastUpdate;
                
                return Data.Settings.ThumbsAge switch
                {
                    AppSettings.ThumbnailAge.OneHour => age > HOUR,
                    AppSettings.ThumbnailAge.OneDay => age > DAY,
                    AppSettings.ThumbnailAge.OneWeek => age > WEEK,
                    AppSettings.ThumbnailAge.OneMonth => age > MONTH,
                    _ => false,
                };
            }
        }
    }

    public enum ThumbnailStep
    {
        ReadingDatabase,
        CheckingUpdates,
        Pulling,
    }

    public enum ThumbnailSize
    {
        Disabled = 0,
        Medium = 48,
        Large = 96,
        ExtraLarge = 192,
        Drag = 256,
    }

    /// <summary>
    /// Raised when a thumbnail acquisition step starts or completes.
    /// The bool parameter is <see langword="true"/> when the step starts and <see langword="false"/> when it ends.
    /// </summary>
    public static event Action<ThumbnailStep, bool>? ThumbnailProgressChanged;

    /// <summary>
    /// Raised during the Pulling step to report per-file progress.
    /// Parameters are (completedFiles, totalFiles).
    /// </summary>
    public static event Action<int, int>? ThumbnailPullingProgressUpdated;

    public static event Action<string, string>? ThumbnailUpdated;

    private sealed class PaneThumbnailState
    {
        public Thumbnail? Cache;
        public ThumbnailSize LoadedSize;
        public CancellationTokenSource? Cts;
        public Action<string, string>? UpdatedHandler;
    }

    private static readonly ConditionalWeakTable<FileClass, PaneThumbnailState> PaneThumbnails = new();

    public static bool IsCustomThumbnailCandidate(FileClass file) =>
        Data.Settings.ThumbsMode is not AppSettings.ThumbnailMode.Off
        && file.Type is AbstractFile.FileType.File
        && Data.Settings.MaxCustomThumbWeight > 0
        && file.Size is long size
        && size <= (long)Data.Settings.MaxCustomThumbWeight * 1024
        && AdbExplorerConst.COMMON_PHOTO_EXT.Contains(file.Extension, StringComparer.InvariantCultureIgnoreCase);

    public static bool IsPhotoPaneThumbnailCandidate(FileClass file) =>
        Data.Settings.ThumbsMode is not AppSettings.ThumbnailMode.Off
        && file.Type is AbstractFile.FileType.File
        && !file.IsLink
        && AdbExplorerConst.COMMON_PHOTO_EXT.Contains(file.Extension, StringComparer.InvariantCultureIgnoreCase);

    public static Thumbnail? GetPaneThumbnail(FileClass file) =>
        Data.Settings.ThumbsMode is AppSettings.ThumbnailMode.Off
            ? null
            : PaneThumbnails.TryGetValue(file, out var state) ? state.Cache : null;

    /// <summary>
    /// Loads a thumbnail for the details/preview pane. Unlike icon-view / OnPhotoDir folder preloading,
    /// selection always forces acquisition when <see cref="AppSettings.ThumbsMode"/> is not Off
    /// (including <see cref="AppSettings.ThumbnailMode.IconViewOnly"/> and <see cref="AppSettings.ThumbnailMode.OnPhotoDir"/>).
    /// </summary>
    public static void BeginPaneThumbnailLoad(FileClass file, ThumbnailSize size, bool scaleWithDpi = true)
    {
        if (Data.Settings.ThumbsMode is AppSettings.ThumbnailMode.Off)
            return;

        if (Data.DevicesObject.Current is not { } device)
            return;

        var useCustomThumbs = Data.Settings.MaxCustomThumbWeight > 0;

        var state = PaneThumbnails.GetValue(file, static _ => new PaneThumbnailState());
        if (state.Cache?.Image is not null && state.LoadedSize >= size)
            return;

        if (state.Cts is not null)
            return;

        // Immediate path: CSV + local files, so details/preview does not wait for icon view or photo-dir ForceLoad.
        EnsureCsvCacheLoaded(device);
        if (LoadThumbnail(device, file, size, scaleWithDpi) is Thumbnail { Image: not null } immediate)
        {
            ApplyPaneThumbnail(file, immediate);
            state.LoadedSize = size;
            return;
        }

        var serialNumber = device.SerialNumber;
        var path = file.ParsedFullPath;

        state.Cts = new CancellationTokenSource();
        var token = state.Cts.Token;

        state.UpdatedHandler = (updatedDeviceId, updatedFilePath) =>
        {
            if (updatedDeviceId != serialNumber
                || (updatedFilePath != path && updatedFilePath != file.FullPath))
                return;

            Task.Run(() =>
            {
                if (token.IsCancellationRequested)
                    return;

                if (LoadThumbnail(device, file, size, scaleWithDpi) is not Thumbnail loaded
                    || loaded.Image is null)
                    return;

                App.SafeBeginInvoke(() =>
                {
                    if (token.IsCancellationRequested)
                    {
                        ApplyPaneThumbnail(file, loaded);
                        ClearPaneLoadToken(file, token);
                        return;
                    }

                    CompletePaneThumbnailLoad(file, loaded, size);
                }, System.Windows.Threading.DispatcherPriority.Normal);
            }, token);
        };

        ThumbnailUpdated += state.UpdatedHandler;

        Task.Run(() =>
        {
            if (token.IsCancellationRequested)
                return;

            // Pane selection forces acquisition for IconViewOnly / OnPhotoDir even if folder preload never ran.
            EnsureThumbnailsReady(device);

            if (token.IsCancellationRequested)
                return;

            var thumbnail = LoadThumbnail(device, file, size, scaleWithDpi);

            if ((thumbnail is null || thumbnail.Value.Image is null)
                && useCustomThumbs
                && IsCustomThumbnailCandidate(file))
            {
                TryPullCustomThumbnail(device, file);
                thumbnail = LoadThumbnail(device, file, size, scaleWithDpi);
            }

            if (token.IsCancellationRequested)
            {
                if (thumbnail is Thumbnail { Image: not null } cached)
                {
                    App.SafeBeginInvoke(() =>
                    {
                        ApplyPaneThumbnail(file, cached);
                        ClearPaneLoadToken(file, token);
                    }, System.Windows.Threading.DispatcherPriority.Normal);
                }
                else
                {
                    ClearPaneLoadToken(file, token);
                }

                return;
            }

            if (thumbnail is Thumbnail { Image: not null } loaded)
            {
                App.SafeBeginInvoke(() =>
                {
                    if (token.IsCancellationRequested)
                    {
                        ApplyPaneThumbnail(file, loaded);
                        ClearPaneLoadToken(file, token);
                        return;
                    }

                    CompletePaneThumbnailLoad(file, loaded, size);
                }, System.Windows.Threading.DispatcherPriority.Normal);
            }
            else
            {
                var waitingForPull = IsPendingCustomPull($"{serialNumber}|{path}")
                    || (!string.Equals(file.FullPath, path, StringComparison.Ordinal)
                        && IsPendingCustomPull($"{serialNumber}|{file.FullPath}"));

                if (!waitingForPull)
                {
                    ClearPaneLoadToken(file, token);
                    App.SafeBeginInvoke(() => file.NotifyPaneThumbnailChanged());
                }
                // else: waiting for async custom pull via ThumbnailUpdated; keep CTS/handler
            }
        }, token);
    }

    public static bool IsPaneThumbnailLoading(FileClass file) =>
        PaneThumbnails.TryGetValue(file, out var state) && state.Cts is not null;

    /// <summary>
    /// Ends an in-flight pane load with no image and notifies listeners (preview can show "unavailable").
    /// </summary>
    private static void NotifyPaneThumbnailUnavailable(FileClass file)
    {
        App.SafeBeginInvoke(() =>
        {
            CancelPaneThumbnailLoad(file);
            file.NotifyPaneThumbnailChanged();
        });
    }

    /// <summary>Loads <c>thumbnailInfo.csv</c> into memory without querying the device media DB (safe on UI thread).</summary>
    private static void EnsureCsvCacheLoaded(LogicalDeviceViewModel device)
    {
        if (GetDeviceThumbsInfo(device.SerialNumber) is null)
            return;

        var deviceInfo = GetCachedDeviceInfo(device.SerialNumber);
        if (deviceInfo.ThumbnailPathCache is not null)
            return;

        var cache = GetThumbsCacheFromCsv(deviceInfo);
        if (cache.Count == 0)
            return;

        deviceInfo.ThumbnailPathCache = cache;
        if (deviceInfo.HasThumbnailSupport)
        {
            deviceInfo = SetDeviceLocalDir(deviceInfo);
            Task.Run(() => MergeDeviceWithLocalCache(deviceInfo));
        }

        UpdateCache(deviceInfo);
    }

    /// <summary>Ensures device thumbnail metadata is available, forcing load if pane selection needs it before folder preload.</summary>
    private static void EnsureThumbnailsReady(LogicalDeviceViewModel device)
    {
        var info = GetCachedDeviceInfo(device.SerialNumber);
        if (!string.IsNullOrEmpty(info.DeviceId) && info.ThumbnailPathCache is not null)
            return;

        ForceLoad(device);
    }

    private static void ClearPaneLoadToken(FileClass file, CancellationToken token)
    {
        if (!PaneThumbnails.TryGetValue(file, out var state))
            return;

        if (state.Cts is null || state.Cts.Token != token)
            return;

        if (state.UpdatedHandler is not null)
        {
            ThumbnailUpdated -= state.UpdatedHandler;
            state.UpdatedHandler = null;
        }

        state.Cts.Dispose();
        state.Cts = null;
    }

    public static void CancelPaneThumbnailLoad(FileClass file)
    {
        if (!PaneThumbnails.TryGetValue(file, out var state))
            return;

        if (state.UpdatedHandler is not null)
        {
            ThumbnailUpdated -= state.UpdatedHandler;
            state.UpdatedHandler = null;
        }

        state.Cts?.Cancel();
        state.Cts?.Dispose();
        state.Cts = null;
    }

    public static void ApplyPaneThumbnail(FileClass file, Thumbnail thumb)
    {
        var state = PaneThumbnails.GetValue(file, static _ => new PaneThumbnailState());
        state.Cache = thumb;
        state.LoadedSize = ThumbnailSize.Drag;
        file.NotifyPaneThumbnailChanged();
    }

    private static void CompletePaneThumbnailLoad(FileClass file, Thumbnail thumb, ThumbnailSize size)
    {
        if (!PaneThumbnails.TryGetValue(file, out var state))
            return;

        if (state.UpdatedHandler is not null)
        {
            ThumbnailUpdated -= state.UpdatedHandler;
            state.UpdatedHandler = null;
        }

        state.Cts?.Dispose();
        state.Cts = null;
        state.Cache = thumb;
        state.LoadedSize = size;
        file.NotifyPaneThumbnailChanged();
    }

    private static readonly Mutex _mutex = new(false);
    private static readonly Mutex _listMutex = new(false);
    private static readonly HashSet<string> PendingCustomPulls = [];
    private static readonly Lock PendingCustomPullsLock = new();
    private static readonly Lock ThrottledPullScheduleLock = new();
    private static readonly Queue<(string PullKey, Action<bool> StartPull)> ThrottledPullQueue = new();
    private static bool _throttledPullInFlight;
    private static readonly ConcurrentDictionary<string, Lock> DeviceCsvLocks = new(StringComparer.Ordinal);

    private static readonly List<DeviceThumbnailInfo> _deviceInfoCache = [];

    public record struct Thumbnail(BitmapSource Image, ThumbnailInfo Info);

    private record struct DeviceThumbnailInfo
    {
        public DeviceThumbnailInfo(string serialNumber, string physicalId)
        {
            DeviceId = serialNumber;
            PhysicalId = physicalId;
        }

        /// <summary>
        /// Physical Device ID used for ADB commands.
        /// </summary>
        public string PhysicalId { get; }

        /// <summary>
        /// Device serial number used for on-disk storage and device comparison.
        /// </summary>
        public string DeviceId { get; init; }
        public string DevicePicturesThumbnailDir { get; set; }
        public string[] DeviceImageThumbnailDirs { get; set; } = [];
        public string? DeviceMoviesThumbnailDir { get; set; }
        public string LocalThumbnailDir { get; set; }
        public bool IsProbed { get; set; }
        public bool HasThumbnailSupport { get; set; }

        /// <summary>
        /// Key: Original file path on the device, Value: Thumbnail info (including thumbnail ID which corresponds to the local thumbnail file name)
        /// </summary>
        public Dictionary<string, ThumbnailInfo> ThumbnailPathCache { get; set; }
    }

    [GeneratedRegex(@"Row: \d+ _id=(?<ID>\d+), _data=(?<Path>.+), resolution=(?:(?:(?<ResX>\d+).(?<ResY>\d+))|NULL), f_number=(?:(?<fNum>[\d.]+)|NULL), iso=(?:(?<ISO>\d+)|NULL), exposure_time=(?:(?<Exposure>[\d.E-]+)|NULL)", RegexOptions.Multiline)]
    private static partial Regex RE_IMAGE_METADATA();

    [GeneratedRegex(@"Row: \d+ _id=(?<ID>\d+), _data=(?<Path>.+), resolution=(?:(?:(?<ResX>\d+).(?<ResY>\d+))|NULL), duration=(?:(?<Dur>\d+)|NULL), bitrate=(?:(?<Bitrate>\d+)|NULL)", RegexOptions.Multiline)]
    private static partial Regex RE_VIDEO_METADATA();

    private static DeviceThumbnailInfo? GetDeviceThumbsInfo(string serialNumber)
    {
        _listMutex.WaitOne();
        try
        {
            var cached = _deviceInfoCache.FirstOrDefault(d => d.DeviceId == serialNumber);
            if (!string.IsNullOrEmpty(cached.DeviceId) && cached.IsProbed)
                return cached;

            var device = Data.DevicesObject.LogicalDeviceViewModels.FirstOrDefault(d => d.SerialNumber == serialNumber);
            if (device is null)
                return null;

            var info = new DeviceThumbnailInfo(serialNumber, device.ID);

            var existing = new HashSet<string>(
                ADBService.PathsExist(info.PhysicalId, DCIM_THUMBNAILS, PICTURES_THUMBNAILS, MOVIES_THUMBNAILS),
                StringComparer.Ordinal);

            var imageDirs = ImageThumbnailSources
                .Where(s => existing.Contains(s.ThumbDir))
                .Select(s => s.ThumbDir)
                .ToArray();

            var hasMovies = Data.Settings.MovieThumbsEnabled && existing.Contains(MOVIES_THUMBNAILS);
            info.DeviceImageThumbnailDirs = imageDirs;
            info.DevicePicturesThumbnailDir = imageDirs.Length > 0 ? imageDirs[0] : "";
            if (hasMovies)
                info.DeviceMoviesThumbnailDir = MOVIES_THUMBNAILS;

            info.IsProbed = true;
            info.HasThumbnailSupport = imageDirs.Length > 0 || hasMovies;
            _deviceInfoCache.Add(info);

            return info;
        }
        finally
        {
            _listMutex.ReleaseMutex();
        }
    }

    /// <summary>
    /// Cancels any in-progress thumbnail loading and hides the progress tooltip.
    /// </summary>
    public static void StopLoading()
    {
        foreach (ThumbnailStep step in Enum.GetValues<ThumbnailStep>())
            ThumbnailProgressChanged?.Invoke(step, false);
    }

    public static bool ForceLoad(LogicalDeviceViewModel device)
    {
        if (!_mutex.WaitOne(0))
            return false;

        if (GetDeviceThumbsInfo(device.SerialNumber) is not DeviceThumbnailInfo deviceInfo)
        {
            _mutex.ReleaseMutex();
            return false;
        }

        UpdateDeviceCache(deviceInfo);
        deviceInfo = GetCachedDeviceInfo(device.SerialNumber);

        var success = deviceInfo.HasThumbnailSupport
            ? GetLocalThumbPath(device) is not null
            : Data.Settings.MaxCustomThumbWeight > 0 || deviceInfo.ThumbnailPathCache?.Count > 0;

        _mutex.ReleaseMutex();

        return success;
    }

    /// <summary>
    /// Called when a thumbnail was requested but not found locally. Handles two cases:
    /// <list type="bullet">
    /// <item>The file is known in the device media DB (<see cref="ThumbnailInfo.LastUpdate"/> == <see cref="DateTime.MinValue"/>) but its local file is absent — pulls the original photo into the cached folder under its DB id.</item>
    /// <item>The file is not in the cache at all and qualifies for custom-weight pulling — pulls it into <see cref="CUSTOM_PHOTOS_SUBFOLDER"/> with a timestamp-based name.</item>
    /// </list>
    /// Fires <see cref="ThumbnailUpdated"/> upon completion so the caller can reload.
    /// </summary>
    public static void TryPullCustomThumbnail(LogicalDeviceViewModel device, FileClass file)
    {
        if (!IsCustomThumbnailCandidate(file))
            return;

        if (GetDeviceThumbsInfo(device.SerialNumber) is not DeviceThumbnailInfo deviceInfo)
            return;

        UpdateDeviceCache(deviceInfo);

        deviceInfo = GetCachedDeviceInfo(device.SerialNumber);
        if (string.IsNullOrEmpty(deviceInfo.DeviceId) || deviceInfo.ThumbnailPathCache is null)
            return;

        var filePath = file.ParsedFullPath;
        var pullKey = $"{device.SerialNumber}|{filePath}";

        lock (PendingCustomPullsLock)
        {
            if (!PendingCustomPulls.Add(pullKey))
                return;
        }

        // Archive members: extract on device → sync-pull into CustomThumbs (no shell-stdout transfer).
        if (ArchivePath.TryParse(file.FullPath, out var archivePath, out var internalPath, device.ID)
            && !string.IsNullOrEmpty(internalPath))
        {
            QueueArchiveMemberThumbnailPull(pullKey, device, file, archivePath, internalPath, deviceInfo);
            return;
        }

        if (deviceInfo.ThumbnailPathCache.TryGetValue(filePath, out var cachedInfo)
            || (file.FullPath != filePath && deviceInfo.ThumbnailPathCache.TryGetValue(file.FullPath, out cachedInfo)))
        {
            var localDir = string.IsNullOrEmpty(cachedInfo.LocalFolder)
                ? GetLocalThumbPath(device)
                : Path.Combine(Data.AppDataPath, deviceInfo.DeviceId, cachedInfo.LocalFolder);

            if (localDir is null)
            {
                CancelCustomPull(pullKey);
                return;
            }

            var localFile = Path.Combine(localDir, cachedInfo.Id);
            if (File.Exists(localFile))
            {
                CancelCustomPull(pullKey);
                var resolution = cachedInfo.Resolution ?? ReadImageResolution(localFile);
                long? fileSize = new FileInfo(localFile).Length;
                MarkCustomThumbnailReady(device.SerialNumber, file, cachedInfo.Id, resolution, fileSize);
                return;
            }

            QueueSyncPullToLocal(pullKey, file, localFile, device, cachedInfo.Id, cachedInfo.Resolution);
            return;
        }

        deviceInfo = GetCachedDeviceInfo(device.SerialNumber);
        if (string.IsNullOrEmpty(deviceInfo.DeviceId))
        {
            CancelCustomPull(pullKey);
            return;
        }

        string newId;
        var targetDir = Path.Combine(Data.AppDataPath, deviceInfo.DeviceId, CUSTOM_PHOTOS_SUBFOLDER);
        lock (GetDeviceCsvLock(deviceInfo.DeviceId))
        {
            deviceInfo = GetCachedDeviceInfo(device.SerialNumber);
            if (deviceInfo.ThumbnailPathCache is null)
            {
                CancelCustomPull(pullKey);
                return;
            }

            if (deviceInfo.ThumbnailPathCache.ContainsKey(filePath))
            {
                CancelCustomPull(pullKey);
                return;
            }

            Directory.CreateDirectory(targetDir);
            newId = NewCustomThumbId(file, useOriginalExtension: false);

            RegisterCustomThumbCacheEntry(ref deviceInfo, file, newId);
            UpdateCache(deviceInfo);
            WriteThumbsCacheToCsvFile(deviceInfo.DeviceId, deviceInfo.ThumbnailPathCache);
        }

        QueueSyncPullToLocal(pullKey, file, Path.Combine(targetDir, newId), device, newId, knownResolution: null);
    }

    /// <summary>
    /// Extract archive member to device temp, sync-pull into <see cref="CUSTOM_PHOTOS_SUBFOLDER"/>, register cache, notify.
    /// </summary>
    private static void QueueArchiveMemberThumbnailPull(
        string pullKey,
        LogicalDeviceViewModel device,
        FileClass file,
        string archivePath,
        string internalPath,
        DeviceThumbnailInfo deviceInfo)
    {
        string thumbId;
        var targetDir = Path.Combine(Data.AppDataPath, deviceInfo.DeviceId, CUSTOM_PHOTOS_SUBFOLDER);

        lock (GetDeviceCsvLock(deviceInfo.DeviceId))
        {
            deviceInfo = GetCachedDeviceInfo(device.SerialNumber);
            if (deviceInfo.ThumbnailPathCache is null)
            {
                CancelCustomPull(pullKey);
                return;
            }

            if (deviceInfo.ThumbnailPathCache.TryGetValue(file.FullPath, out var existing)
                || deviceInfo.ThumbnailPathCache.TryGetValue(file.ParsedFullPath, out existing))
            {
                thumbId = existing.Id;
                var existingLocal = Path.Combine(targetDir, thumbId);
                if (File.Exists(existingLocal))
                {
                    CancelCustomPull(pullKey);
                    MarkCustomThumbnailReady(
                        device.SerialNumber,
                        file,
                        thumbId,
                        existing.Resolution ?? ReadImageResolution(existingLocal),
                        new FileInfo(existingLocal).Length);

                    return;
                }
            }
            else
            {
                thumbId = NewCustomThumbId(file, useOriginalExtension: true);
                RegisterCustomThumbCacheEntry(ref deviceInfo, file, thumbId);
                UpdateCache(deviceInfo);
                WriteThumbsCacheToCsvFile(deviceInfo.DeviceId, deviceInfo.ThumbnailPathCache);
            }
        }

        var localFile = Path.Combine(targetDir, thumbId);
        QueueCustomPull(pullKey, throttled =>
        {
            string? stagingRoot = null;
            var succeeded = false;
            try
            {
                Directory.CreateDirectory(targetDir);

                var (root, extractedPath, _) = ArchiveExtract.ExtractSelectionForPull(
                    device.ID,
                    archivePath,
                    internalPath,
                    isDirectory: false,
                    CancellationToken.None);
                stagingRoot = root;

                using (var service = new AdvancedSharpAdbClient.SyncService(device.DeviceData))
                using (var fs = new FileStream(localFile, FileMode.Create, FileAccess.Write, FileShare.Read))
                {
                    service.Pull(extractedPath, fs);
                    SyncTransferTracker.AddPullBytes(fs.Length);
                }

                if (!File.Exists(localFile) || new FileInfo(localFile).Length == 0)
                    return;

                MarkCustomThumbnailReady(
                    device.SerialNumber,
                    file,
                    thumbId,
                    ReadImageResolution(localFile),
                    new FileInfo(localFile).Length);

                succeeded = true;
            }
            catch (Exception e)
            {
#if !DEPLOY
                DebugLog.PrintLine($"Archive custom thumbnail pull failed: {e.Message}");
#endif
                try { if (File.Exists(localFile)) File.Delete(localFile); } catch { /* ignore */ }
            }
            finally
            {
                if (stagingRoot is not null)
                    ArchiveExtract.CleanupStaging(device.ID, stagingRoot, CancellationToken.None);

                CompleteCustomPull(pullKey, throttled);
                if (!succeeded)
                    NotifyPaneThumbnailUnavailable(file);
            }
        });
    }

    private static string NewCustomThumbId(FileClass file, bool useOriginalExtension)
    {
        var noExt = file.NoExtName;
        var prefix = noExt[^Math.Min(5, noExt.Length)..];
        var ext = useOriginalExtension && !string.IsNullOrEmpty(file.Extension)
            ? file.Extension
            : ".jpg";

        return $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{prefix}{ext}";
    }

    private static void RegisterCustomThumbCacheEntry(ref DeviceThumbnailInfo deviceInfo, FileClass file, string thumbId)
    {
        var entry = new ThumbnailInfo(thumbId, MediaType.images, null, null, DateTime.MinValue, CUSTOM_PHOTOS_SUBFOLDER);
        deviceInfo.ThumbnailPathCache[file.FullPath] = entry;
        if (!string.Equals(file.ParsedFullPath, file.FullPath, StringComparison.Ordinal))
            deviceInfo.ThumbnailPathCache[file.ParsedFullPath] = entry;
    }

    /// <summary>
    /// Writes resolution/size into the cache for both FullPath and ParsedFullPath, then fires <see cref="ThumbnailUpdated"/>.
    /// </summary>
    private static void MarkCustomThumbnailReady(
        string serialNumber,
        FileClass file,
        string thumbId,
        Size? resolution,
        long? fileSize)
    {
        var deviceInfo = GetCachedDeviceInfo(serialNumber);
        if (string.IsNullOrEmpty(deviceInfo.DeviceId) || deviceInfo.ThumbnailPathCache is null)
            return;

        // Update \ Insert
        void upsert(string key)
        {
            if (string.IsNullOrEmpty(key))
                return;

            if (deviceInfo.ThumbnailPathCache.TryGetValue(key, out var existing) && existing.Id == thumbId)
            {
                deviceInfo.ThumbnailPathCache[key] = existing with
                {
                    LastUpdate = DateTime.Now,
                    Resolution = resolution ?? existing.Resolution,
                    FileSize = fileSize ?? existing.FileSize,
                };
            }
            else
            {
                deviceInfo.ThumbnailPathCache[key] = new ThumbnailInfo(
                    thumbId,
                    MediaType.images,
                    resolution,
                    null,
                    DateTime.Now,
                    CUSTOM_PHOTOS_SUBFOLDER,
                    FileSize: fileSize);
            }
        }

        upsert(file.FullPath);
        if (!string.Equals(file.ParsedFullPath, file.FullPath, StringComparison.Ordinal))
            upsert(file.ParsedFullPath);

        UpdateCache(deviceInfo);
        lock (GetDeviceCsvLock(deviceInfo.DeviceId))
            WriteThumbsCacheToCsvFile(deviceInfo.DeviceId, deviceInfo.ThumbnailPathCache);

        ThumbnailUpdated?.Invoke(serialNumber, file.FullPath);
        if (!string.Equals(file.ParsedFullPath, file.FullPath, StringComparison.Ordinal))
            ThumbnailUpdated?.Invoke(serialNumber, file.ParsedFullPath);
    }

    private static void QueueSyncPullToLocal(
        string pullKey,
        FileClass file,
        string localFile,
        LogicalDeviceViewModel device,
        string thumbId,
        Size? knownResolution)
    {
        var source = new SyncFile(file);
        var parent = Path.GetDirectoryName(localFile) ?? localFile;
        var target = SyncFile.MergeToWindowsPath(source, parent);
        target.UpdatePath(localFile);

        QueueCustomPull(pullKey, throttled =>
        {
            var op = FileSyncOperation.PullFile(source, target, device, App.AppDispatcher);
            if (throttled)
                op.MaxThreads = 1;

            op.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(FileOperation.Status))
                    return;

                if (op.Status is FileOperation.OperationStatus.Completed)
                {
                    var resolution = knownResolution ?? ReadImageResolution(localFile);
                    long? pulledSize = File.Exists(localFile) ? new FileInfo(localFile).Length : null;
                    MarkCustomThumbnailReady(device.SerialNumber, file, thumbId, resolution, pulledSize);
                    CompleteCustomPull(pullKey, throttled);
                }
                else if (op.Status is FileOperation.OperationStatus.Failed
                    or FileOperation.OperationStatus.Canceled)
                {
                    CompleteCustomPull(pullKey, throttled);
                    NotifyPaneThumbnailUnavailable(file);
                }
            };

            op.Start();
        });
    }

    private static void QueueCustomPull(string pullKey, Action<bool> startPull)
    {
        if (!Data.Settings.LimitThumbsPullSpeed)
        {
            _ = Task.Run(() => RunCustomPull(pullKey, startPull, throttled: false));
            return;
        }

        lock (ThrottledPullScheduleLock)
        {
            ThrottledPullQueue.Enqueue((pullKey, startPull));
            TryStartNextThrottledPull();
        }
    }

    private static void TryStartNextThrottledPull()
    {
        if (_throttledPullInFlight)
            return;

        while (ThrottledPullQueue.Count > 0)
        {
            var (pullKey, startPull) = ThrottledPullQueue.Dequeue();
            if (!IsPendingCustomPull(pullKey))
                continue;

            _throttledPullInFlight = true;
            _ = Task.Run(() => RunCustomPull(pullKey, startPull, throttled: true));
            return;
        }
    }

    private static void RunCustomPull(string pullKey, Action<bool> startPull, bool throttled)
    {
        try
        {
            if (!IsPendingCustomPull(pullKey))
            {
                if (throttled)
                    ThrottledPullCompleted();
                return;
            }

            startPull(throttled);
        }
        catch
        {
            CompleteCustomPull(pullKey, throttled);
        }
    }

    private static bool IsPendingCustomPull(string pullKey)
    {
        lock (PendingCustomPullsLock)
            return PendingCustomPulls.Contains(pullKey);
    }

    private static void ThrottledPullCompleted()
    {
        lock (ThrottledPullScheduleLock)
        {
            _throttledPullInFlight = false;
            TryStartNextThrottledPull();
        }
    }

    private static void CancelCustomPull(string pullKey)
    {
        lock (PendingCustomPullsLock)
            PendingCustomPulls.Remove(pullKey);
    }

    private static void CompleteCustomPull(string pullKey, bool throttled)
    {
        CancelCustomPull(pullKey);
        if (throttled)
            ThrottledPullCompleted();
    }

    private static DeviceThumbnailInfo GetCachedDeviceInfo(string serialNumber)
    {
        _listMutex.WaitOne();
        try
        {
            return _deviceInfoCache.FirstOrDefault(d => d.DeviceId == serialNumber);
        }
        finally
        {
            _listMutex.ReleaseMutex();
        }
    }

    public static bool IsInitialized(string serialNumber)
    {
        _listMutex.WaitOne();
        try
        {
            return _deviceInfoCache.FirstOrDefault(d => d.DeviceId == serialNumber) is DeviceThumbnailInfo info
                && info.IsProbed;
        }
        finally
        {
            _listMutex.ReleaseMutex();
        }
    }

    public static Thumbnail? LoadThumbnail(LogicalDeviceViewModel device, FileClass file, ThumbnailSize size, bool scaleWithDpi = true)
    {
        var thumbnail = LoadThumbnail(device, file.ParsedFullPath, size, scaleWithDpi);
        if (thumbnail?.Image is not null || file.FullPath == file.ParsedFullPath)
            return thumbnail;

        return LoadThumbnail(device, file.FullPath, size, scaleWithDpi) ?? thumbnail;
    }

    public static Thumbnail? LoadThumbnail(LogicalDeviceViewModel device, string filePath, ThumbnailSize size, bool scaleWithDpi = true)
    {
        if (GetDeviceThumbsInfo(device.SerialNumber) is not DeviceThumbnailInfo deviceInfo)
            return null;

        UpdateDeviceCache(deviceInfo);
        deviceInfo = GetCachedDeviceInfo(device.SerialNumber);
        if (string.IsNullOrEmpty(deviceInfo.DeviceId) || deviceInfo.ThumbnailPathCache is null)
            return null;

        if (!deviceInfo.ThumbnailPathCache.TryGetValue(filePath, out var info))
            return null;

        var localThumbnailDir = GetLocalThumbDirectory(deviceInfo, info);
        if (string.IsNullOrEmpty(info.LocalFolder) && string.IsNullOrEmpty(localThumbnailDir))
            localThumbnailDir = GetLocalThumbPath(device);

        if (string.IsNullOrEmpty(localThumbnailDir))
            return null;

        var fullPath = Path.Combine(localThumbnailDir, info.Id);
        if (NeedsThumbnailRefresh(info, fullPath))
        {
            if (info.LocalFolder != CUSTOM_PHOTOS_SUBFOLDER && deviceInfo.HasThumbnailSupport)
                TryPullStaleThumbnail(device, deviceInfo, info, filePath);

            return new(null, info);
        }

        try
        {
            var decodePixelWidth = Data.RuntimeSettings.MainWindowScalingFactor > 0
                ? (int)Math.Ceiling((int)size / Data.RuntimeSettings.MainWindowScalingFactor)
                : (int)size * 2;

            using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var bitmapImage = new BitmapImage();
            bitmapImage.BeginInit();
            bitmapImage.StreamSource = stream;
            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
            bitmapImage.DecodePixelWidth = scaleWithDpi ? decodePixelWidth : (int)size;
            bitmapImage.EndInit();
            bitmapImage.Freeze();

            if (!info.Resolution.HasValue)
            {
                info = info with { Resolution = new Size(bitmapImage.PixelWidth, bitmapImage.PixelHeight) };
                UpdateThumbnailInfo(device.SerialNumber, info.Id, info.Resolution);
                deviceInfo = GetCachedDeviceInfo(device.SerialNumber);
                if (deviceInfo.ThumbnailPathCache?.TryGetValue(filePath, out var refreshed) is true)
                    info = refreshed;
            }

            return new(bitmapImage, info);
        }
        catch
        {
            return null;
        }
    }

    public static void SaveAllThumbsToCsv()
    {
        DeviceThumbnailInfo[] snapshot;
        _listMutex.WaitOne();
        try
        {
            snapshot = [.. _deviceInfoCache];
        }
        finally
        {
            _listMutex.ReleaseMutex();
        }

        foreach (var item in snapshot)
        {
            DeviceThumbsToCsv(item);
        }
    }

    private static Lock GetDeviceCsvLock(string deviceId) => DeviceCsvLocks.GetOrAdd(deviceId, static _ => new Lock());

    private static void DeviceThumbsToCsv(DeviceThumbnailInfo? deviceInfo)
    {
        if (deviceInfo is not DeviceThumbnailInfo info || info.ThumbnailPathCache is null || info.ThumbnailPathCache.Count == 0)
            return;

        lock (GetDeviceCsvLock(info.DeviceId))
            WriteThumbsCacheToCsvFile(info.DeviceId, info.ThumbnailPathCache);
    }

    private static void WriteThumbsCacheToCsvFile(string deviceId, Dictionary<string, ThumbnailInfo> thumbnailPathCache)
    {
        if (thumbnailPathCache.Count == 0)
            return;

        var deviceDir = Path.Combine(Data.AppDataPath, deviceId);
        if (!Directory.Exists(deviceDir))
            Directory.CreateDirectory(deviceDir);

        var csvLines = thumbnailPathCache.ToArray().Select(kvp => $"{kvp.Key}|{kvp.Value.ToCsv()}");
        var csvContent = string.Join(Environment.NewLine, csvLines);
        var savePath = Path.Combine(deviceDir, CSV_CACHE_FILE);

        File.WriteAllText(savePath, csvContent, CsvEncoding);
    }

    private static Dictionary<string, ThumbnailInfo> ReadThumbsCacheFromCsvFile(string deviceId)
    {
        var cache = new Dictionary<string, ThumbnailInfo>();
        var csvPath = Path.Combine(Data.AppDataPath, deviceId, CSV_CACHE_FILE);
        if (!File.Exists(csvPath))
            return cache;

        string[] lines;
        try
        {
            lines = File.ReadAllLines(csvPath, CsvEncoding);
        }
        catch
        {
            return cache;
        }

        foreach (var line in lines)
        {
            var parts = line.Split('|', 2);
            if (parts.Length != 2)
                continue;

            var path = parts[0];
            var infoCsv = parts[1];
            if (ThumbnailInfo.FromCsv(infoCsv) is ThumbnailInfo info)
                cache.TryAdd(path, info);
        }

        return cache;
    }

    private static void UpdateDeviceCache(DeviceThumbnailInfo deviceInfo)
    {
        if (deviceInfo.ThumbnailPathCache is not null)
            return;

        var cache = GetThumbsCacheFromCsv(deviceInfo);

        if (cache.Count == 0)
        {
            deviceInfo.ThumbnailPathCache = deviceInfo.HasThumbnailSupport
                ? GetThumbsCacheFromDevice(deviceInfo)
                : [];

            if (deviceInfo.HasThumbnailSupport)
                DeviceThumbsToCsv(deviceInfo);
        }
        else
        {
            deviceInfo.ThumbnailPathCache = cache;
            if (deviceInfo.HasThumbnailSupport)
            {
                deviceInfo = SetDeviceLocalDir(deviceInfo);
                Task.Run(() => MergeDeviceWithLocalCache(deviceInfo));
            }
        }

        UpdateCache(deviceInfo);
    }

    private static void MergeDeviceWithLocalCache(DeviceThumbnailInfo deviceInfo)
    {
        if (!deviceInfo.HasThumbnailSupport)
            return;
        var deviceCache = GetThumbsCacheFromDevice(deviceInfo);

        foreach (var kvp in deviceCache)
        {
            deviceInfo.ThumbnailPathCache.TryAdd(kvp.Key, kvp.Value);
        }

        UpdateCache(deviceInfo);
        PullThumbnails(deviceInfo);
    }

    private static void UpdateCache(DeviceThumbnailInfo deviceInfo)
    {
        _listMutex.WaitOne();

        _deviceInfoCache.RemoveAll(d => d.DeviceId == deviceInfo.DeviceId);
        _deviceInfoCache.Add(deviceInfo);

        _listMutex.ReleaseMutex();
    }

    private static Dictionary<string, ThumbnailInfo> GetThumbsCacheFromCsv(DeviceThumbnailInfo deviceInfo)
    {
        lock (GetDeviceCsvLock(deviceInfo.DeviceId))
            return ReadThumbsCacheFromCsvFile(deviceInfo.DeviceId);
    }

    private static Dictionary<string, ThumbnailInfo> GetThumbsCacheFromDevice(DeviceThumbnailInfo deviceInfo)
    {
        ThumbnailProgressChanged?.Invoke(ThumbnailStep.ReadingDatabase, true);

        var ct = Data.DeviceCts.Token;
        var picsResponse = GetThumbsFromDevice(deviceInfo, MediaType.images, ct);
        var thumbnailMap = ParseThumbnailMap(picsResponse, MediaType.images).ToList();

        string moviesResponse = Data.Settings.MovieThumbsEnabled
            ? GetThumbsFromDevice(deviceInfo, MediaType.video, ct)
            : "";

        var moviesThumbnailMap = ParseThumbnailMap(moviesResponse, MediaType.video);

        ThumbnailProgressChanged?.Invoke(ThumbnailStep.ReadingDatabase, false);

        if (thumbnailMap is null || thumbnailMap.Count == 0)
            // Cache an almost empty dictionary to avoid repeated ADB calls
            return new([new KeyValuePair<string, ThumbnailInfo>("", new())]);
        
        IEnumerable<KeyValuePair<string, ThumbnailInfo>> combined = [.. thumbnailMap, .. moviesThumbnailMap];
        return combined.ToDictionary();
    }

    private static void UpdateThumbnailInfo(string serialNumber, string id, Size? resolution = null, long? fileSize = null)
    {
        var deviceInfo = GetCachedDeviceInfo(serialNumber);
        if (string.IsNullOrEmpty(deviceInfo.DeviceId) || deviceInfo.ThumbnailPathCache is null)
            return;

        List<string> updatedKeys = [];
        foreach (var (key, value) in deviceInfo.ThumbnailPathCache)
        {
            if (value.Id != id)
                continue;

            if (fileSize is null)
            {
                var localPath = GetLocalThumbFilePath(deviceInfo, value);
                if (localPath is not null && File.Exists(localPath))
                    fileSize = new FileInfo(localPath).Length;
            }

            deviceInfo.ThumbnailPathCache[key] = value with
            {
                LastUpdate = DateTime.Now,
                Resolution = resolution ?? value.Resolution,
                FileSize = fileSize ?? value.FileSize,
            };
            updatedKeys.Add(key);
        }

        if (updatedKeys.Count == 0)
            return;

        UpdateCache(deviceInfo);

        lock (GetDeviceCsvLock(deviceInfo.DeviceId))
            WriteThumbsCacheToCsvFile(deviceInfo.DeviceId, deviceInfo.ThumbnailPathCache);

        foreach (var key in updatedKeys)
            ThumbnailUpdated?.Invoke(serialNumber, key);
    }

    private static string? GetLocalThumbDirectory(DeviceThumbnailInfo deviceInfo, ThumbnailInfo info)
        => string.IsNullOrEmpty(info.LocalFolder)
            ? deviceInfo.LocalThumbnailDir
            : Path.Combine(Data.AppDataPath, deviceInfo.DeviceId, info.LocalFolder);

    private static string? GetLocalThumbFilePath(DeviceThumbnailInfo deviceInfo, ThumbnailInfo info)
    {
        var dir = GetLocalThumbDirectory(deviceInfo, info);
        return dir is null ? null : Path.Combine(dir, info.Id);
    }

    private static string? GetDeviceThumbDirectory(DeviceThumbnailInfo deviceInfo, ThumbnailInfo info, string originalFilePath)
    {
        if (info.Type is MediaType.video)
            return deviceInfo.DeviceMoviesThumbnailDir;

        foreach (var (thumbDir, prefixes) in ImageThumbnailSources)
        {
            if (deviceInfo.DeviceImageThumbnailDirs is null || !deviceInfo.DeviceImageThumbnailDirs.Contains(thumbDir))
                continue;

            foreach (var prefix in prefixes)
            {
                if (originalFilePath.StartsWith(prefix, StringComparison.Ordinal))
                    return thumbDir;
            }
        }

        return string.IsNullOrEmpty(deviceInfo.DevicePicturesThumbnailDir)
            ? null
            : deviceInfo.DevicePicturesThumbnailDir;
    }

    private static bool NeedsThumbnailRefresh(ThumbnailInfo info, string localPath)
    {
        if (!File.Exists(localPath))
            return true;

        var localSize = new FileInfo(localPath).Length;
        if (info.FileSize is long expected && expected > 0)
            return localSize < expected;

        return localSize == 0;
    }

    private static void TryPullStaleThumbnail(LogicalDeviceViewModel device, DeviceThumbnailInfo deviceInfo, ThumbnailInfo info, string originalFilePath)
    {
        var androidDir = GetDeviceThumbDirectory(deviceInfo, info, originalFilePath);
        var localDir = GetLocalThumbDirectory(deviceInfo, info);
        if (string.IsNullOrEmpty(androidDir) || string.IsNullOrEmpty(localDir))
            return;

        Directory.CreateDirectory(localDir);

        var androidPath = FileHelper.ConcatPaths(androidDir, info.Id);
        var localPath = Path.Combine(localDir, info.Id);
        var source = new SyncFile(androidPath, AbstractFile.FileType.File);
        var target = SyncFile.MergeToWindowsPath(source, localDir);
        target.UpdatePath(localPath);
        var id = info.Id;

        var op = FileSyncOperation.PullFile(source, target, device, App.AppDispatcher);
        op.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FileOperation.Status) && op.Status is FileOperation.OperationStatus.Completed)
            {
                long? size = File.Exists(localPath) ? new FileInfo(localPath).Length : null;
                UpdateThumbnailInfo(device.SerialNumber, id, fileSize: size);
            }
        };

        op.Start();
    }

    private static Size? ReadImageResolution(string filePath)
    {
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var frame = BitmapFrame.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            return new Size(frame.PixelWidth, frame.PixelHeight);
        }
        catch
        {
            return null;
        }
    }

    private static string? GetLocalThumbPath(LogicalDeviceViewModel device)
    {
        if (GetDeviceThumbsInfo(device.SerialNumber) is not DeviceThumbnailInfo deviceInfo)
            return null;

        if (!deviceInfo.HasThumbnailSupport)
            return null;

        if (!string.IsNullOrEmpty(deviceInfo.LocalThumbnailDir))
        {
            return deviceInfo.LocalThumbnailDir;
        }

        deviceInfo = SetDeviceLocalDir(deviceInfo);

        UpdateCache(deviceInfo);
        PullThumbnails(deviceInfo);

        return deviceInfo.LocalThumbnailDir;
    }

    private static DeviceThumbnailInfo SetDeviceLocalDir(DeviceThumbnailInfo deviceInfo)
    {
        var deviceDir = Path.Combine(Data.AppDataPath, deviceInfo.DeviceId);
        deviceInfo.LocalThumbnailDir = Path.Combine(deviceDir, Path.GetFileName(deviceInfo.DevicePicturesThumbnailDir));

        return deviceInfo;
    }

    private static void PullThumbnails(DeviceThumbnailInfo deviceInfo)
    {
        if (!deviceInfo.HasThumbnailSupport)
            return;

        ThumbnailProgressChanged?.Invoke(ThumbnailStep.CheckingUpdates, true);

        IEnumerable<string> filesToReplace = [];

        if (Directory.Exists(deviceInfo.LocalThumbnailDir) && deviceInfo.ThumbnailPathCache.Count > 1)
        {
            var parent = FileHelper.GetParentPath(deviceInfo.LocalThumbnailDir);
            filesToReplace = deviceInfo.ThumbnailPathCache.Where(kvp => kvp.Value.LastUpdate != DateTime.MinValue && kvp.Value.IsOverdue).Select(thumb => Path.Combine(parent, thumb.Value.Id));
        }
        else
        {
            Directory.CreateDirectory(deviceInfo.LocalThumbnailDir);
        }

        FileClass pics = deviceInfo.DeviceImageThumbnailDirs.Length > 0
            ? new("", deviceInfo.DeviceImageThumbnailDirs[0], AbstractFile.FileType.Folder)
            : new("", deviceInfo.DevicePicturesThumbnailDir, AbstractFile.FileType.Folder);

        IEnumerable<FileClass> picFolders = deviceInfo.DeviceImageThumbnailDirs.Length > 0
            ? deviceInfo.DeviceImageThumbnailDirs.Select(dir => new FileClass("", dir, AbstractFile.FileType.Folder))
            : [pics];

        FileClass movies = deviceInfo.DeviceMoviesThumbnailDir is null
            ? new("", "", AbstractFile.FileType.Unknown)
            : new("", deviceInfo.DeviceMoviesThumbnailDir, AbstractFile.FileType.Folder);

        var deviceDir = FileHelper.GetParentPath(deviceInfo.LocalThumbnailDir);
        var device = Data.DevicesObject.LogicalDeviceViewModels.FirstOrDefault(d => d.SerialNumber == deviceInfo.DeviceId);
        if (device is null)
            return;

        Task.Run(() =>
        {
            var opsList = FileActionLogic.SilentPullFiles(device, deviceDir, Data.Settings.LimitThumbsPullSpeed, filesToReplace, [.. picFolders, movies]).ToList();

            ThumbnailProgressChanged?.Invoke(ThumbnailStep.CheckingUpdates, false);

            if (opsList.Count == 0)
                return;

            ThumbnailProgressChanged?.Invoke(ThumbnailStep.Pulling, true);

            int totalFiles = opsList.Sum(op => op.FilePath.Children.Count);
            int completedFiles = 0;
            int completedOps = 0;
            int totalOps = opsList.Count;

            ThumbnailPullingProgressUpdated?.Invoke(0, totalFiles);

            foreach (var operation in opsList)
            {
                SyncFile filePath = operation.FilePath;

                NotifyCollectionChangedEventHandler collectionChangedHandler = null;
                PropertyChangedEventHandler propertyChangedHandler = null;

                collectionChangedHandler = (sender, e) =>
                {
                    e.NewItems?.OfType<AdbSyncProgressInfo>().Where(u => u.CurrentFilePercentage == 100).ForEach(update =>
                    {
                        UpdateThumbnailInfo(deviceInfo.DeviceId, FileHelper.GetFullName(update.AndroidPath));
                        int newCompleted = Interlocked.Increment(ref completedFiles);
                        ThumbnailPullingProgressUpdated?.Invoke(newCompleted, totalFiles);
                    });
                };

                propertyChangedHandler = (sender, e) =>
                {
                    if (e.PropertyName == nameof(FileOperation.Status) && operation.Status is FileOperation.OperationStatus.Completed)
                    {
                        filePath.ProgressUpdates.CollectionChanged -= collectionChangedHandler;
                        operation.PropertyChanged -= propertyChangedHandler;

                        if (Interlocked.Increment(ref completedOps) == totalOps)
                        {
                            ThumbnailProgressChanged?.Invoke(ThumbnailStep.Pulling, false);
                        }
                    }
                };

                operation.ProgressUpdates.CollectionChanged += collectionChangedHandler;
                operation.PropertyChanged += propertyChangedHandler;
            }

            DeviceThumbsToCsv(GetDeviceThumbsInfo(deviceInfo.DeviceId));
        });
    }

    private static IEnumerable<KeyValuePair<string, ThumbnailInfo>> ParseThumbnailMap(string stdout, MediaType type)
    {
        Regex re = type switch
        {
            MediaType.images => RE_IMAGE_METADATA(),
            MediaType.video => RE_VIDEO_METADATA(),
            _ => throw new NotSupportedException($"Unsupported media type: {type}"),
        };

        foreach (Match match in re.Matches(stdout))
        {
            if (!match.Success)
                continue;

            var path = match.Groups["Path"].Value.TrimEnd();
            var resX = match.Groups["ResX"];
            var resY = match.Groups["ResY"];
            
            Size? resolution = null;
            if (resX.Success && resY.Success)
            {
                resolution = new(double.Parse(resX.Value), double.Parse(resY.Value));
            }

            var dur = match.Groups["Dur"]?.Value;
            TimeSpan? duration = string.IsNullOrEmpty(dur)
                ? null
                : TimeSpan.FromMilliseconds(int.Parse(dur));
            
            var fNum = double.TryParse(match.Groups["fNum"]?.Value, out double f) ? f : (double?)null;
            var iso = int.TryParse(match.Groups["ISO"]?.Value, out int i) ? i : (int?)null;
            var exposure = double.TryParse(match.Groups["Exposure"]?.Value, out double e) ? e : (double?)null;
            var bitrate = int.TryParse(match.Groups["Bitrate"]?.Value, out int b) ? b : (int?)null;

            yield return new(path, new($"{match.Groups["ID"].Value}.jpg", type, resolution, duration, DateTime.MinValue, ".thumbnails", fNum, iso, exposure, bitrate));
        }
    }

    private static string GetThumbsFromDevice(DeviceThumbnailInfo info, MediaType media, CancellationToken cancellationToken)
    {
        ADBService.ExecuteDeviceAdbShellCommand(
                    info.PhysicalId, "content",
                    out string stdout, out _,
                    cancellationToken,
                    "query",
                    "--uri", $"content://media/external/{media}/media",
                    "--projection", $"_id:_data:resolution:{(media is MediaType.images ? "f_number:iso:exposure_time" : "duration:bitrate")}"
                );

        return stdout;
    }
}
