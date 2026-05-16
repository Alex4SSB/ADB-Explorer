using ADB_Explorer.Models;
using ADB_Explorer.Services;
using ADB_Explorer.ViewModels;
using AdvancedSharpAdbClient;

namespace ADB_Explorer.Helpers;

internal static class AdbHelper
{
    public static Task<bool> CheckAdbVersion() => Task.Run(() =>
    {
        string adbPath = string.IsNullOrEmpty(Data.Settings.ManualAdbPath)
            ? AdbExplorerConst.ADB_PROCESS
            : Data.Settings.ManualAdbPath;

        ADBService.VerifyAdbVersion(adbPath);

        return Data.RuntimeSettings.AdbVersion >= AdbExplorerConst.MIN_ADB_VERSION;
    });

    public static void EnableMdns() => App.SafeInvoke(async () =>
    {
        ADBService.IsMdnsEnabled = Data.Settings.EnableMdns;
        if (Data.Settings.EnableMdns)
        {
            Data.MdnsService.Enable();
        }
        else if (Data.MdnsService.State is MDNS.MdnsState.Running)
        {
            var result = await DialogService.ShowConfirmation(Strings.Resources.S_DISABLE_MDNS,
                                                              Strings.Resources.S_DISABLE_MDNS_TITLE,
                                                              Strings.Resources.S_RESTART_ADB_NOW,
                                                              cancelText: Strings.Resources.S_RESTART_LATER,
                                                              icon: DialogService.DialogIcon.Informational);

            if (result.Item1 is Wpf.Ui.Controls.ContentDialogResult.Primary)
                ADBService.KillAdbServer();

            Data.MdnsService.Disable();
        }
    });

    public static Task<bool> WriteTextFileAsync(LogicalDeviceViewModel device, string filePath, string content, CancellationToken cancellationToken = default) =>
        Task.Run(async () =>
    {
        try
        {
            await WriteFileAsync(device, filePath, content, cancellationToken);
            return true;
        }
        catch (Exception e)
        {
            App.SafeInvoke(() =>
                DialogService.ShowMessage(e.Message, Strings.Resources.S_WRITE_FILE_ERROR_TITLE, DialogService.DialogIcon.Exclamation, copyToClipboard: true));

            return false;
        }
    });

    public static Task<string?> ReadTextFileAsync(LogicalDeviceViewModel device, string filePath, CancellationToken cancellationToken = default) =>
        Task.Run(async () =>
    {
        try
        {
            var stream = await ReadFileAsStreamAsync(device, filePath, cancellationToken);
            if (stream is null) return null;

            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync(cancellationToken);
        }
        catch (Exception e)
        {
            if (e is OperationCanceledException) return null;

            App.SafeInvoke(() =>
                DialogService.ShowMessage(e.Message, Strings.Resources.S_READ_FILE_ERROR_TITLE, DialogService.DialogIcon.Exclamation, copyToClipboard: true));

            return "";
        }
    });

    public static async Task<MemoryStream?> ReadFileAsStreamAsync(LogicalDeviceViewModel device, string path, CancellationToken cancellationToken = default)
    {
        MemoryStream stream = new();
        using (SyncService service = new(device.Device.DeviceData))
        {
            try
            {
                await service.PullAsync(path, stream, cancellationToken: cancellationToken);
                SyncTransferTracker.AddPullBytes(stream.Position);
            }
            catch
            {
                return null;
            }
        }

        stream.Position = 0;
        return stream;
    }

    public static MemoryStream? ReadFileAsStream(LogicalDeviceViewModel device, string path)
    {
        MemoryStream stream = new();
        using (SyncService service = new(device.Device.DeviceData))
        {
            try
            {
                service.Pull(path, stream);
                SyncTransferTracker.AddPullBytes(stream.Position);
            }
            catch
            {
                return null;
            }
        }

        stream.Position = 0;
        return stream;
    }

    public static async Task WriteFileAsync(LogicalDeviceViewModel device, string path, string content, CancellationToken cancellationToken = default)
    {
        using MemoryStream stream = new();
        using StreamWriter writer = new(stream);
        await writer.WriteAsync(content);
        await writer.FlushAsync(cancellationToken);
        stream.Position = 0;

        using SyncService service = new(device.Device.DeviceData);
        await service.PushAsync(stream, path, (UnixFileMode)0x1ED, DateTime.Now, cancellationToken: cancellationToken); // 0x1ED = 0777 in octal
        SyncTransferTracker.AddPushBytes(stream.Length);
    }

    public static async Task FetchDumpsysInfoAsync(LogicalDeviceViewModel device, Package package, CancellationToken cancellationToken = default)
    {
        string stdout = "", stderr = "";
        await Task.Run(() => ADBService.ExecuteDeviceAdbShellCommand(device.ID, "dumpsys", out stdout, out stderr, cancellationToken, "package", package.Name), cancellationToken);

        if (cancellationToken.IsCancellationRequested || string.IsNullOrEmpty(stdout))
            return;

        var versionNameMatch = AdbRegEx.RE_DUMPSYS_VERSION_NAME().Match(stdout);
        var lastUpdateMatch = AdbRegEx.RE_DUMPSYS_LAST_UPDATE().Match(stdout);

        App.SafeInvoke(() =>
        {
            if (versionNameMatch.Success)
                package.VersionName = versionNameMatch.Groups["VersionName"].Value;

            if (lastUpdateMatch.Success && DateTime.TryParseExact(lastUpdateMatch.Groups["LastUpdateTime"].Value, "yyyy-MM-dd HH:mm:ss", null, System.Globalization.DateTimeStyles.None, out var updated))
                package.LastUpdateTime = updated;
        });
    }

    public static void ApplyMountInfo(LogicalDeviceViewModel device, CancellationToken cancellationToken)
    {
        var infos = GetMountInfo(device, cancellationToken);
        var driveList = device.Drives.OfType<LogicalDriveViewModel>();
        foreach (var drive in driveList)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            if (drive.Type is AbstractDrive.DriveType.Root)
            {
                drive.FSInfo = infos.FirstOrDefault(i => i.MountPoint == "/");
            }
            else
            {
                var path = drive.Path;
                Models.FileSystemInfo? info = null;

                do
                {
                    if (infos.FirstOrDefault(i => i.MountPoint == path) is Models.FileSystemInfo inf && inf.MountPoint is not null)
                    {
                        info = inf;
                        break;
                    }
                    path = FileHelper.GetParentPath(path);

                } while (path != "/");

                App.SafeInvoke(() => drive.FSInfo = info);
            }
        }
    }

    private static IEnumerable<Models.FileSystemInfo> GetMountInfo(LogicalDeviceViewModel device, CancellationToken cancellationToken)
    {
        ADBService.ExecuteDeviceAdbShellCommand(device.ID, "mount", out string stdout, out _, cancellationToken);

        var matches = AdbRegEx.RE_MOUNT_PARSE().Matches(stdout);

        foreach (Match match in matches)
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            yield return new(
                BlockDev: match.Groups["BlockDev"].Value,
                MountPoint: match.Groups["MntPt"].Value,
                FileSystemType: match.Groups["Type"].Value,
                Options: match.Groups["Attr"].Value.Split(',')
            );
        }
    }
}
