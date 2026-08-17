using ADB_Explorer.Models;
using ADB_Explorer.Services;
using static ADB_Explorer.Models.AbstractFile;
using static ADB_Explorer.Models.AdbExplorerConst;

namespace ADB_Explorer.Helpers;

public readonly record struct AppBackupSources(
    string ApkParent,
    IReadOnlyList<string> ApkFileNames,
    string? ObbPackageName,
    IReadOnlyDictionary<string, long> MemberBytes);

public static class AppBackupHelper
{
    public static bool IsApkBackup(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        return FileHelper.GetExtension(path).Equals(APK_BACKUP_EXTENSION, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsInstallApkName(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return false;

        return Array.IndexOf(INSTALL_APK, FileHelper.GetExtension(fileName).ToUpperInvariant()) > -1;
    }

    public static IReadOnlyList<string> FilterInstallApkNames(IEnumerable<string> names)
        => [.. names.Where(IsInstallApkName)];

    public static string ObbDirectory(string packageName)
        => FileHelper.ConcatPaths(OBB_ROOT, packageName);

    public static string WindowsBackupFileName(string packageName)
        => $"{packageName}{APK_BACKUP_EXTENSION.ToLowerInvariant()}";

    public static string DeviceTempArchivePath()
        => FileHelper.ConcatPaths(TEMP_PATH, $"{Guid.NewGuid():N}.tar.gz");

    /// <summary>
    /// APK members are top-level <c>*.apk</c>/<c>*.apex</c>. Any other top-level folder is OBB
    /// (stored as <c>-C /sdcard/Android/obb &lt;package&gt;</c>).
    /// </summary>
    public static (IReadOnlyList<string> ApkMembers, IReadOnlyList<string> ObbPackages) SplitBackupMembers(
        IEnumerable<string> archivePaths)
    {
        List<string> apks = [];
        HashSet<string> obb = new(StringComparer.Ordinal);

        foreach (var raw in archivePaths)
        {
            var path = ArchivePath.NormalizeInternal(raw);
            if (string.IsNullOrEmpty(path) || path == ".")
                continue;

            var slash = path.IndexOf('/');
            var top = slash < 0 ? path : path[..slash];
            if (IsInstallApkName(top))
                apks.Add(top);
            else
                obb.Add(top);
        }

        return ([.. apks.Distinct(StringComparer.Ordinal)], [.. obb]);
    }

    public static string BuildCreateArchiveScript(
        string tarCommand,
        string archivePath,
        string apkParent,
        IReadOnlyList<string> apkFileNames,
        string? obbPackageName,
        bool verbose = false)
    {
        apkFileNames ??= [];

        var parts = new List<string>
        {
            tarCommand,
            "-czf",
            ADBService.EscapeAdbShellString(archivePath),
        };

        if (verbose)
            parts.Add("-v");

        if (apkFileNames.Count > 0)
        {
            parts.Add("-C");
            parts.Add(ADBService.EscapeAdbShellString(apkParent));
            parts.AddRange(apkFileNames.Select(name => ADBService.EscapeAdbShellString(name)));
        }

        if (!string.IsNullOrEmpty(obbPackageName))
        {
            parts.Add("-C");
            parts.Add(ADBService.EscapeAdbShellString(OBB_ROOT));
            parts.Add(ADBService.EscapeAdbShellString(obbPackageName));
        }

        if (apkFileNames.Count == 0 && string.IsNullOrEmpty(obbPackageName))
            parts.AddRange(["-T", "/dev/null"]);

        return string.Join(' ', parts);
    }

    public static string NormalizeTarVerboseMember(string? line)
        => ArchiveVerboseProgress.NormalizeMember(line);

    public static AppBackupSources CollectSources(string deviceId, Package package, CancellationToken cancellationToken)
    {
        var parent = FileHelper.GetParentPath(package.Path);
        List<string> apkNames = [];
        Dictionary<string, long> memberBytes = new(StringComparer.Ordinal);

        try
        {
            foreach (var entry in ADBService.ListDirectoryEntries(deviceId, parent, cancellationToken))
            {
                if (entry.Type is not FileType.File || !IsInstallApkName(entry.FullName))
                    continue;

                apkNames.Add(entry.FullName);
                memberBytes[entry.FullName] = entry.Size ?? 0;
            }
        }
        catch
        {
            // Listing /data/app can fail on some devices; fall back to the listed APK path.
        }

        if (apkNames.Count == 0)
        {
            var listed = FileHelper.GetFullName(package.Path);
            if (IsInstallApkName(listed))
            {
                apkNames = [listed];
                memberBytes[listed] = ADBService.TryGetFileSize(deviceId, package.Path, cancellationToken) ?? 0;
            }
        }

        var obbDir = ObbDirectory(package.Name);
        var obbExists = ADBService.PathsExist(deviceId, obbDir).Length > 0;
        if (obbExists)
        {
            memberBytes.TryAdd(package.Name, 0);
            try
            {
                foreach (var entry in ADBService.ListDirectoryRecursive(deviceId, obbDir, cancellationToken))
                {
                    var relative = FileHelper.ExtractRelativePath(entry.FullPath, OBB_ROOT, includeSelf: false);
                    var key = NormalizeTarVerboseMember(relative);
                    if (string.IsNullOrEmpty(key))
                        continue;

                    long size = 0;
                    if (entry.Type is FileType.File)
                        size = entry.Size ?? 0;

                    memberBytes[key] = size;
                }
            }
            catch
            {
                // OBB listing is best-effort; tar -v still reports members without sizes.
            }
        }

        string? obbName = null;
        if (obbExists)
            obbName = package.Name;

        return new(parent, apkNames, obbName, memberBytes);
    }
}
