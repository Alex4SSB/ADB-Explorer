using Vanara.Windows.Shell;

namespace ADB_Explorer.Services;

public static partial class ShellFileOperation
{
    public static string GetPackageName(LogicalDeviceViewModel device, string fullPath)
    {
        AdbService.ExecuteDeviceAdbShellCommand(device.ID,
                                                "pm",
                                                out string stdout,
                                                out _,
                                                CancellationToken.None,
                                                "install",
                                                "-R",
                                                "--pkg",
                                                "''",
                                                AdbService.EscapeAdbShellString(fullPath));

        var match = AdbRegEx.RE_PACKAGE_NAME().Match(stdout);
        return match.Success ? match.Groups["package"].Value : fullPath[..fullPath.LastIndexOf('.')][(fullPath.LastIndexOf('/') + 1)..];
    }

    public static void InstallPackages(LogicalDeviceViewModel device, IEnumerable<FileClass> items, Dispatcher dispatcher)
    {
        foreach (var item in items)
        {
            if (AppBackupHelper.IsApkBackup(item.FullName))
            {
                RestorePackageBackup(device, item, dispatcher);
                continue;
            }

            var op = new PackageInstallOperation(dispatcher, device, item);
            op.WhenCompleted(() => OnInstallCompleted(op));

            Data.FileOpQ.AddOperation(op);
        }
    }

    public static void PushPackages(LogicalDeviceViewModel device, IEnumerable<ShellItem> items, Dispatcher dispatcher)
    {
        foreach (var item in items)
        {
            if (AppBackupHelper.IsApkBackup(item.ParsingName) || AppBackupHelper.IsApkBackup(item.Name))
            {
                RestorePackageBackup(device, item, dispatcher);
                continue;
            }

            var op = new PackageInstallOperation(dispatcher, device, new(new FilePath(item)), pushPackage: true);
            op.WhenCompleted(() => OnInstallCompleted(op));
            
            Data.FileOpQ.AddOperation(op);
        }
    }

    public static void BackupPackages(
        LogicalDeviceViewModel device,
        IEnumerable<Package> packages,
        string windowsFolder,
        Dispatcher dispatcher)
    {
        foreach (var package in packages)
        {
            var destName = FileHelper.DuplicateFile(
                Directory.Exists(windowsFolder) ? Directory.GetFiles(windowsFolder).Select(Path.GetFileName) : [],
                AppBackupHelper.WindowsBackupFileName(package.Name));
            var windowsDest = FileHelper.ConcatPaths(windowsFolder, destName, '\\');
            Directory.CreateDirectory(windowsFolder);
            var tempArchive = AppBackupHelper.DeviceTempArchivePath();
            var display = new FileClass(destName, windowsDest, AbstractFile.FileType.File);

            var op = new AppBackupOperation(display, tempArchive, windowsDest, package, device, dispatcher);
            Data.FileOpQ.AddOperation(op);
        }
    }

    public static void RestorePackageBackup(LogicalDeviceViewModel device, FileClass deviceFile, Dispatcher dispatcher)
    {
        var tempArchive = AppBackupHelper.DeviceTempArchivePath();
        if (!SilentCopy(device, deviceFile.FullPath, tempArchive, out var stderr))
        {
            DialogService.ShowMessage(
                stderr,
                Strings.Resources.S_MENU_INSTALL,
                DialogService.DialogIcon.Critical,
                copyToClipboard: true);
            return;
        }

        Data.FileOpQ.AddOperation(new AppRestoreOperation(deviceFile, tempArchive, device, dispatcher));
    }

    public static void RestorePackageBackup(LogicalDeviceViewModel device, ShellItem windowsItem, Dispatcher dispatcher)
    {
        var tempArchive = AppBackupHelper.DeviceTempArchivePath();
        var source = new SyncFile(windowsItem);
        var target = new SyncFile(tempArchive);
        var push = FileSyncOperation.PushFile(source, target, device, dispatcher);
        var display = new FileClass(windowsItem);

        push.WhenFinished(status =>
        {
            if (status is FileOperation.OperationStatus.Completed)
            {
                dispatcher.Invoke(() =>
                    Data.FileOpQ.AddOperation(new AppRestoreOperation(display, tempArchive, device, dispatcher)));
            }
            else
            {
                SilentDelete(device, tempArchive);
            }
        });

        Data.FileOpQ.AddOperation(push);
    }

