using ADB_Explorer.Helpers;
using ADB_Explorer.Models;

namespace ADB_Explorer.Services;

public static class ShellFileOperation
{
    public static void SilentDelete(ADBService.AdbDevice device, IEnumerable<FilePath> items)
        => SilentDelete(device, items.Select(item => item.FullPath).ToArray());

    public static void SilentDelete(ADBService.AdbDevice device, params string[] items)
    {
        var args = new[] { "-rf" }.Concat(items.Select(item => ADBService.EscapeAdbShellString(item))).ToArray();
        ADBService.ExecuteDeviceAdbShellCommand(device.ID, "rm", out _, out _, args);
    }

    public static void DeleteItems(ADBService.AdbDevice device, IEnumerable<FilePath> items, ObservableList<FileClass> fileList, Dispatcher dispatcher)
    {
        foreach (var item in items)
        {
            Data.FileOpQ.AddOperation(new FileDeleteOperation(dispatcher, device, item, fileList));
        }
    }

    public static bool MoveItem(ADBService.AdbDevice device, FilePath item, string targetPath) => MoveItem(device, item.FullPath, targetPath);

    public static bool MoveItem(ADBService.AdbDevice device, string fullPath, string targetPath, bool throwOnError = true)
    {
        var exitCode = ADBService.ExecuteDeviceAdbShellCommand(device.ID, "mv", out string stdout, out string stderr, new[] { ADBService.EscapeAdbShellString(fullPath), ADBService.EscapeAdbShellString(targetPath) });

        if (exitCode != 0 && throwOnError)
        {
            throw new Exception(stderr);
        }

        return exitCode == 0;
    }

    public static void MoveItems(ADBService.AdbDevice device, IEnumerable<FilePath> items, string targetPath, string currentPath, ObservableList<FileClass> fileList, Dispatcher dispatcher, bool isCopy = false)
    {
        if (targetPath == AdbExplorerConst.RECYCLE_PATH) // Recycle
        {
            var mdTask = Task.Run(() => MakeDir(device, AdbExplorerConst.RECYCLE_PATH));
            mdTask.ContinueWith((t) =>
            {
                dispatcher.Invoke(() =>
                {
                    foreach (var item in items)
                    {
                        Data.FileOpQ.AddOperation(new FileMoveOperation(dispatcher, device, item, targetPath, item.FullName, currentPath, fileList));
                    }
                });
            });
        }
        else if (targetPath is null && currentPath == AdbExplorerConst.RECYCLE_PATH) // Restore
        {
            foreach (var item in items)
            {
                if (((FileClass)item).Extension == AdbExplorerConst.RECYCLE_INDEX_SUFFIX)
                    continue;

                Data.FileOpQ.AddOperation(new FileMoveOperation(dispatcher, device, item, ((FileClass)item).TrashIndex.ParentPath, item.FullName, currentPath, fileList));
            }
        }
        else
        {
            foreach (var item in items)
            {
                var targetName = item.FullName;
                if (currentPath == targetPath)
                {
                    targetName = $"{((FileClass)item).NoExtName}{FileClass.ExistingIndexes(fileList, ((FileClass)item).NoExtName, isCopy)}{((FileClass)item).Extension}";
                }
                Data.FileOpQ.AddOperation(new FileMoveOperation(dispatcher, device, item, targetPath, targetName, currentPath, fileList, isCopy));
            }
        }
    }

    public static void MakeDir(ADBService.AdbDevice device, string fullPath)
    {
        var exitCode = ADBService.ExecuteDeviceAdbShellCommand(device.ID,
                                                               "mkdir",
                                                               out string stdout,
                                                               out string stderr,
                                                               new[] { "-p", ADBService.EscapeAdbShellString(fullPath) });

        if (exitCode != 0)
        {
            throw new Exception(stderr);
        }
    }

    public static void MakeFile(ADBService.AdbDevice device, string fullPath)
    {
        var exitCode = ADBService.ExecuteDeviceAdbShellCommand(device.ID,
                                                               "touch",
                                                               out string stdout,
                                                               out string stderr,
                                                               ADBService.EscapeAdbShellString(fullPath));

        if (exitCode != 0)
        {
            throw new Exception(stderr);
        }
    }

