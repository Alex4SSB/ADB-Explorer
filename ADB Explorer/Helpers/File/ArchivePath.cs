using ADB_Explorer.Models;
using ADB_Explorer.Services;

namespace ADB_Explorer.Helpers;

/// <summary>
/// Composite device paths: <c>{archivePath}/{internalPath}</c>.
/// The archive file is the first path segment whose name matches a supported archive extension
/// and <c>stat</c> reports as a regular file (not a directory named e.g. <c>folder.zip</c>).
/// A trailing slash after that segment denotes the archive root.
/// </summary>
public static class ArchivePath
{
    private static readonly ConcurrentDictionary<string, DevicePathKind> PathKindCache = new(StringComparer.Ordinal);

    internal static Func<string?, string, bool?>? TestResolveIsRegularFile { get; set; }

    internal static void ClearCachesForTests() => PathKindCache.Clear();

    /// <summary>
    /// Drops cached <c>stat</c> file/directory classifications so archive boundaries are
    /// re-evaluated. Must be called whenever the device tree may have changed (navigation,
    /// refresh) to avoid treating a directory as an archive after a same-named file was replaced.
    /// </summary>
    public static void InvalidateCache() => PathKindCache.Clear();

    public static string NormalizeInternal(string? internalPath)
    {
        if (string.IsNullOrEmpty(internalPath))
            return "";

        var normalized = internalPath.Trim('/');
        // Toybox/GNU tar may list members with a "./" prefix; strip for stable paths.
        while (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized[2..].TrimStart('/');

        return normalized;
    }

    public static bool TryParse(string path, out string archivePath, out string internalPath, string? deviceId = null)
    {
        archivePath = "";
        internalPath = "";

        if (!TryFindArchiveEnd(path, ResolveDeviceId(deviceId), out var archiveEnd))
            return false;

        archivePath = path[..archiveEnd];

        if (path.Length == archiveEnd || path[archiveEnd] != '/')
            return false;

        internalPath = NormalizeInternal(path[(archiveEnd + 1)..]);
        return true;
    }

    public static bool IsArchivePath(string path, string? deviceId = null) => TryParse(path, out _, out _, deviceId);

    public static string Join(string archivePath, string internalPath)
    {
        archivePath = archivePath.TrimEnd('/');
        internalPath = NormalizeInternal(internalPath);
        return string.IsNullOrEmpty(internalPath)
            ? $"{archivePath}/"
            : $"{archivePath}/{internalPath}";
    }

    public static string GetArchivePath(string compositePath, string? deviceId = null)
        => TryParse(compositePath, out var archive, out _, deviceId) ? archive : compositePath;

    public static string GetParent(string compositePath, string? deviceId = null)
    {
        if (!TryParse(compositePath, out var archivePath, out var internalPath, deviceId))
            return FileHelper.GetParentPath(compositePath, deviceId);

        if (string.IsNullOrEmpty(internalPath))
            return FileHelper.GetParentPath(archivePath, deviceId);

        var lastSep = internalPath.LastIndexOf('/');
        return lastSep < 0
            ? Join(archivePath, "")
            : Join(archivePath, internalPath[..lastSep]);
    }

    public static string GetBreadcrumbLabel(string compositePath, string? deviceId = null)
    {
        if (!TryParse(compositePath, out var archivePath, out var internalPath, deviceId))
            return FileHelper.GetFullName(compositePath);

        return string.IsNullOrEmpty(internalPath)
            ? FileHelper.GetFullName(archivePath)
            : FileHelper.GetFullName(internalPath);
    }

    public static string FormatDetailsLocation(string path, string? deviceId = null)
    {
        if (string.IsNullOrEmpty(path) || !TryParse(path, out var archivePath, out var internalPath, deviceId))
            return path;

        return string.IsNullOrEmpty(internalPath)
            ? archivePath
            : $"{archivePath}\n→ {internalPath}";
    }

    private static string? ResolveDeviceId(string? deviceId)
        => deviceId ?? Data.DevicesObject?.Current?.ID;

    public static bool IsBrowsableArchiveFile(string path, string? deviceId = null)
        => ArchiveHelper.GetFamily(FileHelper.GetFullName(path)) is not ArchiveFamily.None
        && GetPathKind(deviceId, path) is DevicePathKind.RegularFile;

    private static DevicePathKind GetPathKind(string? deviceId, string candidatePath)
    {
        if (TestResolveIsRegularFile is { } resolver)
        {
            return resolver(deviceId, candidatePath) switch
            {
                true => DevicePathKind.RegularFile,
                false => DevicePathKind.Directory,
                _ => DevicePathKind.Unknown,
            };
        }

        deviceId = ResolveDeviceId(deviceId);
        if (deviceId is null)
            return DevicePathKind.Unknown;

        var cacheKey = $"{deviceId}\0{candidatePath}";
        if (PathKindCache.TryGetValue(cacheKey, out var cached) && cached is not DevicePathKind.Unknown)
            return cached;

        DevicePathKind? probed;
        try
        {
            probed = ADBService.TryGetPathKind(deviceId, candidatePath);
        }
        catch
        {
            probed = null;
        }

        var kind = probed ?? DevicePathKind.Unknown;

        if (kind is not DevicePathKind.Unknown)
            PathKindCache[cacheKey] = kind;

        return kind;
    }

    private static bool TryFindArchiveEnd(string path, string? deviceId, out int archiveEndIndex)
    {
        archiveEndIndex = -1;
        if (string.IsNullOrEmpty(path))
            return false;

        var start = path[0] == '/' ? 1 : 0;
        for (var i = start; i <= path.Length;)
        {
            var slashIndex = path.IndexOf('/', i);
            var segmentEnd = slashIndex >= 0 ? slashIndex : path.Length;
            var segment = path[i..segmentEnd];

            if (!string.IsNullOrEmpty(segment)
                && ArchiveHelper.GetFamily(segment) is not ArchiveFamily.None)
            {
                var candidatePath = path[..segmentEnd];
                if (GetPathKind(deviceId, candidatePath) is DevicePathKind.RegularFile)
                {
                    archiveEndIndex = segmentEnd;
                    return true;
                }
            }

            if (slashIndex < 0)
                break;

            i = slashIndex + 1;
        }

        return false;
    }
}
