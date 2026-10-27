using Vanara.PInvoke;
using Vanara.Windows.Shell;

namespace ADB_Explorer.Services;

public static partial class ThumbnailService
{
    /// <summary>
    /// Records the image or video data (dimensions, exposure, duration...) of what was just pushed from Windows,
    /// which the device's media database may not have, read from the Windows files while they're still there.
    /// </summary>
    public static void RecordPushedMediaInfo(LogicalDeviceViewModel device, SyncFile source, SyncFile target)
    {
        if (Data.Settings.ThumbsMode is AppSettings.ThumbnailMode.Off)
            return;

        var (windowsPath, androidPath, isDirectory) = (source.FullPath, target.FullPath, source.IsDirectory);
        var (drivePath, linkTarget) = (Data.CurrentDrive?.Path, Data.CurrentDrive?.LinkTargetPath);
        var serialNumber = device.SerialNumber;

        // Counted before it starts, so a drop's staging isn't deleted from under it
        Interlocked.Increment(ref _pushedMediaReads);
        Task.Run(() =>
        {
            try
            {
                List<(string Path, ThumbnailInfo Info)> pushed = [];
                foreach (var (local, remote) in PushedMediaFiles(windowsPath, androidPath, isDirectory))
                {
                    if (ReadMediaInfo(local) is not { } info)
                        continue;

                    // Keyed as listed and as the media database names it, like the custom thumbnails
                    pushed.Add((remote, info));
                    if (linkTarget is not null && drivePath is not null && remote.StartsWith(drivePath, StringComparison.Ordinal))
                        pushed.Add((remote.Replace(drivePath, linkTarget), info));
                }

                if (pushed.Count > 0)
                    RecordMediaInfo(serialNumber, pushed);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // The files went away meanwhile
            }
            finally
            {
                if (Interlocked.Decrement(ref _pushedMediaReads) == 0)
                    PushedMediaRead?.Invoke();
            }
        });
    }

    private static int _pushedMediaReads;

    /// <summary>Whether files just pushed are still being read for their data.</summary>
    public static bool IsReadingPushedMedia => Volatile.Read(ref _pushedMediaReads) > 0;

    /// <summary>Raised (on any thread) when no pushed files are being read anymore.</summary>
    public static event Action? PushedMediaRead;

    /// <summary>The media files pushed from <paramref name="windowsPath"/> (itself, or those under it), with their device paths.</summary>
    internal static IEnumerable<(string Local, string Remote)> PushedMediaFiles(string windowsPath, string androidPath, bool isDirectory)
    {
        if (!isDirectory)
        {
            if (File.Exists(windowsPath) && GetPerceivedMediaType(Path.GetExtension(windowsPath)) is not null)
                yield return (windowsPath, androidPath);

            yield break;
        }

        // A folder in a zip or on a phone has no files to read here
        if (!Directory.Exists(windowsPath))
            yield break;

        var options = new System.IO.EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        foreach (var file in Directory.EnumerateFiles(windowsPath, "*", options))
        {
            if (GetPerceivedMediaType(Path.GetExtension(file)) is null)
                continue;

            yield return (file, FileHelper.ConcatPaths(androidPath, Path.GetRelativePath(windowsPath, file).Replace('\\', '/')));
        }
    }

    private static readonly ConcurrentDictionary<string, MediaType?> PerceivedMediaTypes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether Windows perceives files with <paramref name="extension"/> as images or videos (null for neither).</summary>
    internal static MediaType? GetPerceivedMediaType(string extension)
    {
        if (string.IsNullOrEmpty(extension))
            return null;

        return PerceivedMediaTypes.GetOrAdd(extension, static ext =>
        {
            try
            {
                using var key = Registry.ClassesRoot.OpenSubKey(ext);
                using var system = Registry.ClassesRoot.OpenSubKey($@"SystemFileAssociations\{ext}");
                var perceived = key?.GetValue("PerceivedType") as string ?? system?.GetValue("PerceivedType") as string;

                return perceived?.ToLowerInvariant() switch
                {
                    "image" => MediaType.images,
                    "video" => MediaType.video,
                    _ => null,
                };
            }
            catch (Exception e) when (e is System.Security.SecurityException or IOException or UnauthorizedAccessException)
            {
                return null;
            }
        });
    }

