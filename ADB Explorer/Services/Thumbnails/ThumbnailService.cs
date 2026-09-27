namespace ADB_Explorer.Services;

public static partial class ThumbnailService
{
    private const string DCIM_THUMBNAILS = "/sdcard/DCIM/.thumbnails";
    private const string PICTURES_THUMBNAILS = "/sdcard/Pictures/.thumbnails";
    private const string MOVIES_THUMBNAILS = "/sdcard/Movies/.thumbnails";

    private static readonly (string ThumbDir, string[] MediaPrefixes)[] ImageThumbnailSources =
    [
        (DCIM_THUMBNAILS, ["/sdcard/DCIM", "/storage/emulated/0/DCIM"]),
        (PICTURES_THUMBNAILS, ["/sdcard/Pictures", "/storage/emulated/0/Pictures"]),
    ];
    
    static readonly TimeSpan MONTH = TimeSpan.FromDays(30);
    static readonly TimeSpan WEEK = TimeSpan.FromDays(7);
    static readonly TimeSpan DAY = TimeSpan.FromDays(1);
    static readonly TimeSpan HOUR = TimeSpan.FromHours(1);

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
        /// <summary>Details / Explorer view - 16px, no thumbnails</summary>
        Disabled = 0,
        /// <summary>Drive view. For files should be 48px</summary>
        Tiles = 1,
        /// <summary>Content view - 32px</summary>
        Content = 2,
        Medium = 48,
        Large = 96,
        ExtraLarge = 192,
        Drag = 256,
    }

    public const int ContentIconPixelSize = 32;

    public static int GetPixelSize(ThumbnailSize size) =>
        size is ThumbnailSize.Content ? ContentIconPixelSize : (int)size;

    public static bool IsIconLayout(ThumbnailSize size) =>
        size is ThumbnailSize.Medium or ThumbnailSize.Large or ThumbnailSize.ExtraLarge;

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

    private static readonly Mutex _mutex = new(false);
    private static readonly Mutex _listMutex = new(false);

    private static readonly List<DeviceThumbnailInfo> _deviceInfoCache = [];

    public record struct Thumbnail(BitmapSource? Image, ThumbnailInfo Info);

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
        public string DevicePicturesThumbnailDir { get; set; } = "";
        public string[] DeviceImageThumbnailDirs { get; set; } = [];
        public string? DeviceMoviesThumbnailDir { get; set; }
        public string LocalThumbnailDir { get; set; } = "";
        public bool IsProbed { get; set; }
        public bool HasThumbnailSupport { get; set; }

        /// <summary>
        /// Key: Original file path on the device, Value: Thumbnail info (including thumbnail ID which corresponds to the local thumbnail file name)
        /// </summary>
        public Dictionary<string, ThumbnailInfo> ThumbnailPathCache { get; set; } = [];
    }

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
                AdbService.PathsExist(info.PhysicalId, DCIM_THUMBNAILS, PICTURES_THUMBNAILS, MOVIES_THUMBNAILS),
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
            var pixelSize = GetPixelSize(size);
            var decodePixelWidth = Data.RuntimeSettings.MainWindowScalingFactor > 0
                ? (int)Math.Ceiling(pixelSize / Data.RuntimeSettings.MainWindowScalingFactor)
                : pixelSize * 2;

            using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var bitmapImage = new BitmapImage();
            bitmapImage.BeginInit();
            bitmapImage.StreamSource = stream;
            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
            bitmapImage.DecodePixelWidth = scaleWithDpi ? decodePixelWidth : pixelSize;
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
}
