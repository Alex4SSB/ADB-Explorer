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

    private const string MEDIA_SYNC_FILE = "mediaSync.txt";

    /// <summary>Device time read right before a media DB query - saved as the sync time once the thumbnails it found are pulled.</summary>
    private static readonly ConcurrentDictionary<string, long> PendingMediaSync = new(StringComparer.Ordinal);

    /// <summary>The device Unix time up to which the device's media DB entries and thumbnail files are all in the local cache.</summary>
    private static long? ReadMediaSyncTime(string deviceId)
    {
        try
        {
            var path = Path.Combine(Data.AppDataPath, deviceId, MEDIA_SYNC_FILE);
            return File.Exists(path) && long.TryParse(File.ReadAllText(path).Trim(), out var time) ? time : null;
        }
        catch
        {
            return null;
        }
    }

    private static void CommitMediaSync(string deviceId)
    {
        if (!PendingMediaSync.TryRemove(deviceId, out var time))
            return;

        try
        {
            var deviceDir = Path.Combine(Data.AppDataPath, deviceId);
            Directory.CreateDirectory(deviceDir);
            File.WriteAllText(Path.Combine(deviceDir, MEDIA_SYNC_FILE), time.ToString());
        }
        catch
        { }
    }

    private static void ClearMediaSync(string deviceId)
    {
        PendingMediaSync.TryRemove(deviceId, out _);

        try
        {
            File.Delete(Path.Combine(Data.AppDataPath, deviceId, MEDIA_SYNC_FILE));
        }
        catch
        { }
    }

    private static void UpdateDeviceCache(DeviceThumbnailInfo deviceInfo)
    {
        if (deviceInfo.ThumbnailPathCache is not null)
            return;

        var cache = GetThumbsCacheFromCsv(deviceInfo);

        if (cache.Count == 0)
        {
            // Nothing cached locally, so nothing from an earlier sync can be skipped
            ClearMediaSync(deviceInfo.DeviceId);

            var fromDevice = deviceInfo.HasThumbnailSupport
                ? GetThumbsCacheFromDevice(deviceInfo, null)
                : [];

            // An almost empty dictionary when the device has no media, to avoid repeated ADB calls
            deviceInfo.ThumbnailPathCache = fromDevice.Count > 0 || !deviceInfo.HasThumbnailSupport
                ? fromDevice
                : new([new KeyValuePair<string, ThumbnailInfo>("", new())]);

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
        if (!deviceInfo.HasThumbnailSupport || deviceInfo.ThumbnailPathCache is not { } cache)
            return;

        // Only what changed since the last sync - the rest is already in the cache read from the CSV
        var since = ReadMediaSyncTime(deviceInfo.DeviceId);
        var deviceCache = GetThumbsCacheFromDevice(deviceInfo, since);

        foreach (var kvp in deviceCache)
        {
            // The device's thumbnail replaces an entry with only the data read when the file was pushed, keeping that data
            if (cache.TryGetValue(kvp.Key, out var pushed) && pushed.IsInfoOnly)
                cache[kvp.Key] = kvp.Value.WithMediaInfo(pushed);
            else
                cache.TryAdd(kvp.Key, kvp.Value);
        }

        UpdateCache(deviceInfo);
        DeviceCacheMerged?.Invoke(deviceInfo.DeviceId);
        PullThumbnails(deviceInfo, since);
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

    /// <param name="since">Reads only media added or modified after this device Unix time, or all of it when <see langword="null"/>.</param>
    private static Dictionary<string, ThumbnailInfo> GetThumbsCacheFromDevice(DeviceThumbnailInfo deviceInfo, long? since)
    {
        ThumbnailProgressChanged?.Invoke(ThumbnailStep.ReadingDatabase, true);

        // Read before the query, so media added while it runs is caught by the next one
        if (AdbService.GetDeviceUnixTime(deviceInfo.PhysicalId) is long now)
        {
            PendingMediaSync[deviceInfo.DeviceId] = now;

            // The device clock went back - the sync time can't be trusted
            if (since > now)
                since = null;
        }

        // Not DeviceCts: every navigation cancels it, which would cut off the video query and cache an empty result
        var ct = CancellationToken.None;
        var picsResponse = GetThumbsFromDevice(deviceInfo, MediaType.images, since, ct);
        var thumbnailMap = ParseThumbnailMap(picsResponse, MediaType.images);

        string moviesResponse = Data.Settings.MovieThumbsEnabled
            ? GetThumbsFromDevice(deviceInfo, MediaType.video, since, ct)
            : "";

        var moviesThumbnailMap = ParseThumbnailMap(moviesResponse, MediaType.video);

        ThumbnailProgressChanged?.Invoke(ThumbnailStep.ReadingDatabase, false);

        IEnumerable<KeyValuePair<string, ThumbnailInfo>> entries = [.. thumbnailMap, .. moviesThumbnailMap];
        return entries.ToDictionary();
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

    /// <param name="modifiedAfter">The last sync time: thumbnails older than it are already pulled, so only newer ones are listed.</param>
    private static void PullThumbnails(DeviceThumbnailInfo deviceInfo, long? modifiedAfter = null)
    {
        if (!deviceInfo.HasThumbnailSupport)
            return;

        var device = Data.DevicesObject.LogicalDeviceViewModels.FirstOrDefault(d => d.SerialNumber == deviceInfo.DeviceId);
        if (device is null)
            return;

        ThumbnailProgressChanged?.Invoke(ThumbnailStep.CheckingUpdates, true);

        List<string> filesToReplace = [];

        if (Directory.Exists(deviceInfo.LocalThumbnailDir) && deviceInfo.ThumbnailPathCache is { Count: > 1 } cache)
        {
            var parent = FileHelper.GetParentPath(deviceInfo.LocalThumbnailDir);
            filesToReplace = [.. cache.Where(kvp => kvp.Value.LastUpdate != DateTime.MinValue && kvp.Value.IsOverdue).Select(thumb => Path.Combine(parent, thumb.Value.Id))];
        }
        else
        {
            Directory.CreateDirectory(deviceInfo.LocalThumbnailDir);

            // Nothing pulled yet, whatever the sync time says
            modifiedAfter = null;
        }

        // Overdue thumbnails are older than the sync time, so only a full listing finds them
        if (filesToReplace.Count > 0)
            modifiedAfter = null;

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

        Task.Run(() =>
        {
            var token = Data.DeviceCts.Token;
            if (token.IsCancellationRequested)
            {
                ThumbnailProgressChanged?.Invoke(ThumbnailStep.CheckingUpdates, false);
                return;
            }

            var opsList = FileActionLogic.SilentPullFiles(device, deviceDir, Data.Settings.ThumbAndIconConcurrency, filesToReplace, modifiedAfter, [.. picFolders, movies]).ToList();

            ThumbnailProgressChanged?.Invoke(ThumbnailStep.CheckingUpdates, false);

            // A navigation cut the listing short - it may have missed thumbnails, so the sync time stays
            if (token.IsCancellationRequested)
                return;

            if (opsList.Count == 0)
            {
                CommitMediaSync(deviceInfo.DeviceId);
                return;
            }

            ThumbnailProgressChanged?.Invoke(ThumbnailStep.Pulling, true);

            int totalFiles = opsList.Sum(op => op.FilePath.Children.Count);
            int completedFiles = 0;
            int completedOps = 0;
            int failedOps = 0;
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
                operation.WhenFinished(status =>
                {
                    operation.ProgressUpdates.CollectionChanged -= collectionChangedHandler;

                    if (status is not FileOperation.OperationStatus.Completed)
                        Interlocked.Increment(ref failedOps);

                    if (Interlocked.Increment(ref completedOps) != totalOps)
                        return;

                    ThumbnailProgressChanged?.Invoke(ThumbnailStep.Pulling, false);

                    // A failed pull would be skipped by the next listing, which starts at the sync time
                    if (Volatile.Read(ref failedOps) == 0)
                        CommitMediaSync(deviceInfo.DeviceId);
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

    private static string GetThumbsFromDevice(DeviceThumbnailInfo info, MediaType media, long? since, CancellationToken cancellationToken)
    {
        string[] args =
        [
            "query",
            "--uri", $"content://media/external/{media}/media",
            "--projection", $"_id:_data:resolution:{(media is MediaType.images ? "f_number:iso:exposure_time" : "duration:bitrate")}",
        ];

        // date_added too: a file pushed with its original date has an old date_modified
        if (since is long time)
            args = [.. args, "--where", $"'date_added>{time} OR date_modified>{time}'"];

        AdbService.ExecuteDeviceAdbShellCommand(info.PhysicalId, "content", out string stdout, out _, cancellationToken, args);

        return stdout;
    }
}
