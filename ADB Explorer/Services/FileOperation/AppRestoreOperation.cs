namespace ADB_Explorer.Services;

/// <summary>
/// Restores an app backup already on the device as <c>/data/local/tmp/*.tar.gz</c>:
/// extract APKs, <c>pm install</c> / install session, extract OBB, then clean up.
/// </summary>
public partial class AppRestoreOperation : AbstractShellFileOperation
{
    [GeneratedRegex(@"\[(\d+)\]", RegexOptions.Compiled)]
    private static partial Regex SessionId();

    public string TempArchivePath { get; }

    public override string Tooltip => Strings.Resources.S_MENU_INSTALL;

    public override FrameworkElement OpIcon => new InstallIcon();

    public AppRestoreOperation(
        FileClass displayFile,
        string tempArchivePath,
        LogicalDeviceViewModel device,
        Dispatcher dispatcher)
        : base(displayFile, device, dispatcher)
    {
        TempArchivePath = tempArchivePath;
        OperationName = OperationType.Install;
        AltTarget = new(Navigation.SpecialLocation.PackageDrive);
    }

    public override void Start()
    {
        BeginInProgress();

        var operationTask = Task.Run(() => Restore(CancelTokenSource!.Token), CancelTokenSource!.Token);

        TrackTask(operationTask, "Restore failed",
            onCompleted: () =>
            {
                SetCompleted();
                Dispatcher.Invoke(RefreshPackagesIfNeeded);
            },
            onAborted: () => Cleanup(CancellationToken.None));
    }

    private void Restore(CancellationToken cancellationToken)
    {
        string? stagingRoot = null;
        try
        {
            var toc = ArchiveListing.GetOrFetchToc(Device.ID, TempArchivePath, cancellationToken);
            var (apkMembers, obbPackages) = AppBackupHelper.SplitBackupMembers(toc.Entries.Select(e => e.Path));
            var session = new ArchiveOpProgressSession(
                this,
                ArchiveVerboseProgress.MemberBytesFromEntries(toc.Entries));

            if (apkMembers.Count > 0)
            {
                stagingRoot = ArchiveExtract.CreateStagingRoot(Device.ID, cancellationToken);
                var contentRoot = FileHelper.ConcatPaths(stagingRoot, "content");
                ArchiveExtract.ExtractTarMembers(
                    Device.ID,
                    TempArchivePath,
                    contentRoot,
                    apkMembers,
                    cancellationToken,
                    session.OnLine);
                session.OnCommandFinished();

                var apkPaths = apkMembers.Select(name => FileHelper.ConcatPaths(contentRoot, name)).ToList();
                if (obbPackages.Count == 0)
                    session.Finish();

                InstallDeviceApks(Device.ID, apkPaths, cancellationToken);
            }

            foreach (var package in obbPackages)
            {
                var obbDest = AppBackupHelper.ObbDirectory(package);
                try
                {
                    ArchiveExtract.ExtractTarMembers(
                        Device.ID,
                        TempArchivePath,
                        AdbExplorerConst.OBB_ROOT,
                        [package],
                        cancellationToken,
                        session.OnLine);
                }
                catch (Exception e)
                {
                    throw new IOException($"{Strings.Resources.S_APK_BACKUP_OBB_FAILED}\n{obbDest}\n{e.Message}", e);
                }
            }

            session.Finish();
        }
        finally
        {
            if (!string.IsNullOrEmpty(stagingRoot))
                ArchiveExtract.CleanupStaging(Device.ID, stagingRoot, CancellationToken.None);

            Cleanup(cancellationToken);
        }
    }

    internal static void InstallDeviceApks(string deviceId, IReadOnlyList<string> apkPaths, CancellationToken cancellationToken)
    {
        if (apkPaths.Count == 0)
            throw new ArgumentException("At least one APK is required.", nameof(apkPaths));

        if (apkPaths.Count == 1)
        {
            var single = AdbService.ExecuteVoidShellCommand(
                deviceId,
                cancellationToken,
                "pm",
                "install",
                "-r",
                "-d",
                AdbService.EscapeAdbShellString(apkPaths[0])).GetAwaiter().GetResult();

            if (!string.IsNullOrEmpty(single))
                throw new IOException(single);

            return;
        }

        var createExit = AdbService.ExecuteDeviceAdbShellCommand(
            deviceId,
            "pm",
            out var createStdout,
            out var createStderr,
            cancellationToken,
            "install-create",
            "-r",
            "-d");

        AdbService.ThrowIfFailed(createExit, createStdout, createStderr);

        var sessionMatch = SessionId().Match(createStdout);
        if (!sessionMatch.Success)
            sessionMatch = SessionId().Match(createStderr);

        if (!sessionMatch.Success)
            throw new IOException(string.IsNullOrWhiteSpace(createStdout) ? createStderr : createStdout);

        var session = sessionMatch.Groups[1].Value;

        try
        {
            foreach (var apk in apkPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var splitName = FileHelper.GetFullName(apk);
                var ext = FileHelper.GetExtension(splitName);
                if (!string.IsNullOrEmpty(ext))
                    splitName = splitName[..^ext.Length];

                var pathEsc = AdbService.EscapeAdbShellString(apk);
                var nameEsc = AdbService.EscapeAdbShellString(splitName);
                var script = $"pm install-write -S $(stat -c%s {pathEsc}) {session} {nameEsc} < {pathEsc}";

                var writeExit = AdbService.ExecuteDeviceAdbShellCommand(
                    deviceId,
                    "sh",
                    out var writeStdout,
                    out var writeStderr,
                    cancellationToken,
                    "-c",
                    AdbService.EscapeAdbShellString(script));

                AdbService.ThrowIfFailed(writeExit, writeStdout, writeStderr);
            }

            var commit = AdbService.ExecuteVoidShellCommand(
                deviceId,
                cancellationToken,
                "pm",
                "install-commit",
                session).GetAwaiter().GetResult();

            if (!string.IsNullOrEmpty(commit))
                throw new IOException(commit);
        }
        catch
        {
            _ = AdbService.ExecuteDeviceAdbShellCommand(
                deviceId,
                "pm",
                out _,
                out _,
                CancellationToken.None,
                "install-abandon",
                session);
            throw;
        }
    }

    private void Cleanup(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        ShellFileOperation.SilentDelete(Device, TempArchivePath);
        ArchiveListing.InvalidateToc(TempArchivePath);
    }

    private void RefreshPackagesIfNeeded()
    {
        if (Device.ID == Data.ActiveDevice?.ID && Data.FileActions.IsAppDrive)
            FileActionLogic.UpdatePackages(true);
    }
}
