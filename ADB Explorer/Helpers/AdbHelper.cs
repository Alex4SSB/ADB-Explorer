using ADB_Explorer.Models;
using ADB_Explorer.Services;
using ADB_Explorer.Services.AppInfra;
using ADB_Explorer.ViewModels;
using ADB_Explorer.ViewModels.Pages;
using AdvancedSharpAdbClient;

namespace ADB_Explorer.Helpers;

public partial class AdbState : ObservableObject
{
    [ObservableProperty]
    public partial string Path { get; set; }

    [ObservableProperty]
    public partial AdbHelper.AdbStatus Status { get; set; } = AdbHelper.AdbStatus.NotFound;

    [ObservableProperty]
    public partial Version Version { get; set; }

    public string VersionString => $"v{Version}";

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(Version))
        {
            OnPropertyChanged(nameof(VersionString));
        }
    }
}

public static class AdbHelper
{
    public enum AdbStatus
    {
        NotFound,       // we don't know where to look
        PathInvalid,    // we know where to look but we'd rather not
        Compromised,    // we found it but we'd rather not execute it
        VersionUnknown, // it is safe but it doesn't report its version
        Outdated,       // it is definitely ADB but it is too old
        Valid           // all good
    }

    public static AdbState CurrentAdbState { get; } = new();

    public static Task<bool> CheckAdbVersion() => Task.Run(() =>
    {
        string adbPath = string.IsNullOrEmpty(Data.Settings.ManualAdbPath)
            ? AdbExplorerConst.ADB_PROCESS
            : Data.Settings.ManualAdbPath;

        ADBService.VerifyAdbVersion(adbPath);

        return CurrentAdbState.Status is AdbStatus.Valid;
    });

    public static void EnterAdbSetupMode()
    {
        string adbPath = string.IsNullOrEmpty(Data.Settings.ManualAdbPath)
            ? AdbExplorerConst.ADB_PROCESS
            : Data.Settings.ManualAdbPath;

        ADBService.VerifyAdbVersion(adbPath);

        App.SafeInvoke(() =>
        {
            App.Services.GetService<SettingsViewModel>()?.EnsureWorkingDirectoriesVisible();
            Data.CurrentPage.Value = typeof(Views.Pages.SettingsPage);
        });
    }

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

    public static Task<bool> WriteTextFileAsync(LogicalDeviceViewModel device, FileClass file, string content, CancellationToken cancellationToken = default) =>
        Task.Run(async () =>
    {
        try
        {
            await WriteFileAsync(device, file, content, cancellationToken);
            return true;
        }
        catch (Exception e)
        {
            App.SafeInvoke(() =>
                DialogService.ShowMessage(e.Message,
                                          Strings.Resources.S_WRITE_FILE_ERROR_TITLE,
                                          DialogService.DialogIcon.Exclamation,
                                          copyToClipboard: true,
                                          error: DialogError.WriteFileFailed));

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
            if (e is OperationCanceledException || cancellationToken.IsCancellationRequested)
                return null;

            App.SafeInvoke(() =>
                DialogService.ShowMessage(e.Message,
                                          Strings.Resources.S_READ_FILE_ERROR_TITLE,
                                          DialogService.DialogIcon.Exclamation,
                                          copyToClipboard: true,
                                          error: DialogError.ReadFileFailed));

            return "";
        }
    });

