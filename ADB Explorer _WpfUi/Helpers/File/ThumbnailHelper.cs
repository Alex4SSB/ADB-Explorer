using ADB_Explorer.Models;
using ADB_Explorer.Services;
using ADB_Explorer.Services.AppInfra;

namespace ADB_Explorer.Helpers;

public static partial class ThumbnailHelper
{
    public enum MediaType
    {
        images,
        video,
    }

    public record struct ThumbnailInfo(string Id, MediaType Type, Size? Resolution, TimeSpan? Duration);

    public record struct Thumbnail(BitmapSource Image, ThumbnailInfo Info);

    private const string DCIM_THUMBNAILS     = "/sdcard/DCIM/.thumbnails";
    private const string PICTURES_THUMBNAILS = "/sdcard/Pictures/.thumbnails";
    private const string MOVIES_THUMBNAILS   = "/sdcard/Movies/.thumbnails";

    private static readonly Mutex _mutex = new(false);

    private static readonly List<DeviceThumbnailInfo> _deviceInfoCache = [];

    private struct DeviceThumbnailInfo
    {
        public string DeviceId { get; init; }
        public string DevicePicturesThumbnailDir { get; init; }
        public string? DeviceMoviesThumbnailDir { get; init; }
        public string LocalThumbnailDir { get; set; }
        public Dictionary<string, ThumbnailInfo> ThumbnailPathCache { get; set; }
    }

    [GeneratedRegex(@"Row: \d+ _id=(?<ID>\d+), _data=(?<Path>.+), resolution=(?:(?<ResX>\d+).(?<ResY>\d+))?, duration=(?<Dur>\d+)?", RegexOptions.Multiline)]
    private static partial Regex RE_THUMBNAIL_PATH();

    /// <summary>
    /// Detects and caches which thumbnail directory the device uses.
    /// Returns null if neither candidate exists.
    /// </summary>
    private static DeviceThumbnailInfo? GetThumbnailDir(string deviceId)
    {
        // Ensure only one thread is probing the device at a time to avoid redundant ADB calls.
        _mutex.WaitOne();

        if (_deviceInfoCache.FirstOrDefault(d => d.DeviceId == deviceId) is DeviceThumbnailInfo info && !string.IsNullOrEmpty(info.DeviceId))
        {
            _mutex.ReleaseMutex();
            return info;
        }

        var existingDirs = ADBService.PathsExist(deviceId, DCIM_THUMBNAILS, PICTURES_THUMBNAILS, MOVIES_THUMBNAILS);
        if (existingDirs.Length == 0)
        {
            _mutex.ReleaseMutex();
            return null;
        }

        var pics = existingDirs[0].TrimEnd('/');
        var movies = Data.Settings.MovieThumbsEnabled && existingDirs.Length > 1
            ? existingDirs[1].TrimEnd('/')
            : null;

        DeviceThumbnailInfo item = new()
        { 
            DeviceId = deviceId, 
            DevicePicturesThumbnailDir = pics,
            DeviceMoviesThumbnailDir = movies,
        };

        _deviceInfoCache.Add(item);

        _mutex.ReleaseMutex();
        return item;
    }

    public static string? DeviceThumbsDir(string deviceId)
        => GetThumbnailDir(deviceId)?.DevicePicturesThumbnailDir;

    public static void InvalidateThumbnailDirCache(string deviceId)
        => _deviceInfoCache.RemoveAll(d => d.DeviceId == deviceId);

    public static bool ForceLoad(ADBService.AdbDevice device)
    {
        GetThumbnailName(device.ID, "");

        return GetLocalThumbPath(device) is not null;
    }

    public static bool IsInitialized(string deviceId)
    {
        return _deviceInfoCache.FirstOrDefault(d => d.DeviceId == deviceId) is DeviceThumbnailInfo info
            && !string.IsNullOrEmpty(info.LocalThumbnailDir);
    }