    public static void WriteLine(ADBService.AdbDevice device, string fullPath, string newLine)
    {
        var exitCode = ADBService.ExecuteDeviceAdbShellCommand(device.ID,
                                                               "echo",
                                                               out string stdout,
                                                               out string stderr,
                                                               new[] { newLine, ">>", ADBService.EscapeAdbShellString(fullPath) });

        if (exitCode != 0)
        {
            throw new Exception(stderr);
        }
    }

    public static string ReadAllText(ADBService.AdbDevice device, params string[] paths)
    {
        var exitCode = ADBService.ExecuteDeviceAdbShellCommand(device.ID,
                                                                "cat",
                                                                out string stdout,
                                                                out string stderr,
                                                                paths.Select(path => ADBService.EscapeAdbShellString(path)).ToArray());

        if (exitCode != 0)
            throw new Exception(stderr);

        return stdout;
    }

    public static async Task MoveItems(bool isCopy,
                                       string targetPath,
                                       IEnumerable<FileClass> pasteItems,
                                       string targetName,
                                       ObservableList<FileClass> fileList,
                                       Dispatcher dispatcher,
                                       ADBService.AdbDevice device,
                                       string currentPath)
    {
        bool merge = false;
        string[] existingItems = Array.Empty<string>();
        string destination = targetPath == AdbExplorerConst.TEMP_PATH ? "Temp" : targetName;

        await Task.Run(() =>
        {
            if (targetPath == currentPath)
                return;

            if (!targetPath.EndsWith('/'))
                targetPath += "/";

            existingItems = ADBService.FindFiles(device.ID, pasteItems.Select(file => $"{targetPath}{file.FullName}"));
            if (existingItems?.Any() is true)
            {
                existingItems = existingItems.Select(path => path[(path.LastIndexOf('/') + 1)..]).ToArray();

                if (pasteItems.Any(item => item.IsDirectory && existingItems.Contains(item.FullName)))
                    merge = true;
            }
        });

        await dispatcher.BeginInvoke(async () =>
        {
            string primaryText = "";
            if (merge)
            {
                if (pasteItems.All(item => item.IsDirectory))
                    primaryText = "Merge";
                else
                    primaryText = "Merge or Replace";
            }
            else
                primaryText = "Replace";

            if (existingItems.Length is int count and > 0)
            {
                var result = await DialogService.ShowConfirmation(
                    $"There {(count > 1 ? "are" : "is")} {count} conflicting item{(count > 1 ? "s" : "")} in {destination}",
                    "Paste Conflicts",
                    primaryText: primaryText,
                    secondaryText: count == pasteItems.Count() ? "" : "Skip",
                    cancelText: "Cancel",
                    icon: DialogService.DialogIcon.Exclamation);

                if (result.Item1 is ContentDialogResult.None)
                {
                    return;
                }
                else if (result.Item1 is ContentDialogResult.Secondary)
                {
                    pasteItems = pasteItems.Where(item => !existingItems.Contains(item.FullName));
                }
            }

            MoveItems(device: device,
                      items: pasteItems,
                      targetPath: targetPath,
                      currentPath: currentPath,
                      fileList: fileList,
                      dispatcher: dispatcher,
                      isCopy: isCopy);
        });
    }

    public static string GetPackageName(ADBService.AdbDevice device, string fullPath)
    {
        ADBService.ExecuteDeviceAdbShellCommand(device.ID,
                                                "pm",
                                                out string stdout,
                                                out _,
                                                "install",
                                                "-R",
                                                "--pkg",
                                                "''",
                                                ADBService.EscapeAdbShellString(fullPath));

        var match = AdbRegEx.RE_PACKAGE_NAME.Match(stdout);
        return match.Success ? match.Groups["package"].Value : fullPath[..fullPath.LastIndexOf('.')][(fullPath.LastIndexOf('/') + 1)..];
    }