    public static void UninstallPackages(LogicalDeviceViewModel device, IEnumerable<string> packages, Dispatcher dispatcher)
    {
        foreach (var item in packages)
        {
            var op = new PackageInstallOperation(dispatcher, device, packageName: item);
            op.WhenCompleted(() => OnInstallCompleted(op));

            Data.FileOpQ.AddOperation(op);
        }
    }

    private static void OnInstallCompleted(PackageInstallOperation op)
    {
        if (op.Device.ID == Data.ActiveDevice.ID
            && Data.FileActions.IsAppDrive)
        {
            // update UI when on current device and current path
            if (op.IsUninstall)
                Data.Packages.RemoveAll(pkg => pkg.Name == op.PackageName);
            else if (op.PushPackage)
                Data.FileActions.RefreshPackages = true;
        }
    }

    public static ulong? GetPackagesCount(LogicalDeviceViewModel device, bool includeSystem = true)
    {
        string[] args = includeSystem
            ? ["list", "packages", "|", "wc", "-l"]
            : ["list", "packages", "-3", "|", "wc", "-l"];

        var result = AdbService.ExecuteDeviceAdbShellCommand(device.ID, "pm", out string stdout, out _, CancellationToken.None, args);
        if (result != 0 || !ulong.TryParse(stdout, out ulong value))
            return null;

        return value;
    }

    private static readonly string PKG_LIST_SYSTEM = $"{AdbExplorerConst.ADB_UNIT_SEP}SYS{AdbExplorerConst.ADB_UNIT_SEP}";

    private static readonly string PKG_LIST_USER = $"{AdbExplorerConst.ADB_UNIT_SEP}USER{AdbExplorerConst.ADB_UNIT_SEP}";

    public static ObservableList<Package> GetPackages(LogicalDeviceViewModel device, bool includeSystem = true, bool optionalParams = true)
    {
        // More package-specific info can be acquired using dumpsys package [package_name]

        var optional = optionalParams ? " -U --show-versioncode" : "";
        var userCmd = $"pm list packages -3 -f{optional}";
        var script = includeSystem
            ? string.Join("; ",
                $"echo {PKG_LIST_SYSTEM}",
                $"pm list packages -s -f{optional}",
                $"echo {AdbExplorerConst.ADB_FIELD_SEP}",
                $"echo {PKG_LIST_USER}",
                userCmd,
                $"echo {AdbExplorerConst.ADB_FIELD_SEP}")
            : userCmd;

        var exitCode = AdbService.ExecuteDeviceAdbShellCommand(device.ID, script, out string stdout, out _, CancellationToken.None);
        if (exitCode != 0 || string.IsNullOrWhiteSpace(stdout))
            return [];

        ObservableList<Package> packages = [];

        if (includeSystem)
        {
            packages.AddRange(ParsePackageSection(ExtractPackageListSection(stdout, PKG_LIST_SYSTEM), Package.PackageType.System, device.SerialNumber));
            packages.AddRange(ParsePackageSection(ExtractPackageListSection(stdout, PKG_LIST_USER), Package.PackageType.User, device.SerialNumber));
        }
        else
        {
            packages.AddRange(ParsePackageSection(stdout, Package.PackageType.User, device.SerialNumber));
        }

        return packages;
    }

    private static IEnumerable<Package> ParsePackageSection(string section, Package.PackageType type, string serialNumber)
        => section.Split(AdbService.LINE_SEPARATORS, StringSplitOptions.RemoveEmptyEntries)
                  .Select(pkg => Package.New(pkg, type))
                  .OfType<Package>()
                  .Select(pkg =>
                  {
                      pkg.DeviceSerial = serialNumber;
                      return pkg;
                  });

    private static string ExtractPackageListSection(string stdout, string label)
    {
        var start = stdout.IndexOf(label, StringComparison.Ordinal);
        if (start < 0)
            return "";

        var end = stdout.IndexOf(AdbExplorerConst.ADB_FIELD_SEP, start + label.Length);
        if (end < 0)
            end = stdout.Length;

        return stdout[(start + label.Length)..end].Trim(AdbExplorerConst.ADB_FIELD_SEP, ' ', '\r', '\n');
    }
}