    public static Thumbnail? LoadThumbnail(ADBService.AdbDevice device, string filePath)
    {
        if (GetThumbnailName(device.ID, filePath) is not ThumbnailInfo entry)
            return null;

        if (GetLocalThumbPath(device) is not string localThumbnailDir)
            return null;

        try
        {
            var fullPath = Path.Combine(localThumbnailDir, entry.Id);
            if (!File.Exists(fullPath))
                return null;

            using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            return new(decoder.Frames[0], entry);
        }
        catch
        {
            return null;
        }
    }

    private static ThumbnailInfo? GetThumbnailName(string deviceId, string filePath)
    {
        if (GetThumbnailDir(deviceId) is not DeviceThumbnailInfo deviceInfo)
            return null;

        // Ensure only one thread is accessing/modifying the thumbnail path cache at a time to avoid redundant ADB calls.
        _mutex.WaitOne();

        if (deviceInfo.ThumbnailPathCache is null || deviceInfo.ThumbnailPathCache.Count == 0)
        {
            deviceInfo.ThumbnailPathCache = [];
            var picsResponse = GetThumbsFromDevice(deviceId, MediaType.images, CancellationToken.None);
            var thumbnailMap = ParseThumbnailMap(picsResponse, MediaType.images).ToList();

            string moviesResponse = Data.Settings.MovieThumbsEnabled
                ? GetThumbsFromDevice(deviceId, MediaType.video, CancellationToken.None)
                : "";

            var moviesThumbnailMap = ParseThumbnailMap(moviesResponse, MediaType.video);

            if (thumbnailMap is null || !thumbnailMap.Any())
                // Cache an almost empty dictionary to avoid repeated ADB calls
                deviceInfo.ThumbnailPathCache = new([new KeyValuePair<string, ThumbnailInfo>("", new())]);
            else
            {
                IEnumerable<KeyValuePair<string, ThumbnailInfo>> combined = [.. thumbnailMap, .. moviesThumbnailMap];
                deviceInfo.ThumbnailPathCache = combined.ToDictionary();
            }

            _deviceInfoCache.RemoveAll(d => d.DeviceId == deviceId);
            _deviceInfoCache.Add(deviceInfo);
        }

        _mutex.ReleaseMutex();

        deviceInfo.ThumbnailPathCache.TryGetValue(filePath, out var thumbnailPath);
        return thumbnailPath;
    }

    private static string? GetLocalThumbPath(ADBService.AdbDevice device)
    {
        if (GetThumbnailDir(device.ID) is not DeviceThumbnailInfo deviceInfo)
            return null;

        _mutex.WaitOne();

        if (!string.IsNullOrEmpty(deviceInfo.LocalThumbnailDir))
        {
            _mutex.ReleaseMutex();
            return deviceInfo.LocalThumbnailDir;
        }

        FileClass pics = new ("", deviceInfo.DevicePicturesThumbnailDir, AbstractFile.FileType.Folder);

        FileClass movies = deviceInfo.DeviceMoviesThumbnailDir is null
            ? new("", "", AbstractFile.FileType.Unknown)
            : new("", deviceInfo.DeviceMoviesThumbnailDir, AbstractFile.FileType.Folder);

        var deviceDir = Directory.CreateDirectory(Path.Combine(Data.AppDataPath, device.ID)).FullName;

        FileActionLogic.SilentPullFiles(device, deviceDir, Data.Settings.LimitThumbsPullSpeed, pics, movies);

        deviceDir = Path.Combine(deviceDir, Path.GetFileName(deviceInfo.DevicePicturesThumbnailDir));
        deviceInfo.LocalThumbnailDir = deviceDir;

        _deviceInfoCache.RemoveAll(d => d.DeviceId == device.ID);
        _deviceInfoCache.Add(deviceInfo);

        _mutex.ReleaseMutex();

        return deviceDir;
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

            yield return new(path, new($"{match.Groups["ID"].Value}.jpg", type, resolution, duration));
        }
    }

    private static string GetThumbsFromDevice(string deviceId, MediaType media, CancellationToken cancellationToken)
    {
        ADBService.ExecuteDeviceAdbShellCommand(
                    deviceId, "content",
                    out string stdout, out _,
                    cancellationToken,
                    "query",
                    "--uri", $"content://media/external/{media}/media",
                    "--projection", "_id:_data:resolution:duration"
                );

        return stdout;
    }
}