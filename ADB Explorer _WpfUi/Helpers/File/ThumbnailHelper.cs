using ADB_Explorer.Models;
using ADB_Explorer.Services;
using ADB_Explorer.Services.AppInfra;
using Vanara.Windows.Shell;

namespace ADB_Explorer.Helpers;

public static partial class ThumbnailHelper
{
    private const string DCIM_THUMBNAILS     = "/sdcard/DCIM/.thumbnails";
    private const string PICTURES_THUMBNAILS = "/sdcard/Pictures/.thumbnails";

    private static readonly Mutex _mutex = new(false);

    private static readonly List<DeviceThumbnailInfo> _deviceInfoCache = [];

    private struct DeviceThumbnailInfo
    {
        public string DeviceId { get; init; }
        public string DeviceThumbnailDir { get; init; }
        public string LocalThumbnailDir { get; set; }
        public Dictionary<string, string> ThumbnailPathCache { get; set; }
    }

    [GeneratedRegex(@"Row: \d+ _id=(?<ID>\d+), _data=(?<Path>.+)", RegexOptions.Multiline)]
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

        var existingDirs = ADBService.PathsExist(deviceId, DCIM_THUMBNAILS, PICTURES_THUMBNAILS);
        if (existingDirs.Length == 0)
        {
            _mutex.ReleaseMutex();
            return null;
        }

        string result = existingDirs[0].TrimEnd('/');

        DeviceThumbnailInfo item = new() { DeviceId = deviceId, DeviceThumbnailDir = result };
        _deviceInfoCache.Add(item);

        _mutex.ReleaseMutex();
        return item;
    }

    public static string? DeviceThumbsDir(string deviceId)
        => GetThumbnailDir(deviceId)?.DeviceThumbnailDir;

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

    public static BitmapSource? LoadThumbnail(ADBService.AdbDevice device, string filePath)
    {
        if (GetThumbnailName(device.ID, filePath) is not string thumbnailName)
            return null;

        if (GetLocalThumbPath(device) is not string localThumbnailDir)
            return null;

        try
        {
            var fullPath = Path.Combine(localThumbnailDir, thumbnailName);
            if (!File.Exists(fullPath))
                return null;

            using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            return decoder.Frames[0];
        }
        catch
        {
            return null;
        }
    }

    private static string? GetThumbnailName(string deviceId, string filePath)
    {
        if (GetThumbnailDir(deviceId) is not DeviceThumbnailInfo deviceInfo)
            return null;

        // Ensure only one thread is accessing/modifying the thumbnail path cache at a time to avoid redundant ADB calls.
        _mutex.WaitOne();

        if (deviceInfo.ThumbnailPathCache is null || deviceInfo.ThumbnailPathCache.Count == 0)
        {
            deviceInfo.ThumbnailPathCache = [];
            var stdout = GetThumbsFromDevice(deviceId, CancellationToken.None);
            var thumbnailMap = ParseThumbnailMap(stdout);

            if (thumbnailMap is null || !thumbnailMap.Any())
                // Cache an almost empty dictionary to avoid repeated ADB calls
                deviceInfo.ThumbnailPathCache = new([new KeyValuePair<string, string>("", "")]);
            else
                deviceInfo.ThumbnailPathCache = thumbnailMap.ToDictionary();

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

        FileClass file = new("", deviceInfo.DeviceThumbnailDir, AbstractFile.FileType.Folder);
        var deviceDir = Directory.CreateDirectory(Path.Combine(Data.AppDataPath, device.ID)).FullName;

        FileActionLogic.SilentPullFiles(device, deviceDir, Data.Settings.LimitThumbsPullSpeed, file);

        deviceDir = Path.Combine(deviceDir, Path.GetFileName(deviceInfo.DeviceThumbnailDir));
        deviceInfo.LocalThumbnailDir = deviceDir;

        _deviceInfoCache.RemoveAll(d => d.DeviceId == device.ID);
        _deviceInfoCache.Add(deviceInfo);

        _mutex.ReleaseMutex();

        return deviceDir;
    }

    private static IEnumerable<KeyValuePair<string, string>> ParseThumbnailMap(string stdout)
    {
        foreach (Match match in RE_THUMBNAIL_PATH().Matches(stdout))
        {
            if (!match.Success)
                continue;

            var path = match.Groups["Path"].Value.TrimEnd();
            yield return new(path, $"{match.Groups["ID"].Value}.jpg");
        }
    }

    private static string GetThumbsFromDevice(string deviceId, CancellationToken cancellationToken)
    {
        ADBService.ExecuteDeviceAdbShellCommand(
                    deviceId, "content",
                    out string stdout, out _,
                    cancellationToken,
                    "query",
                    "--uri", "content://media/external/images/media",
                    "--projection", "_id:_data"
                );

        return stdout;
    }
}