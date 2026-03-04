using ADB_Explorer.Models;
using ADB_Explorer.Services;

namespace ADB_Explorer.Helpers;

public static partial class ThumbnailHelper
{
    private const string DCIM_THUMBNAILS    = "/sdcard/DCIM/.thumbnails";
    private const string PICTURES_THUMBNAILS = "/sdcard/Pictures/.thumbnails";

    // Keyed by device serial — cleared when device disconnects
    private static readonly Dictionary<string, string> _thumbnailDirCache = [];

    [GeneratedRegex(@"(?<!\w)_id=(?<id>\d+)")]
    private static partial Regex IdPattern();

    [GeneratedRegex(@"_display_name=(?<name>.+?)(?=,\s+\w+=|[\r\n]|$)")]
    private static partial Regex DisplayNamePattern();

    /// <summary>
    /// Detects and caches which thumbnail directory the device uses.
    /// Returns null if neither candidate exists.
    /// </summary>
    public static string? GetThumbnailDir(string deviceId)
    {
        if (_thumbnailDirCache.TryGetValue(deviceId, out var cached))
            return cached;

        var existingDirs = ADBService.PathsExist(deviceId, [DCIM_THUMBNAILS, PICTURES_THUMBNAILS]);
        if (existingDirs.Length == 0)
        {
            return null;
        }

        _thumbnailDirCache.Add(deviceId, existingDirs[0]);
        return existingDirs[0];
    }

    public static void InvalidateThumbnailDirCache(string deviceId)
        => _thumbnailDirCache.Remove(deviceId);

    // Keyed by device serial, then by file path
    private static readonly Dictionary<string, Dictionary<string, string>> _thumbnailPathCache = [];

    public static string? GetThumbnailPath(string deviceId, FileClass file)
    {
        if (!_thumbnailPathCache.TryGetValue(deviceId, out var deviceCache))
        {
            deviceCache = [];
            _thumbnailPathCache.Add(deviceId, deviceCache);
        }

        if (!deviceCache.ContainsKey(file.ParentPath))
        {
            var thumbnailMap = GetThumbnailMap(deviceId, file.ParentPath, CancellationToken.None).Append(new(file.ParentPath, null));
            if (_thumbnailPathCache[deviceId] is var dict && dict.Count > 0)
                _thumbnailPathCache[deviceId] = dict.AppendRange(thumbnailMap).ToDictionary();
            else
                _thumbnailPathCache[deviceId] = thumbnailMap.ToDictionary();
        }

        _thumbnailPathCache[deviceId].TryGetValue(file.FullPath, out var thumbnailPath);
        return thumbnailPath;
    }

    /// <summary>
    /// Returns a map of display_name → thumbnail path for all visible files in a folder.
    /// Executes exactly one ADB call regardless of the number of visible files.
    /// </summary>
    /// <param name="deviceId">ADB device serial.</param>
    /// <param name="deviceFolderPath">Current folder on the device, e.g. /sdcard/DCIM/Camera/</param>
    public static IEnumerable<KeyValuePair<string, string>> GetThumbnailMap(
        string deviceId,
        string deviceFolderPath,
        CancellationToken cancellationToken)
    {
        var relativePath = ToMediaStoreRelativePath(deviceFolderPath);
        if (relativePath is null)
            yield break;

        var thumbnailDir = GetThumbnailDir(deviceId);
        if (thumbnailDir is null)
            yield break;

        ADBService.ExecuteDeviceAdbShellCommand(
            deviceId, "content",
            out string stdout, out _,
            cancellationToken,
            "query",
            "--uri", "content://media/external/images/media",
            "--projection", "_id:_display_name",
            "--where", $"relative_path=\\'{relativePath}\\'"
        );

        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!line.StartsWith("Row:"))
                continue;

            var idMatch   = IdPattern().Match(line);
            var nameMatch = DisplayNamePattern().Match(line);

            if (!idMatch.Success || !nameMatch.Success)
                continue;

            var displayName = nameMatch.Groups["name"].Value.Trim();

            yield return new(FileHelper.ConcatPaths(deviceFolderPath, displayName), FileHelper.ConcatPaths(thumbnailDir, idMatch.Groups["id"].Value + ".jpg"));
        }
    }

    /// <summary>
    /// Strips the internal storage root prefix from a device path to produce
    /// a MediaStore-compatible relative_path (with trailing slash).
    /// e.g. /sdcard/DCIM/Camera/ → DCIM/Camera/
    /// </summary>
    private static string ToMediaStoreRelativePath(string devicePath)
    {
        var currentDrive = DriveHelper.GetCurrentDrive(devicePath);
        if (currentDrive.Type is not AbstractDrive.DriveType.Internal and not AbstractDrive.DriveType.External)
            return null; // Not on internal storage — no MediaStore thumbnails

        return devicePath[currentDrive.Path.Length..].Trim('/') + '/';
    }
}