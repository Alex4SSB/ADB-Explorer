using ADB_Explorer.Models;
using ADB_Explorer.Services;

namespace ADB_Explorer.Helpers;

public static class AppDataHelper
{
    /// <summary>
    /// Moves MSIX-virtualized app data into the real %LocalAppData%\AdbExplorer folder on first launch after unvirtualization.
    /// </summary>
    public static void MigrateVirtualizedAppData(string targetPath)
    {
        var sourcePath = GetVirtualizedAppDataPath();
        if (sourcePath is null || !Directory.Exists(sourcePath))
            return;

        try
        {
            MergeDirectory(sourcePath, targetPath);

            if (Directory.Exists(sourcePath))
                TryDeleteDirectory(sourcePath);
        }
        catch
        {
            // Best-effort migration; don't block startup.
        }
    }

    /// <summary>
    /// Returns the legacy MSIX-virtualized app-data folder, if the process has package identity.
    /// Pre-unvirtualization writes were redirected to Packages\{family}\LocalCache\Local under real %LocalAppData%.
    /// </summary>
    private static string? GetVirtualizedAppDataPath()
    {
        var familyName = NativeMethods.GetCurrentPackageFamilyName();
        if (familyName is null)
            return null;

        var localAppData = global::Windows.Storage.UserDataPaths.GetDefault().LocalAppData;
        return Path.Combine(localAppData, "Packages", familyName, "LocalCache", "Local", AdbExplorerConst.APP_DATA_FOLDER);
    }

    private static void MergeDirectory(string sourceDir, string targetDir)
    {
        if (!Directory.Exists(targetDir))
        {
            Directory.Move(sourceDir, targetDir);
            return;
        }

        foreach (var sourceFile in Directory.EnumerateFiles(sourceDir))
        {
            var fileName = Path.GetFileName(sourceFile);
            var targetFile = Path.Combine(targetDir, fileName);

            if (ShouldCopyFile(sourceFile, targetFile))
                File.Copy(sourceFile, targetFile, overwrite: true);
        }

        foreach (var sourceSubDir in Directory.EnumerateDirectories(sourceDir))
        {
            var dirName = Path.GetFileName(sourceSubDir);
            MergeDirectory(sourceSubDir, Path.Combine(targetDir, dirName));
        }
    }

    private static bool ShouldCopyFile(string sourceFile, string targetFile)
    {
        if (!File.Exists(targetFile))
            return true;

        if (string.Equals(Path.GetFileName(sourceFile), AdbExplorerConst.APP_SETTINGS_FILE, StringComparison.OrdinalIgnoreCase))
            return File.GetLastWriteTimeUtc(sourceFile) > File.GetLastWriteTimeUtc(targetFile);

        return false;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        { }
    }
}