    /// <summary>
    /// The data Windows reads from the image or video at <paramref name="windowsPath"/>, as an entry with no thumbnail,
    /// or null when it's neither or Windows reads nothing from it.
    /// </summary>
    internal static ThumbnailInfo? ReadMediaInfo(string windowsPath)
    {
        if (GetPerceivedMediaType(Path.GetExtension(windowsPath)) is not { } type)
            return null;

        try
        {
            using var item = ShellItem.Open(windowsPath);
            var properties = item.Properties;

            // What Windows doesn't know reads as nothing or 0
            double? positive(Ole32.PROPERTYKEY key)
            {
                if (!properties.TryGetValue<object>(key, out var value) || value is not IConvertible convertible)
                    return null;

                var number = convertible.ToDouble(CultureInfo.InvariantCulture);

                return number > 0 ? number : null;
            }

            double? width, height;
            if (type is MediaType.video)
            {
                width = positive(Ole32.PROPERTYKEY.System.Video.FrameWidth);
                height = positive(Ole32.PROPERTYKEY.System.Video.FrameHeight);
            }
            else
            {
                width = positive(Ole32.PROPERTYKEY.System.Image.HorizontalSize);
                height = positive(Ole32.PROPERTYKEY.System.Image.VerticalSize);
            }

            Size? resolution = null;
            if (width is { } x && height is { } y)
                resolution = new(x, y);

            // In 100 ns units, as ticks are
            TimeSpan? duration = null;
            if (positive(Ole32.PROPERTYKEY.System.Media.Duration) is { } ticks)
                duration = TimeSpan.FromTicks((long)ticks);

            var info = new ThumbnailInfo("", type, resolution, duration, DateTime.MinValue,
                FNumber: positive(Ole32.PROPERTYKEY.System.Photo.FNumber),
                ISO: (int?)positive(Ole32.PROPERTYKEY.System.Photo.ISOSpeed),
                ExposureTime: positive(Ole32.PROPERTYKEY.System.Photo.ExposureTime),
                Bitrate: (int?)positive(Ole32.PROPERTYKEY.System.Video.TotalBitrate));

            if (info == new ThumbnailInfo("", type, null, null, DateTime.MinValue))
                return null;

            return info;
        }
        catch (Exception e) when (e is COMException or IOException or UnauthorizedAccessException or ArgumentException or InvalidCastException)
        {
            return null;
        }
    }

    /// <summary>
    /// Adds the data of the pushed files to their cache entries (the pushed file's data wins), or keeps it in entries
    /// of its own until the device's media database or a custom thumbnail gives them a thumbnail.
    /// </summary>
    private static void RecordMediaInfo(string serialNumber, List<(string Path, ThumbnailInfo Info)> pushed)
    {
        if (GetDeviceThumbsInfo(serialNumber) is not DeviceThumbnailInfo deviceInfo)
            return;

        UpdateDeviceCache(deviceInfo);

        deviceInfo = GetCachedDeviceInfo(serialNumber);
        if (string.IsNullOrEmpty(deviceInfo.DeviceId))
            return;

        lock (GetDeviceCsvLock(deviceInfo.DeviceId))
        {
            deviceInfo = GetCachedDeviceInfo(serialNumber);
            if (deviceInfo.ThumbnailPathCache is null)
                return;

            foreach (var (path, info) in pushed)
            {
                deviceInfo.ThumbnailPathCache[path] = deviceInfo.ThumbnailPathCache.TryGetValue(path, out var existing) && !existing.IsInfoOnly
                    ? existing.WithMediaInfo(info)
                    : info;
            }

            UpdateCache(deviceInfo);
            WriteThumbsCacheToCsvFile(deviceInfo.DeviceId, deviceInfo.ThumbnailPathCache);
        }

        foreach (var (path, _) in pushed)
            ThumbnailUpdated?.Invoke(serialNumber, path);
    }
}
