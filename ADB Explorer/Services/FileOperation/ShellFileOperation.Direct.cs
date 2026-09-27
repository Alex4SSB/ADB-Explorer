using AdvancedSharpAdbClient;
using AdvancedSharpAdbClient.Models;
using Vanara.Windows.Shell;

namespace ADB_Explorer.Services;

public static partial class ShellFileOperation
{
    public static void SilentDelete(LogicalDeviceViewModel device, IEnumerable<FilePath> items)
        => SilentDelete(device, items.Select(item => item.FullPath).ToArray());

    public static void SilentDelete(LogicalDeviceViewModel device, params string[] items)
    {
        string[] args = ["-rf", .. items.Select(item => AdbService.EscapeAdbShellString(item))];
        AdbService.ExecuteDeviceAdbShellCommand(device.ID, "rm", out _, out _, CancellationToken.None, args);
    }

    public static bool SilentCopy(LogicalDeviceViewModel device, string fullPath, string targetPath, out string stderr, bool throwOnError = false)
    {
        var exitCode = AdbService.ExecuteDeviceAdbShellCommand(device.ID,
                                                               "cp",
                                                               out _,
                                                               out stderr,
                                                               CancellationToken.None,
                                                               "-p",
                                                               AdbService.EscapeAdbShellString(fullPath),
                                                               AdbService.EscapeAdbShellString(targetPath));

        if (exitCode != 0 && throwOnError)
            throw new Exception(stderr);

        return exitCode == 0;
    }

    public static bool SilentCopy(LogicalDeviceViewModel device, string fullPath, string targetPath, bool throwOnError = false)
        => SilentCopy(device, fullPath, targetPath, out _, throwOnError);

    public static bool SilentMove(LogicalDeviceViewModel device, FilePath item, string targetPath) => SilentMove(device, item.FullPath, targetPath);

    public static bool SilentMove(LogicalDeviceViewModel device, string fullPath, string targetPath, bool throwOnError = true)
    {
        var exitCode = AdbService.ExecuteDeviceAdbShellCommand(device.ID,
                                                               "mv",
                                                               out _,
                                                               out var stderr,
                                                               CancellationToken.None,
                                                               AdbService.EscapeAdbShellString(fullPath),
                                                               AdbService.EscapeAdbShellString(targetPath));

        if (exitCode != 0 && throwOnError)
        {
            throw new Exception(stderr);
        }

        return exitCode == 0;
    }

    /// <summary>
    /// Pushes a Windows file or folder tree to <paramref name="androidDestPath"/> via AdvancedSharpAdbClient sync
    /// (no classic <c>adb push</c>).
    /// </summary>
    public static void SilentPush(
        LogicalDeviceViewModel device,
        ShellItem windowsItem,
        string androidDestPath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(windowsItem.ParsingName) && !Directory.Exists(windowsItem.ParsingName))
            throw new FileNotFoundException(Strings.Resources.S_SYNC_FILE_NOT_FOUND, windowsItem.ParsingName);
        
        var source = new SyncFile(windowsItem, includeContent: true);
        try
        {
            IEnumerable<SyncFile> files = [source, .. source.AllChildren()];

            if (source.IsDirectory)
            {
                var dirPaths = FolderHelper.GetBottomMostFolders(files)
                    .Select(f => FileHelper.ConcatPaths(
                        androidDestPath,
                        FileHelper.ExtractRelativePath(f.FullPath, source.FullPath, false)));

                MakeDirs(device.ID, dirPaths).GetAwaiter().GetResult();
            }

            UnixFileStatus fileMode = UnixFileStatus.AllPermissions | UnixFileStatus.Regular;
            var useSyncV2 = device.SupportsSyncV2;
            var isCanceled = false;
            using var cancelReg = cancellationToken.Register(() => isCanceled = true);

            foreach (var item in files.Where(f => !f.IsDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var targetPath = source.IsDirectory
                    ? FileHelper.ConcatPaths(androidDestPath, FileHelper.ExtractRelativePath(item.FullPath, source.FullPath))
                    : androidDestPath;

                using SyncService service = new(device.DeviceData);
                using var stream = new FileStream(item.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read);

                var lastWriteTime = item.DateModified ?? DateTime.Now;
                service.Push(stream, targetPath, fileMode, lastWriteTime, _ => { }, useSyncV2, in isCanceled);

                SyncTransferTracker.AddPushBytes(stream.Length);
            }
        }
        finally
        {
            source.ClearAll();
        }
    }

    public static async Task MakeDir(LogicalDeviceViewModel device, string fullPath)
        => await MakeDirs(device.ID, [fullPath]);

    public static async Task TryMakeDir(LogicalDeviceViewModel device, string fullPath)
    {
        try
        {
            await MakeDir(device, fullPath);
        }
        catch
        {
        }
    }

    public static async Task MakeDirs(LogicalDeviceViewModel device, IEnumerable<string> paths)
        => await MakeDirs(device.ID, paths);

    public static async Task MakeDirs(string deviceId, IEnumerable<string> paths)
    {
        var result = await AdbService.ExecuteVoidShellCommand(deviceId,
                                                              CancellationToken.None,
                                                              "mkdir",
                                                              ["-p", .. paths.Select(path => AdbService.EscapeAdbShellString(path))]);

        if (!string.IsNullOrEmpty(result))
            throw new Exception(result);
    }

    public static async Task MakeFile(LogicalDeviceViewModel device, string fullPath)
    {
        var result = await AdbService.ExecuteVoidShellCommand(device.ID,
                                                              CancellationToken.None,
                                                              "touch",
                                                              AdbService.EscapeAdbShellString(fullPath));

        if (!string.IsNullOrEmpty(result))
            throw new Exception(result);
    }

    public static async void WriteLine(LogicalDeviceViewModel device, string fullPath, string newLine)
    {
        var result = await AdbService.ExecuteVoidShellCommand(device.ID,
                                                              CancellationToken.None,
                                                              "echo",
                                                              [newLine, ">>", AdbService.EscapeAdbShellString(fullPath)]);

        if (!string.IsNullOrEmpty(result))
        {
            throw new Exception(result);
        }
    }

    public static string ReadAllText(LogicalDeviceViewModel device, params string[] paths)
    {
        if (paths.Length == 0)
            return string.Empty;

        var exitCode = AdbService.ExecuteDeviceAdbShellCommand(device.ID,
                                                               "cat",
                                                               out string stdout,
                                                               out string stderr,
                                                               CancellationToken.None, [.. paths.Select(path => AdbService.EscapeAdbShellString(path))]);

        if (exitCode != 0)
            throw new Exception(stderr);

        return stdout;
    }
}