    public static async Task<MemoryStream?> ReadFileAsStreamAsync(LogicalDeviceViewModel device, string path, CancellationToken cancellationToken = default)
    {
        try
        {
            if (ArchivePath.TryParse(path, out var archivePath, out var internalPath, device.ID)
                && !string.IsNullOrEmpty(internalPath))
            {
                return await ReadArchiveMemberAsStreamAsync(device, archivePath, internalPath, cancellationToken);
            }

            return await PullPathAsStreamAsync(device, path, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private static async Task<MemoryStream?> ReadArchiveMemberAsStreamAsync(
        LogicalDeviceViewModel device,
        string archivePath,
        string internalPath,
        CancellationToken cancellationToken)
    {
        string? stagingRoot = null;
        try
        {
            var (root, extractedPath, _) = ArchiveExtract.ExtractSelectionForPull(
                device.ID,
                archivePath,
                internalPath,
                isDirectory: false,
                cancellationToken);
            stagingRoot = root;
            return await PullPathAsStreamAsync(device, extractedPath, cancellationToken);
        }
        finally
        {
            if (stagingRoot is not null)
                ArchiveExtract.CleanupStaging(device.ID, stagingRoot, cancellationToken);
        }
    }

    private static async Task<MemoryStream?> PullPathAsStreamAsync(
        LogicalDeviceViewModel device,
        string path,
        CancellationToken cancellationToken)
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
        if (ArchivePath.TryParse(path, out var archivePath, out var internalPath, device.ID)
            && !string.IsNullOrEmpty(internalPath))
        {
            return ReadArchiveMemberAsStreamAsync(device, archivePath, internalPath, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }

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

    public static async Task WriteFileAsync(LogicalDeviceViewModel device, FileClass file, string content, CancellationToken cancellationToken = default)
    {
        using MemoryStream stream = new();
        using StreamWriter writer = new(stream);
        await writer.WriteAsync(content);
        await writer.FlushAsync(cancellationToken);
        stream.Position = 0;

        if (ArchivePath.TryParse(file.FullPath, out var archivePath, out var internalPath, device.ID)
            && !string.IsNullOrEmpty(internalPath))
        {
            await WriteArchiveMemberAsync(device, archivePath, internalPath, stream, file, cancellationToken);
            return;
        }

        using SyncService service = new(device.Device.DeviceData);
        await service.PushAsync(stream, file.FullPath, file.Permissions ?? (UnixFileMode)0x1ED, DateTime.Now, cancellationToken: cancellationToken); // 0x1ED = 0777 in octal
        SyncTransferTracker.AddPushBytes(stream.Length);
    }

    private static async Task WriteArchiveMemberAsync(
        LogicalDeviceViewModel device,
        string archivePath,
        string internalPath,
        MemoryStream content,
        FileClass file,
        CancellationToken cancellationToken)
    {
        if (ArchiveHelper.IsMemberPreviewReadOnly(file.FullPath, device.ID))
            throw new InvalidOperationException(Strings.Resources.S_ARCHIVE_READ_ONLY);

        var stagingRoot = ArchiveExtract.CreateStagingRoot(device.ID, cancellationToken);
        try
        {
            var contentRoot = FileHelper.ConcatPaths(stagingRoot, "content");
            var memberPath = FileHelper.ConcatPaths(contentRoot, internalPath);
            var memberParent = FileHelper.GetParentPath(memberPath);
            await ShellFileOperation.MakeDirs(device.ID, [memberParent]);

            content.Position = 0;
            using (SyncService service = new(device.Device.DeviceData))
            {
                await service.PushAsync(
                    content,
                    memberPath,
                    file.Permissions ?? (UnixFileMode)0x1ED,
                    DateTime.Now,
                    cancellationToken: cancellationToken);
                SyncTransferTracker.AddPushBytes(content.Length);
            }

            ArchiveExtract.UpdateZipMember(device.ID, archivePath, internalPath, contentRoot, cancellationToken);
            ArchiveListing.InvalidateToc(archivePath);
        }
        finally
        {
            ArchiveExtract.CleanupStaging(device.ID, stagingRoot, cancellationToken);
        }
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
        var infos = GetMountInfo(device, cancellationToken).ToList();

        // a cancelled fetch is truncated; don't overwrite a good table with it
        if (cancellationToken.IsCancellationRequested)
            return;

        if (infos.Count > 0)
            device.MountPoints = infos;

        foreach (var drive in device.Drives.OfType<LogicalDriveViewModel>())
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            if (drive.Type is AbstractDrive.DriveType.Root)
            {
                var rootInfo = infos.FirstOrDefault(i => i.MountPoint == "/");
                App.SafeInvoke(() => drive.FSInfo = rootInfo);
            }
            else
            {
                var path = string.IsNullOrEmpty(drive.LinkTargetPath)
                    ? drive.Path
                    : drive.LinkTargetPath;

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

        foreach (var drive in device.Drives.OfType<VirtualDriveViewModel>().Where(d => d.Type is AbstractDrive.DriveType.Temp))
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            Models.FileSystemInfo? info = null;
            var mountPoint = drive.DfMountPoint;

            if (!string.IsNullOrEmpty(mountPoint))
                info = infos.FirstOrDefault(i => i.MountPoint == mountPoint);

            App.SafeInvoke(() => drive.FSInfo = info);
        }

        App.SafeInvoke(() =>
        {
            // recompute access; the first pass may have run before the mount table arrived
            Data.DirList?.RefreshLocationAccess();
            FileActionLogic.UpdateFileActions();
        });
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