    public static void InstallPackages(ADBService.AdbDevice device, IEnumerable<FileClass> items, Dispatcher dispatcher)
    {
        foreach (var item in items)
        {
            dispatcher.Invoke(() => Data.FileOpQ.AddOperation(new PackageInstallOperation(dispatcher, device, item)));
        }
    }

    public static void PushPackages(ADBService.AdbDevice device, IEnumerable<ShellObject> items, Dispatcher dispatcher, bool isPackagePath = false)
    {
        foreach (var item in items)
        {
            dispatcher.Invoke(() => Data.FileOpQ.AddOperation(new PackageInstallOperation(dispatcher, device, new(item), pushPackage: true, isPackagePath: isPackagePath)));
        }
    }

    public static void UninstallPackages(ADBService.AdbDevice device, IEnumerable<string> packages, Dispatcher dispatcher, ObservableList<Package> packageList)
    {
        foreach (var item in packages)
        {
            dispatcher.Invoke(() => Data.FileOpQ.AddOperation(new PackageInstallOperation(dispatcher, device, packageName: item, packageList: packageList)));
        }
    }

    public static ulong? GetPackagesCount(ADBService.AdbDevice device)
    {
        var result = ADBService.ExecuteDeviceAdbShellCommand(device.ID, "pm", out string stdout, out _, new[] { "list", "packages", "|", "wc", "-l" });
        if (result != 0 || !ulong.TryParse(stdout, out ulong value))
            return null;

        return value;
    }

    public static ObservableList<Package> GetPackages(ADBService.AdbDevice device, bool includeSystem = true, bool optionalParams = true)
    {
        // More package-specific info can be acquired using dumpsys package [package_name]

        ObservableList<Package> packages = new();
        string stdout = "";
        string[] args = { "list", "packages", "-s" };
        if (optionalParams)
            args = args.Concat(new[] { "-U", "--show-versioncode" }).ToArray();

        if (includeSystem)
        {
            // get system packages
            var systemExitCode = ADBService.ExecuteDeviceAdbShellCommand(device.ID, "pm", out stdout, out _, args);

            if (systemExitCode == 0)
                packages.AddRange(stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(pkg => Package.New(pkg, Package.PackageType.System)));
        }

        args[2] = "-3";
        // get user packages
        var userExitCode = ADBService.ExecuteDeviceAdbShellCommand(device.ID, "pm", out stdout, out _, args);

        if (userExitCode == 0)
            packages.AddRange(stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(pkg => Package.New(pkg, Package.PackageType.User)));

        return packages;
    }

    public static void ChangeDateFromName(ADBService.AdbDevice device, IEnumerable<FilePath> items, ObservableList<FileClass> fileList, Dispatcher dispatcher)
    {
        foreach (var item in items)
        {
            var match = AdbRegEx.RE_FILE_NAME_DATE.Match(item.FullName);
            if (!match.Success)
                continue;

            DateTime nameDate = DateTime.MinValue;
            var date = match.Groups["Date"].Value;
            var time = match.Groups["Time"].Value;
            var dateTime = match.Groups["DnT"].Value;

            if (DateOnly.TryParseExact(date, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out DateOnly res))
            {
                nameDate = res.ToDateTime(TimeOnly.MinValue);
                if (TimeOnly.TryParseExact(time, "HHmmss", null, System.Globalization.DateTimeStyles.None, out TimeOnly timeRes))
                    nameDate = res.ToDateTime(timeRes);
            }
            else if (DateTime.TryParseExact(dateTime, "yyyy-MM-dd-HH-mm-ss", null, System.Globalization.DateTimeStyles.None, out DateTime dntRes))
            {
                nameDate = dntRes;
            }
            else
                continue;

            if (((FileClass)item).ModifiedTime is DateTime modified && modified > nameDate)
            {
                dispatcher.Invoke(() => Data.FileOpQ.AddOperation(new FileChangeModifiedOperation(dispatcher, device, item, fileList, nameDate)));
            }
        }
    }
}
