namespace ADB_Explorer.Services;

public class PackageInstallOperation : AbstractShellFileOperation
{
    public bool PushPackage;

    public string PackageName { get; }

    public bool IsUninstall => !string.IsNullOrEmpty(PackageName);

    public override string Tooltip => IsUninstall
        ? Strings.Resources.S_UNINSTALL
        : Strings.Resources.S_MENU_INSTALL;

    public override FrameworkElement OpIcon => IsUninstall ? new UninstallIcon() : new InstallIcon();

    private string? _tempInstallPath;

    public PackageInstallOperation(Dispatcher dispatcher,
                                   LogicalDeviceViewModel device,
                                   FileClass? path = null,
                                   string packageName = "",
                                   bool pushPackage = false)
        // Uninstall never has a real path — base() unconditionally builds a SyncFile from
        // it, so a null path (the old default) crashed. A name-only placeholder is unused
        // by Start()'s uninstall branch, which only reads PackageName.
        : base(path ?? new FileClass(packageName, packageName, AbstractFile.FileType.File), device, dispatcher)
    {
        OperationName = OperationType.Install;
        PackageName = packageName;
        PushPackage = pushPackage;

        if (IsUninstall)
        {
            AltSource = new(Navigation.SpecialLocation.PackageDrive);
            AltTarget = new(Navigation.SpecialLocation.devNull);
        }
        else
            AltTarget = new(Navigation.SpecialLocation.PackageDrive);
    }

    public override void Start()
    {
        BeginInProgress();

        var args = new string[1];
        int index = 0;

        if (IsUninstall)
        {
            args = new string[2];
            args[0] = "uninstall";
            args[1] = PackageName;
            index = 1;
        }
        // install (pm / adb)
        else
        {
            var installPath = FilePath.FullPath;

            if (!PushPackage && DriveHelper.RequiresTempForApkInstall(installPath))
            {
                _tempInstallPath = FileHelper.ConcatPaths(AdbExplorerConst.TEMP_PATH, $"{Guid.NewGuid():N}_{FilePath.FullName}");
                if (!ShellFileOperation.SilentCopy(Device, installPath, _tempInstallPath, out var copyStderr))
                {
                    _tempInstallPath = null;
                    SetFailed(copyStderr);
                    return;
                }

                installPath = _tempInstallPath!;
            }

            if (!PushPackage)
            {
                args = new string[4];
                args[0] = "install";
                args[1] = "-r";
                args[2] = "-d";
                index = 3;
            }

            args[index] = installPath;
        }

        args[index] = PushPackage
            ? AdbService.EscapeAdbString(args[index])
            : AdbService.EscapeAdbShellString(args[index]);

        var operationTask = PushPackage
                ? AdbService.ExecuteDeviceAdbCommand(Device.ID, CancelTokenSource!.Token, "install", args)
                : AdbService.ExecuteVoidShellCommand(Device.ID, CancelTokenSource!.Token, "pm", args);

        TrackTask(operationTask, "Install failed", result =>
        {
            CleanupTempInstallPath();
            SetShellResult(result);
        }, onAborted: CleanupTempInstallPath);
    }

    private void CleanupTempInstallPath()
    {
        if (string.IsNullOrEmpty(_tempInstallPath))
            return;

        ShellFileOperation.SilentDelete(Device, _tempInstallPath);
        _tempInstallPath = null;
    }
}
