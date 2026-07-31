using ADB_Explorer.Controls;
using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.ViewModels;

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

    private string tempInstallPath;

    public PackageInstallOperation(Dispatcher dispatcher,
                                   LogicalDeviceViewModel device,
                                   FileClass path = null,
                                   string packageName = null,
                                   bool pushPackage = false) : base(path, device, dispatcher)
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
        if (Status == OperationStatus.InProgress)
        {
            throw new Exception("Cannot start an already active operation!");
        }

        Status = OperationStatus.InProgress;
        StatusInfo = new InProgShellProgressViewModel();

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
                tempInstallPath = FileHelper.ConcatPaths(AdbExplorerConst.TEMP_PATH, $"{Guid.NewGuid():N}_{FilePath.FullName}");
                if (!ShellFileOperation.SilentCopy(Device, installPath, tempInstallPath, out var copyStderr))
                {
                    tempInstallPath = null;
                    Status = OperationStatus.Failed;
                    StatusInfo = new FailedOpProgressViewModel(copyStderr);
                    return;
                }

                installPath = tempInstallPath;
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
            ? ADBService.EscapeAdbString(args[index])
            : ADBService.EscapeAdbShellString(args[index]);

        var operationTask = PushPackage
                ? ADBService.ExecuteDeviceAdbCommand(Device.ID, CancelTokenSource.Token, "install", args)
                : ADBService.ExecuteVoidShellCommand(Device.ID, CancelTokenSource.Token, "pm", args);

        operationTask.ContinueWith((t) =>
        {
            CleanupTempInstallPath();

            if (t.Result == "")
            {
                Status = OperationStatus.Completed;
                StatusInfo = new CompletedShellProgressViewModel();
            }
            else
            {
                Status = OperationStatus.Failed;
                StatusInfo = new FailedOpProgressViewModel(t.Result);
            }
        }, TaskContinuationOptions.OnlyOnRanToCompletion);

        operationTask.ContinueWith((t) =>
        {
            CleanupTempInstallPath();
            Status = OperationStatus.Canceled;
            StatusInfo = new CanceledOpProgressViewModel();
        }, TaskContinuationOptions.OnlyOnCanceled);

        operationTask.ContinueWith((t) =>
        {
            CleanupTempInstallPath();
            Status = OperationStatus.Failed;
            StatusInfo = new FailedOpProgressViewModel(t.Exception.InnerException.Message);
        }, TaskContinuationOptions.OnlyOnFaulted);
    }

    private void CleanupTempInstallPath()
    {
        if (string.IsNullOrEmpty(tempInstallPath))
            return;

        ShellFileOperation.SilentDelete(Device, tempInstallPath);
        tempInstallPath = null;
    }
}
