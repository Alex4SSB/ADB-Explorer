namespace ADB_Explorer.Services;

public static partial class ThumbnailService
{
    private const string CSV_CACHE_FILE = "thumbnailInfo.csv";

    static readonly Encoding CsvEncoding = new UTF8Encoding(true);

    private static readonly ConcurrentDictionary<string, Lock> DeviceCsvLocks = new(StringComparer.Ordinal);

    [GeneratedRegex(@"Row: \d+ _id=(?<ID>\d+), _data=(?<Path>.+), resolution=(?:(?:(?<ResX>\d+).(?<ResY>\d+))|NULL), f_number=(?:(?<fNum>[\d.]+)|NULL), iso=(?:(?<ISO>\d+)|NULL), exposure_time=(?:(?<Exposure>[\d.E-]+)|NULL)", RegexOptions.Multiline)]
    private static partial Regex RE_IMAGE_METADATA();

    [GeneratedRegex(@"Row: \d+ _id=(?<ID>\d+), _data=(?<Path>.+), resolution=(?:(?:(?<ResX>\d+).(?<ResY>\d+))|NULL), duration=(?:(?<Dur>\d+)|NULL), bitrate=(?:(?<Bitrate>\d+)|NULL)", RegexOptions.Multiline)]
    private static partial Regex RE_VIDEO_METADATA();

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
        op.WhenCompleted(() =>
        {
            long? size = File.Exists(localPath) ? new FileInfo(localPath).Length : null;
            UpdateThumbnailInfo(device.SerialNumber, id, fileSize: size);
        });

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
            if (Data.DeviceCts.IsCancellationRequested)
            {
                ThumbnailProgressChanged?.Invoke(ThumbnailStep.CheckingUpdates, false);
                return;
            }

            var opsList = FileActionLogic.SilentPullFiles(device, deviceDir, Data.Settings.ThumbAndIconConcurrency, filesToReplace, [.. picFolders, movies]).ToList();

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
                NotifyCollectionChangedEventHandler collectionChangedHandler = (sender, e) =>
                {
                    e.NewItems?.OfType<AdbSyncProgressInfo>().Where(u => u.CurrentFilePercentage == 100).ForEach(update =>
                    {
                        UpdateThumbnailInfo(deviceInfo.DeviceId, FileHelper.GetFullName(update.AndroidPath));
                        int newCompleted = Interlocked.Increment(ref completedFiles);
                        ThumbnailPullingProgressUpdated?.Invoke(newCompleted, totalFiles);
                    });
                };

                operation.ProgressUpdates.CollectionChanged += collectionChangedHandler;

                // Counted however each pull ends, so one failure doesn't leave the progress showing.
                operation.WhenFinished(_ =>
                {
                    operation.ProgressUpdates.CollectionChanged -= collectionChangedHandler;

                    if (Interlocked.Increment(ref completedOps) == totalOps)
                        ThumbnailProgressChanged?.Invoke(ThumbnailStep.Pulling, false);
                });
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
        AdbService.ExecuteDeviceAdbShellCommand(
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
