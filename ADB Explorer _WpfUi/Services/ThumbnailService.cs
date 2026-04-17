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

    public record struct ThumbnailInfo(string Id, MediaType Type, Size? Resolution, TimeSpan? Duration, DateTime LastUpdate, string LocalFolder = "")
    {
        const string CsvDateFormat = "yyyy-MM-dd_HH:mm";

        public readonly string ToCsv() => $"{Id}|{Type}|{Resolution?.Width}|{Resolution?.Height}|{Duration?.TotalMilliseconds}|{(LastUpdate == DateTime.MinValue ? null : LastUpdate.ToString(CsvDateFormat))}|{LocalFolder}";

        public static ThumbnailInfo? FromCsv(string csv)
        {
            var parts = csv.Split('|');
            if (parts.Length is not 6 and not 7)
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

            string localFolder = parts.Length == 7 ? parts[6] : "";

            return new(id, type, resolution, duration, lastUpdate, localFolder);
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
        Drag = 120,
        ExtraLarge = 192,
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

    private static readonly Mutex _mutex = new(false);
    private static readonly Mutex _listMutex = new(false);

    private static readonly List<DeviceThumbnailInfo> _deviceInfoCache = [];

    public record struct Thumbnail(BitmapSource Image, ThumbnailInfo Info);

    private record struct DeviceThumbnailInfo
    {
        public DeviceThumbnailInfo(string deviceId)
        {
            DeviceId = deviceId;

            PhysicalId = Data.DevicesObject.LogicalDeviceViewModels.FirstOrDefault(d => d.LogicalID == deviceId)?.ID ?? deviceId;
        }

        /// <summary>
        /// Physical Device ID used for ADB commands.
        /// </summary>
        public string PhysicalId { get; }

        /// <summary>
        /// Logical Device ID, mDNS identifier is omitted. <br />
        /// Might still not match USB ID.
        /// </summary>
        public string DeviceId { get; init; }
        public string DevicePicturesThumbnailDir { get; set; }
        public string? DeviceMoviesThumbnailDir { get; set; }
        public string LocalThumbnailDir { get; set; }

        /// <summary>
        /// Key: Original file path on the device, Value: Thumbnail info (including thumbnail ID which corresponds to the local thumbnail file name)
        /// </summary>
        public Dictionary<string, ThumbnailInfo> ThumbnailPathCache { get; set; }
    }

    [GeneratedRegex(@"Row: \d+ _id=(?<ID>\d+), _data=(?<Path>.+), resolution=(?:(?<ResX>\d+).(?<ResY>\d+))?, duration=(?<Dur>\d+)?", RegexOptions.Multiline)]
    private static partial Regex RE_THUMBNAIL_PATH();

    private static DeviceThumbnailInfo? GetDeviceThumbsInfo(string logicalDeviceId)
    {
        if (_deviceInfoCache.FirstOrDefault(d => d.DeviceId == logicalDeviceId) is DeviceThumbnailInfo info && !string.IsNullOrEmpty(info.DeviceId))
        {
            return info;
        }

        info = new(logicalDeviceId);

        var existingDirs = ADBService.PathsExist(info.PhysicalId, DCIM_THUMBNAILS, PICTURES_THUMBNAILS, MOVIES_THUMBNAILS);
        if (existingDirs.Length == 0)
        {
            return null;
        }

        info.DevicePicturesThumbnailDir = existingDirs[0].TrimEnd('/');
        if (Data.Settings.MovieThumbsEnabled && existingDirs.Length > 1)
            info.DeviceMoviesThumbnailDir = existingDirs[1].TrimEnd('/');
        
        _deviceInfoCache.Add(info);

        return info;
    }

    public static void InvalidateThumbnailDirCache(string logicalDeviceId)
        => _deviceInfoCache.RemoveAll(d => d.DeviceId == logicalDeviceId);

    public static bool ForceLoad(LogicalDeviceViewModel device)
    {
        if (!_mutex.WaitOne(0))
            return false;

        GetThumbnailName(device.LogicalID, "");
        var localPath = GetLocalThumbPath(device);

        _mutex.ReleaseMutex();

        return localPath is not null;
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
        if (Data.Settings.MaxCustomThumbWeight == 0 || file.Type is not AbstractFile.FileType.File)
            return;

        if (file.Size is null || file.Size.Value > (long)Data.Settings.MaxCustomThumbWeight * 1024)
            return;

        if (!AdbExplorerConst.COMMON_PHOTO_EXT.Contains(file.Extension, StringComparer.InvariantCultureIgnoreCase))
            return;

        var deviceInfo = _deviceInfoCache.FirstOrDefault(d => d.DeviceId == device.LogicalID);
        if (string.IsNullOrEmpty(deviceInfo.DeviceId) || deviceInfo.ThumbnailPathCache is null)
            return;

        if (deviceInfo.ThumbnailPathCache.TryGetValue(file.FullPath, out var cachedInfo))
        {
            if (cachedInfo.LastUpdate != DateTime.MinValue)
                return;

            var localDir = string.IsNullOrEmpty(cachedInfo.LocalFolder)
                ? GetLocalThumbPath(device)
                : Path.Combine(Data.AppDataPath, deviceInfo.DeviceId, cachedInfo.LocalFolder);

            if (localDir is null)
                return;

            var source = new SyncFile(file);
            var target = SyncFile.MergeToWindowsPath(source, localDir);
            target.UpdatePath(Path.Combine(localDir, cachedInfo.Id));

            var id = cachedInfo.Id;
            var op = FileSyncOperation.PullFile(source, target, device, App.AppDispatcher);

            op.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(FileOperation.Status) && op.Status is FileOperation.OperationStatus.Completed)
                {
                    Size? resolution = cachedInfo.Resolution ?? ReadImageResolution(Path.Combine(localDir, id));
                    UpdateThumbnailInfo(deviceInfo.DeviceId, id, resolution);
                }
            };

            op.Start();
        }
        else
        {
            var targetDir = Path.Combine(Data.AppDataPath, deviceInfo.DeviceId, CUSTOM_PHOTOS_SUBFOLDER);
            Directory.CreateDirectory(targetDir);

            var noExt = file.NoExtName;
            var prefix = noExt[^Math.Min(5, noExt.Length)..];
            var id = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{prefix}.jpg";

            deviceInfo.ThumbnailPathCache[file.FullPath] = new ThumbnailInfo(id, MediaType.images, null, null, DateTime.MinValue, CUSTOM_PHOTOS_SUBFOLDER);
            UpdateCache(deviceInfo);

            SyncFile customSource = new(file);
            var customTarget = SyncFile.MergeToWindowsPath(customSource, targetDir);
            customTarget.UpdatePath(Path.Combine(targetDir, id));

            var op = FileSyncOperation.PullFile(customSource, customTarget, device, App.AppDispatcher);

            op.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(FileOperation.Status) && op.Status is FileOperation.OperationStatus.Completed)
                {
                    var resolution = ReadImageResolution(Path.Combine(targetDir, id));
                    UpdateThumbnailInfo(deviceInfo.DeviceId, id, resolution);
                }
            };

            op.Start();
        }
    }

    public static bool IsInitialized(string logicalDeviceId)
    {
        return _deviceInfoCache.FirstOrDefault(d => d.DeviceId == logicalDeviceId) is DeviceThumbnailInfo info
            && !string.IsNullOrEmpty(info.LocalThumbnailDir);
    }

    public static Thumbnail? LoadThumbnail(LogicalDeviceViewModel device, string filePath, ThumbnailSize size, bool scaleWithDpi = true)
    {
        _mutex.WaitOne();
        _mutex.ReleaseMutex();

        if (GetThumbnailName(device.LogicalID, filePath) is not ThumbnailInfo info)
            return null;

        string? localThumbnailDir = string.IsNullOrEmpty(info.LocalFolder)
            ? GetLocalThumbPath(device)
            : Path.Combine(Data.AppDataPath, device.LogicalID, info.LocalFolder);

        if (localThumbnailDir is null)
            return null;

        try
        {
            var fullPath = Path.Combine(localThumbnailDir, info.Id);
            if (!File.Exists(fullPath))
                return null;

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
            return new(bitmapImage, info);
        }
        catch
        {
            return null;
        }
    }

    public static void SaveAllThumbsToCsv()
    {
        foreach (var item in _deviceInfoCache)
        {
            DeviceThumbsToCsv(item);
        }
    }

    private static void DeviceThumbsToCsv(DeviceThumbnailInfo? deviceInfo)
    {
        if (deviceInfo is not DeviceThumbnailInfo info || info.ThumbnailPathCache is null || info.ThumbnailPathCache.Count == 0)
            return;

        var deviceDir = Path.Combine(Data.AppDataPath, info.DeviceId);
        if (!Directory.Exists(deviceDir))
            Directory.CreateDirectory(deviceDir);

        var csvLines = info.ThumbnailPathCache.Select(kvp => $"{kvp.Key}|{kvp.Value.ToCsv()}");
        var csvContent = string.Join(Environment.NewLine, csvLines);
        var savePath = Path.Combine(deviceDir, CSV_CACHE_FILE);

        File.WriteAllText(savePath, csvContent, CsvEncoding);
    }

    private static ThumbnailInfo? GetThumbnailName(string logicalDeviceId, string filePath)
    {
        if (GetDeviceThumbsInfo(logicalDeviceId) is not DeviceThumbnailInfo deviceInfo)
            return null;

        UpdateDeviceCache(deviceInfo);

        _deviceInfoCache.First(d => d.DeviceId == logicalDeviceId).ThumbnailPathCache.TryGetValue(filePath, out var thumbnailPath);

        return thumbnailPath;
    }

    private static void UpdateDeviceCache(DeviceThumbnailInfo deviceInfo)
    {
        if (deviceInfo.ThumbnailPathCache is null || deviceInfo.ThumbnailPathCache.Count == 0)
        {
            var cache = GetThumbsCacheFromCsv(deviceInfo);
            
            if (cache.Count == 0)
            {
                deviceInfo.ThumbnailPathCache = GetThumbsCacheFromDevice(deviceInfo);
                DeviceThumbsToCsv(deviceInfo);
            }
            else
            {
                deviceInfo.ThumbnailPathCache = cache;
                deviceInfo = SetDeviceLocalDir(deviceInfo);

                Task.Run(() => MergeDeviceWithLocalCache(deviceInfo));
            }

            UpdateCache(deviceInfo);
        }
    }

    private static void MergeDeviceWithLocalCache(DeviceThumbnailInfo deviceInfo)
    {
        var deviceCache = GetThumbsCacheFromDevice(deviceInfo);
        bool hasUpdates = false;

        foreach (var kvp in deviceCache)
        {
            if (deviceInfo.ThumbnailPathCache.TryAdd(kvp.Key, kvp.Value))
                hasUpdates = true;
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
        var cache = new Dictionary<string, ThumbnailInfo>();
        var csvPath = Path.Combine(Data.AppDataPath, deviceInfo.DeviceId, CSV_CACHE_FILE);
        if (!File.Exists(csvPath))
            return [];

        var lines = File.ReadAllLines(csvPath, CsvEncoding);

        foreach (var line in lines)
        {
            var parts = line.Split('|', 2);
            if (parts.Length != 2)
                continue;

            var path = parts[0];
            var infoCsv = parts[1];
            if (ThumbnailInfo.FromCsv(infoCsv) is ThumbnailInfo info)
            {
                cache.TryAdd(path, info);
            }
        }

        return cache;
    }

    private static Dictionary<string, ThumbnailInfo> GetThumbsCacheFromDevice(DeviceThumbnailInfo deviceInfo)
    {
        ThumbnailProgressChanged?.Invoke(ThumbnailStep.ReadingDatabase, true);

        var picsResponse = GetThumbsFromDevice(deviceInfo, MediaType.images, CancellationToken.None);
        var thumbnailMap = ParseThumbnailMap(picsResponse, MediaType.images).ToList();

        string moviesResponse = Data.Settings.MovieThumbsEnabled
            ? GetThumbsFromDevice(deviceInfo, MediaType.video, CancellationToken.None)
            : "";

        var moviesThumbnailMap = ParseThumbnailMap(moviesResponse, MediaType.video);

        ThumbnailProgressChanged?.Invoke(ThumbnailStep.ReadingDatabase, false);

        if (thumbnailMap is null || thumbnailMap.Count == 0)
            // Cache an almost empty dictionary to avoid repeated ADB calls
            return new([new KeyValuePair<string, ThumbnailInfo>("", new())]);
        
        IEnumerable<KeyValuePair<string, ThumbnailInfo>> combined = [.. thumbnailMap, .. moviesThumbnailMap];
        return combined.ToDictionary();
    }

    private static void UpdateThumbnailInfo(string logicalDeviceId, string id, Size? resolution = null)
    {
        if (GetDeviceThumbsInfo(logicalDeviceId) is not DeviceThumbnailInfo deviceInfo)
            return;

        var thumbnailPathCache = deviceInfo.ThumbnailPathCache;
        var existingEntry = thumbnailPathCache?.FirstOrDefault(kvp => kvp.Value.Id == id);
        string? updatedFilePath = null;
        if (existingEntry.HasValue)
        {
            var updatedEntry = existingEntry.Value.Value with
            {
                LastUpdate = DateTime.Now,
                Resolution = resolution ?? existingEntry.Value.Value.Resolution,
            };
            deviceInfo.ThumbnailPathCache[existingEntry.Value.Key] = updatedEntry;
            updatedFilePath = existingEntry.Value.Key;
        }

        UpdateCache(deviceInfo);

        if (updatedFilePath is not null)
            ThumbnailUpdated?.Invoke(logicalDeviceId, updatedFilePath);
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
        if (GetDeviceThumbsInfo(device.LogicalID) is not DeviceThumbnailInfo deviceInfo)
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

        FileClass pics = new("", deviceInfo.DevicePicturesThumbnailDir, AbstractFile.FileType.Folder);

        FileClass movies = deviceInfo.DeviceMoviesThumbnailDir is null
            ? new("", "", AbstractFile.FileType.Unknown)
            : new("", deviceInfo.DeviceMoviesThumbnailDir, AbstractFile.FileType.Folder);

        var deviceDir = FileHelper.GetParentPath(deviceInfo.LocalThumbnailDir);
        var device = Data.DevicesObject.LogicalDeviceViewModels.FirstOrDefault(d => d.LogicalID == deviceInfo.DeviceId);
        if (device is null)
            return;

        Task.Run(() =>
        {
            var opsList = FileActionLogic.SilentPullFiles(device, deviceDir, Data.Settings.LimitThumbsPullSpeed, filesToReplace, pics, movies).ToList();

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
        foreach (Match match in RE_THUMBNAIL_PATH().Matches(stdout))
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

            yield return new(path, new($"{match.Groups["ID"].Value}.jpg", type, resolution, duration, DateTime.MinValue, ".thumbnails"));
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
                    "--projection", "_id:_data:resolution:duration"
                );

        return stdout;
    }
}
