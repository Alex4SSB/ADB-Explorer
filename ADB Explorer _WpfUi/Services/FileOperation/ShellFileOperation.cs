using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services.AppInfra;
using Vanara.Windows.Shell;

namespace ADB_Explorer.Services;

public abstract class AbstractShellFileOperation : FileOperation
{
    public override FileClass FilePath { get; }

    public override SyncFile AndroidPath => TargetPath;

    protected AbstractShellFileOperation(FileClass filePath, ADBService.AdbDevice adbDevice, Dispatcher dispatcher)
        : base(filePath, adbDevice, dispatcher)
    {
        if (filePath is not null)
        {
            FilePath = filePath;
            TargetPath = new(filePath);
        }
    }

    public override void ClearChildren()
    {
        AndroidPath.Children.Clear();
        AndroidPath.ProgressUpdates.Clear();
    }

    public override void AddUpdates(IEnumerable<FileOpProgressInfo> newUpdates)
        => AndroidPath.AddUpdates(newUpdates);

    public override void AddUpdates(params FileOpProgressInfo[] newUpdates)
        => AndroidPath.AddUpdates(newUpdates);
}

public static class ShellFileOperation
{
    public static void SilentDelete(ADBService.AdbDevice device, IEnumerable<FilePath> items)
        => SilentDelete(device, items.Select(item => item.FullPath).ToArray());

    public static void SilentDelete(ADBService.AdbDevice device, params string[] items)
    {
        string[] args = ["-rf", .. items.Select(item => ADBService.EscapeAdbShellString(item))];
        ADBService.ExecuteDeviceAdbShellCommand(device.ID, "rm", out _, out _, CancellationToken.None, args);
    }

    public static void DeleteItems(ADBService.AdbDevice device, IEnumerable<FileClass> items, Dispatcher dispatcher)
    {
        foreach (var item in items)
        {
            var fileOp = new FileDeleteOperation(dispatcher, device, item);
            fileOp.PropertyChanged += DeleteFileOp_PropertyChanged;

            Data.FileOpQ.AddOperation(fileOp);
        }
    }

    private static void DeleteFileOp_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        var op = sender as FileDeleteOperation;

        // when operation completes, remove this event handler anyway
        if (e.PropertyName is not nameof(FileOperation.Status) || op.Status is not FileOperation.OperationStatus.Completed)
            return;

        // delete file trash indexer if present, even if not current device
        if (op.FilePath.TrashIndex is TrashIndexer indexer)
            SilentDelete(op.Device, indexer.IndexerPath);

        if (op.Device.ID == Data.CurrentADBDevice.ID)
        {
            // remove file from cut items and clear its trash indexer if current device
            op.FilePath.CutState = DragDropEffects.None;
            op.FilePath.TrashIndex = null;

            // update UI if current path
            if (op.TargetPath.ParentPath == Data.CurrentPath)
            {
                Data.DirList.FileList.Remove(op.FilePath);
                FileActionLogic.UpdateFileActions();
            }
        }

        op.PropertyChanged -= DeleteFileOp_PropertyChanged;
    }

    public static void Rename(FileClass item, string targetPath, ADBService.AdbDevice device)
    {
        var fileOp = new FileRenameOperation(item, targetPath, device, App.Current.Dispatcher);
        fileOp.PropertyChanged += RenameFileOp_PropertyChanged;

        Data.FileOpQ.AddOperation(fileOp);
    }

    private static void RenameFileOp_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        var op = sender as FileRenameOperation;

        // when operation completes, remove this event handler anyway
        if (e.PropertyName is not nameof(FileOperation.Status) || op.Status is not FileOperation.OperationStatus.Completed)
            return;

        if (op.Device.ID == Data.CurrentADBDevice.ID
            && op.FilePath.ParentPath == Data.CurrentPath)
        {
            var file = Data.DirList.FileList.Find(f => f.FullPath == op.FilePath.FullPath);

            // update UI when on current device and current path
            op.Dispatcher.Invoke(() => file.UpdatePath(op.TargetPath.FullPath));

            if (Data.SelectedFiles.Count() == 1 && Data.SelectedFiles.First() == file)
                Data.FileActions.ItemToSelect = null;

            // only select the item if there aren't any other operations
            if (Data.FileOpQ.TotalCount == 1)
                Data.FileActions.ItemToSelect = file;
        }

        op.PropertyChanged -= RenameFileOp_PropertyChanged;
    }

    public static bool SilentMove(ADBService.AdbDevice device, FilePath item, string targetPath) => SilentMove(device, item.FullPath, targetPath);

    public static bool SilentMove(ADBService.AdbDevice device, string fullPath, string targetPath, bool throwOnError = true)
    {
        var exitCode = ADBService.ExecuteDeviceAdbShellCommand(device.ID,
                                                               "mv",
                                                               out _,
                                                               out var stderr,
                                                               CancellationToken.None,
                                                               ADBService.EscapeAdbShellString(fullPath),
                                                               ADBService.EscapeAdbShellString(targetPath));

        if (exitCode != 0 && throwOnError)
        {
            throw new Exception(stderr);
        }

        return exitCode == 0;
    }

    public static void MoveItems(ADBService.AdbDevice device,
                                 IEnumerable<FileClass> items,
                                 string targetPath,
                                 string currentPath,
                                 ObservableList<FileClass> fileList,
                                 Dispatcher dispatcher,
                                 DragDropEffects cutType = DragDropEffects.None)
        => MoveItems(device,
                     items,
                     targetPath,
                     currentPath,
                     fileList.Select(f => f.FullName),
                     dispatcher,
                     cutType);

    public static void MoveItems(ADBService.AdbDevice device,
                                 IEnumerable<FileClass> items,
                                 string targetPath,
                                 string currentPath,
                                 IEnumerable<string> existingItems,
                                 Dispatcher dispatcher,
                                 DragDropEffects cutType = DragDropEffects.None,
                                 int masterPid = 0)
    {
        IEnumerable<FileMoveOperation> Recycle()
        {
            foreach (var item in items)
            {
                SyncFile target = new(FileHelper.ConcatPaths(targetPath, item.FullName), item.Type);
                yield return new(item, target, device, dispatcher);
            }
        }

        IEnumerable<FileMoveOperation> Restore()
        {
            if (Data.RecycleIndex.Count == 0)
                TrashHelper.ParseIndexers();

            foreach (var item in items)
            {
                if (item.Extension == AdbExplorerConst.RECYCLE_INDEX_SUFFIX)
                    continue;

                var recycleName = item.TrashIndex is null
                    ? item.FullName
                    : FileHelper.GetFullName(item.TrashIndex.RecycleName);

                var indexer = Data.RecycleIndex.FirstOrDefault(f => f.RecycleName == recycleName);
                if (indexer is null)
                    continue;

                item.UpdatePath(FileHelper.ConcatPaths(AdbExplorerConst.RECYCLE_PATH, recycleName));
                item.TrashIndex = indexer;
                var targetParent = string.IsNullOrEmpty(targetPath)
                    ? indexer.ParentPath
                    : targetPath;

                SyncFile target = new(FileHelper.ConcatPaths(targetParent, item.FullName));
                yield return new(item, target, device, dispatcher);
            }
        }

        IEnumerable<FileMoveOperation> Move()
        {
            foreach (var item in items)
            {
                var targetName = item.FullName;
                if (currentPath == targetPath)
                    targetName = FileHelper.DuplicateFile(existingItems, targetName, cutType);

                SyncFile target = new(FileHelper.ConcatPaths(targetPath, targetName));
                yield return new(item, target, device, dispatcher, cutType);
            }
        }

        List<FileMoveOperation> fileops = [];
        items = [.. items];

        if (items.First().ParentPath == AdbExplorerConst.RECYCLE_PATH || currentPath == AdbExplorerConst.RECYCLE_PATH)
            fileops = [.. Restore()];
        else if (targetPath == AdbExplorerConst.RECYCLE_PATH)
            fileops = [.. Recycle()];
        else
            fileops = [.. Move()];

        fileops.ForEach(op => op.MasterPid = masterPid);

        dispatcher.Invoke(() =>
        {
            fileops.ForEach(op => op.PropertyChanged += MoveFileOp_PropertyChanged);
            Data.FileOpQ.AddOperations(fileops);
        });
    }

    private static void MoveFileOp_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        var op = sender as FileMoveOperation;

        // when operation completes, remove this event handler anyway
        if (e.PropertyName is nameof(FileOperation.Status)
            && op.Status is FileOperation.OperationStatus.Completed)
        {
            // write or delete indexer, even if not current device
            if (op.OperationName is FileOperation.OperationType.Recycle)
            {
                TrashIndexer indexer = new(op);
                WriteLine(op.Device, op.IndexerPath, ADBService.EscapeAdbShellString(indexer.ToString()));
            }
            else if (op.OperationName is FileOperation.OperationType.Restore)
            {
                SilentDelete(op.Device, op.IndexerPath);
            }

            // remove file from cut items
            op.FilePath.CutState = DragDropEffects.None;

            if (op.Device.ID == Data.CurrentADBDevice.ID)
            {
                // notify master process of completion
                if (op.MasterPid > 0 && op.OperationName is not FileOperation.OperationType.Copy)
                {
                    IpcService.NotifyFileMoved(op.MasterPid, op.Device, op.FilePath);
                }

                // clear file trash indexer if restore / recycle on current device
                if (op.OperationName is FileOperation.OperationType.Recycle or FileOperation.OperationType.Restore)
                {
                    op.FilePath.TrashIndex = null;
                }

                // update UI when copy / cut target is current path
                if (op.TargetPath.ParentPath == Data.CurrentPath)
                {
                    if (op.OperationName is FileOperation.OperationType.Copy)
                    {
                        FileClass newFile = new(op.FilePath)
                        {
                            IsLink = op.isLink
                        };
                        newFile.UpdatePath(op.TargetPath.FullPath);
                        newFile.ModifiedTime = op.DateModified;
                        
                        Data.DirList.FileList.Add(newFile);

                        // only select the item if there aren't any other operations
                        if (Data.FileOpQ.TotalCount == 1)
                            Data.FileActions.ItemToSelect = newFile;
                    }
                    else
                    {
                        op.FilePath.UpdatePath(op.TargetPath.FullPath);
                        Data.DirList.FileList.Add(op.FilePath);

                        // only select the item if there aren't any other operations
                        if (Data.FileOpQ.TotalCount == 1)
                            Data.FileActions.ItemToSelect = op.FilePath;
                    }

                    FileActionLogic.UpdateFileActions();
                }

                // update UI when cut / restore / recycle source is current path
                else if (op.FilePath.ParentPath == Data.CurrentPath && op.OperationName is not FileOperation.OperationType.Copy)
                {
                    Data.DirList.FileList.Remove(op.FilePath);
                    FileActionLogic.UpdateFileActions();
                }
            }

            op.PropertyChanged -= MoveFileOp_PropertyChanged;
        }
    }

    public static async void MakeDir(ADBService.AdbDevice device, string fullPath)
    {
        var result = await ADBService.ExecuteVoidShellCommand(device.ID,
                                                              CancellationToken.None,
                                                              "mkdir",
                                                              ["-p", ADBService.EscapeAdbShellString(fullPath)]);

        if (!string.IsNullOrEmpty(result))
        {
            throw new Exception(result);
        }
    }

    public static async void MakeDirs(ADBService.AdbDevice device, IEnumerable<string> paths)
    {
        var result = await ADBService.ExecuteVoidShellCommand(device.ID,
                                                              CancellationToken.None,
                                                              "mkdir",
                                                              ["-p", .. paths.Select(path => ADBService.EscapeAdbShellString(path))]);
        if (!string.IsNullOrEmpty(result))
        {
            throw new Exception(result);
        }
    }

    public static async void MakeFile(ADBService.AdbDevice device, string fullPath)
    {
        var result = await ADBService.ExecuteVoidShellCommand(device.ID,
                                                              CancellationToken.None,
                                                              "touch",
                                                              ADBService.EscapeAdbShellString(fullPath));

        if (!string.IsNullOrEmpty(result))
        {
            throw new Exception(result);
        }
    }

    public static async void WriteLine(ADBService.AdbDevice device, string fullPath, string newLine)
    {
        var result = await ADBService.ExecuteVoidShellCommand(device.ID,
                                                              CancellationToken.None,
                                                              "echo",
                                                              [newLine, ">>", ADBService.EscapeAdbShellString(fullPath)]);

        if (!string.IsNullOrEmpty(result))
        {
            throw new Exception(result);
        }
    }

    public static string ReadAllText(ADBService.AdbDevice device, params string[] paths)
    {
        var exitCode = ADBService.ExecuteDeviceAdbShellCommand(device.ID,
                                                               "cat",
                                                               out string stdout,
                                                               out string stderr,
                                                               CancellationToken.None, paths.Select(path => ADBService.EscapeAdbShellString(path)).ToArray());

        if (exitCode != 0)
            throw new Exception(stderr);

        return stdout;
    }

    public static string GetPackageName(ADBService.AdbDevice device, string fullPath)
    {
        ADBService.ExecuteDeviceAdbShellCommand(device.ID,
                                                "pm",
                                                out string stdout,
                                                out _,
                                                CancellationToken.None,
                                                "install",
                                                "-R",
                                                "--pkg",
                                                "''",
                                                ADBService.EscapeAdbShellString(fullPath));

        var match = AdbRegEx.RE_PACKAGE_NAME().Match(stdout);
        return match.Success ? match.Groups["package"].Value : fullPath[..fullPath.LastIndexOf('.')][(fullPath.LastIndexOf('/') + 1)..];
    }

    public static void InstallPackages(ADBService.AdbDevice device, IEnumerable<FileClass> items, Dispatcher dispatcher)
    {
        foreach (var item in items)
        {
            var op = new PackageInstallOperation(dispatcher, device, item);
            op.PropertyChanged += InstallOp_PropertyChanged;

            Data.FileOpQ.AddOperation(op);
        }
    }

    public static void PushPackages(ADBService.AdbDevice device, IEnumerable<ShellItem> items, Dispatcher dispatcher)
    {
        foreach (var item in items.Select(file => new FilePath(file)))
        {
            var op = new PackageInstallOperation(dispatcher, device, new(item), pushPackage: true);
            op.PropertyChanged += InstallOp_PropertyChanged;
            
            Data.FileOpQ.AddOperation(op);
        }
    }

    public static void UninstallPackages(ADBService.AdbDevice device, IEnumerable<string> packages, Dispatcher dispatcher)
    {
        foreach (var item in packages)
        {
            var op = new PackageInstallOperation(dispatcher, device, packageName: item);
            op.PropertyChanged += InstallOp_PropertyChanged;

            Data.FileOpQ.AddOperation(op);
        }
    }

    private static void InstallOp_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        var op = sender as PackageInstallOperation;

        // when operation completes, remove this event handler anyway
        if (e.PropertyName is not nameof(FileOperation.Status) || op.Status is not FileOperation.OperationStatus.Completed)
            return;

        if (op.Device.ID == Data.CurrentADBDevice.ID
            && Data.FileActions.IsAppDrive)
        {
            // update UI when on current device and current path
            if (op.IsUninstall)
                Data.Packages.RemoveAll(pkg => pkg.Name == op.PackageName);
            else if (op.PushPackage)
                Data.FileActions.RefreshPackages = true;
        }

        op.PropertyChanged -= InstallOp_PropertyChanged;
    }

    public static ulong? GetPackagesCount(ADBService.AdbDevice device)
    {
        var result = ADBService.ExecuteDeviceAdbShellCommand(device.ID, "pm", out string stdout, out _, CancellationToken.None, ["list", "packages", "|", "wc", "-l"]);
        if (result != 0 || !ulong.TryParse(stdout, out ulong value))
            return null;

        return value;
    }

    public static ObservableList<Package> GetPackages(ADBService.AdbDevice device, bool includeSystem = true, bool optionalParams = true)
    {
        // More package-specific info can be acquired using dumpsys package [package_name]

        ObservableList<Package> packages = [];
        string stdout = "";
        string[] args = ["list", "packages", "-s", "-f"];
        if (optionalParams)
            args = [.. args, "-U", "--show-versioncode"];

        if (includeSystem)
        {
            // get system packages
            var systemExitCode = ADBService.ExecuteDeviceAdbShellCommand(device.ID, "pm", out stdout, out _, CancellationToken.None, args);

            if (systemExitCode == 0)
                packages.AddRange(stdout.Split(ADBService.LINE_SEPARATORS, StringSplitOptions.RemoveEmptyEntries).Select(pkg => Package.New(pkg, Package.PackageType.System)));
        }

        args[2] = "-3";
        // get user packages
        var userExitCode = ADBService.ExecuteDeviceAdbShellCommand(device.ID, "pm", out stdout, out _, CancellationToken.None, args);

        if (userExitCode == 0)
            packages.AddRange(stdout.Split(ADBService.LINE_SEPARATORS, StringSplitOptions.RemoveEmptyEntries).Select(pkg => Package.New(pkg, Package.PackageType.User)));

        return packages;
    }

    public static void ChangeDateFromName(ADBService.AdbDevice device, IEnumerable<FileClass> items, Dispatcher dispatcher)
    {
        List<FileOperation> operations = [];

        foreach (var item in items)
        {
            var match = AdbRegEx.RE_FILE_NAME_DATE().Match(item.FullName);
            if (!match.Success)
                continue;

            DateTime nameDate = DateTime.MinValue;
            var date = match.Groups["Date"].Value;
            var time = match.Groups["Time"].Value;
            var dateTime = match.Groups["DnT"].Value;

            if (DateOnly.TryParseExact(date, "yyyyMMdd", null, DateTimeStyles.None, out DateOnly res))
            {
                nameDate = res.ToDateTime(TimeOnly.MinValue);
                if (TimeOnly.TryParseExact(time, "HHmmss", null, DateTimeStyles.None, out TimeOnly timeRes))
                    nameDate = res.ToDateTime(timeRes);
            }
            else if (DateTime.TryParseExact(dateTime, "yyyy-MM-dd-HH-mm-ss", null, DateTimeStyles.None, out DateTime dntRes))
            {
                nameDate = dntRes;
            }
            else
                continue;

            if (item.ModifiedTime is DateTime modified && modified > nameDate)
            {
                operations.Add(new FileChangeModifiedOperation(item, nameDate, device, dispatcher));
            }
        }

        operations.ForEach(op => op.PropertyChanged += ChangeModifiedOp_PropertyChanged);
        Data.FileOpQ.AddOperations(operations);
    }

    private static void ChangeModifiedOp_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        var op = sender as FileChangeModifiedOperation;

        // when operation completes, remove this event handler anyway
        if (e.PropertyName is not nameof(FileOperation.Status) || op.Status is not FileOperation.OperationStatus.Completed)
            return;

        if (op.Device.ID == Data.CurrentADBDevice.ID
            && op.FilePath.ParentPath == Data.CurrentPath)
        {
            // update UI when on current device and current path
            op.FilePath.ModifiedTime = op.NewDate;
        }

        op.PropertyChanged -= ChangeModifiedOp_PropertyChanged;
    }
}
