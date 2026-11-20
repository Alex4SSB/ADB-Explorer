using Vanara.PInvoke;
using static ADB_Explorer.Models.AbstractFile;
using static Vanara.PInvoke.CldApi;

namespace ADB_Explorer.Services;

/// <summary>
/// Deletes and renames made in File Explorer, carried out on the device before Windows applies them locally;
/// moves out of the mount once Windows has them downloaded. They run as the app's own operations, so they show
/// in its queue, keep its listings current and use its recycle bin.
/// </summary>
public sealed partial class DeviceMount
{
    // Windows gives up on an unanswered delete or rename after 60 seconds
    private static readonly TimeSpan CHANGE_TIMEOUT = TimeSpan.FromSeconds(50);

    // How long a move out of the mount may take to download, and how often it's checked on
    private static readonly TimeSpan MOVE_OUT_TIMEOUT = TimeSpan.FromMinutes(30);

    private static readonly TimeSpan MOVE_OUT_POLL = TimeSpan.FromMilliseconds(250);

    // A move into another device's mount checks that device, so less often
    private static readonly TimeSpan MOVE_INTO_MOUNT_POLL = TimeSpan.FromSeconds(2);

    private void OnNotifyDelete(in CF_CALLBACK_INFO info, in CF_CALLBACK_PARAMETERS parameters)
    {
        var opInfo = OperationInfo(info, CF_OPERATION_TYPE.CF_OPERATION_TYPE_ACK_DELETE);
        var localPath = info.VolumeDosName + info.NormalizedPath;
        var devicePath = ToDevicePath(localPath);
        var isDirectory = parameters.Delete.Flags.HasFlag(CF_CALLBACK_DELETE_FLAGS.CF_CALLBACK_DELETE_FLAG_IS_DIRECTORY);
        MarkChangeAnswered();

        // A refresh removing what's gone from the device
        if (IsSelfDelete(localPath))
        {
            AckDelete(opInfo, NTStatus.STATUS_SUCCESS);
            return;
        }

        // A set-aside original: replaced, the device's file is the new content; otherwise deleted there if no replacement comes
        if (_setAside.TryGetValue(localPath, out var original))
        {
            if (original.IsReplaced)
                _setAside.TryRemove(KeyValuePair.Create(localPath, original));
            else
                original.IsDeleted = true;

            AckDelete(opInfo, NTStatus.STATUS_SUCCESS);
            return;
        }

        // Answered from a thread of its own, so the callback returns at once
        RunChange(() => AckDelete(opInfo, DeleteOnDevice(devicePath, isDirectory)));
    }

    private void OnNotifyRename(in CF_CALLBACK_INFO info, in CF_CALLBACK_PARAMETERS parameters)
    {
        var opInfo = OperationInfo(info, CF_OPERATION_TYPE.CF_OPERATION_TYPE_ACK_RENAME);
        var rename = parameters.Rename;
        var sourceLocal = info.VolumeDosName + info.NormalizedPath;
        var source = ToDevicePath(sourceLocal);
        var isDirectory = rename.Flags.HasFlag(CF_CALLBACK_RENAME_FLAGS.CF_CALLBACK_RENAME_FLAG_IS_DIRECTORY);
        MarkChangeAnswered();

        // Within this mount, or out of it. Windows counts another sync root (another device's mount) as in scope too
        string? targetLocal = info.VolumeDosName + rename.TargetPath;
        string? target = null;
        if (rename.Flags.HasFlag(CF_CALLBACK_RENAME_FLAGS.CF_CALLBACK_RENAME_FLAG_TARGET_IN_SCOPE) && IsUnderRoot(targetLocal))
            target = ToDevicePath(targetLocal);
        else
            targetLocal = null;

        // A set-aside original renamed again: back, its save failed and the device has it as it was;
        // on, it goes from where the device still has it. Replaced already, the device's file is the new content.
        if (_setAside.TryRemove(sourceLocal, out var original))
        {
            if (original.IsReplaced || target == original.DevicePath)
            {
                AckRename(opInfo, NTStatus.STATUS_SUCCESS);
                return;
            }

            source = original.DevicePath;
        }
        else if (TrySetAside(sourceLocal, targetLocal, source, isDirectory))
        {
            AckRename(opInfo, NTStatus.STATUS_SUCCESS);
            return;
        }

        // Out of the mount: Windows downloads all of it as it moves it, leaving plain files. Only once they're there
        // is it removed from the device, so a move that fails midway (the device gone, say) loses nothing.
        if (targetLocal is null)
        {
            if (source is null || !CanChange(source))
            {
                AckRename(opInfo, NTStatus.STATUS_ACCESS_DENIED);
                return;
            }

            var outside = info.VolumeDosName + rename.TargetPath;
            var devicePath = source;
            AckRename(opInfo, NTStatus.STATUS_SUCCESS);
            RunChange(() => FinishMoveOut(sourceLocal, outside, devicePath, isDirectory));
            return;
        }

        RunChange(() => AckRename(opInfo, RenameOnDevice(source, target, isDirectory)));
    }

    /// <summary>Waits for a move out of the mount to finish, then deletes what moved from the device, if all of it arrived.</summary>
    private void FinishMoveOut(string sourceLocal, string outside, string devicePath, bool isDirectory)
    {
        // Placeholders are disguised as plain files by default, which would hide one left in what moved
        NativeMethods.RtlSetThreadPlaceholderCompatibilityMode(NativeMethods.PHCM_EXPOSE_PLACEHOLDERS);

        // A large folder takes as long as its download
        var watch = Stopwatch.StartNew();
        while (Path.Exists(sourceLocal) || !Path.Exists(outside))
        {
            if (watch.Elapsed > MOVE_OUT_TIMEOUT)
            {
#if !DEPLOY
                DebugLog.PrintLine($"Mount kept {devicePath} on the device: its move to {outside} didn't finish");
#endif
                return;
            }

            Thread.Sleep(MOVE_OUT_POLL);
        }

        // Into another device's mount: only once that device has all of it, as the push there can still fail
        if (OtherMountOf(outside) is { } other)
        {
            var waiting = LocalTree(outside);
            waiting.RemoveAll(other.HasArrived);
            while (waiting.Count > 0)
            {
                if (watch.Elapsed > MOVE_OUT_TIMEOUT)
                {
#if !DEPLOY
                    DebugLog.PrintLine($"Mount kept {devicePath} on the device: {other.Device.Name} didn't get all of {outside}");
#endif
                    return;
                }

                Thread.Sleep(MOVE_INTO_MOUNT_POLL);
                waiting.RemoveAll(other.HasArrived);
            }
        }
        else if (!IsFullyLocal(outside))
        {
#if !DEPLOY
            DebugLog.PrintLine($"Mount kept {devicePath} on the device: {outside} isn't all downloaded");
#endif
            return;
        }

        DeleteOnDevice(devicePath, isDirectory);
    }

    private DeviceMount? OtherMountOf(string path) => Mounts.FirstOrDefault(mount => mount != this && mount.IsUnderRoot(path));

    /// <summary>The item, and everything in it where it's a folder.</summary>
    private static List<string> LocalTree(string path)
    {
        List<string> result = [path];
        try
        {
            if (Directory.Exists(path))
                result.AddRange(Directory.EnumerateFileSystemEntries(path, "*", SearchOption.AllDirectories));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { }

        return result;
    }

    /// <summary>
    /// Whether this mount pushed a local folder or file to its device: a file is marked in sync once its push completed,
    /// and the device has it at its size.
    /// </summary>
    private bool HasArrived(string localPath)
    {
        if (ToDevicePath(localPath) is not { } devicePath)
            return false;

        try
        {
            var isFolder = Directory.Exists(localPath);
            if (!isFolder && (!IsPlaceholder(File.GetAttributes(localPath)) || IsInSync(localPath) is not true))
                return false;

            var stat = AdbService.SyncLstat(DeviceId, devicePath);
            if (isFolder)
                return stat.IsDirectory;

            return stat.IsRegularFile && stat.Size == new FileInfo(localPath).Length;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Whether nothing in a moved item is a placeholder: it's all on the PC, and the device's copy can go.</summary>
    private static bool IsFullyLocal(string path)
    {
        try
        {
            if (IsPlaceholder(File.GetAttributes(path)))
                return false;

            if (!Directory.Exists(path))
                return true;

            return new DirectoryInfo(path).EnumerateFileSystemInfos("*", SearchOption.AllDirectories).All(entry => !IsPlaceholder(entry.Attributes));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void AckDelete(CF_OPERATION_INFO opInfo, NTStatus status)
    {
        MarkChangeAnswered();
        var opParams = CF_OPERATION_PARAMETERS.Create(new CF_OPERATION_PARAMETERS.ACKDELETE { CompletionStatus = status });
        CfExecute(opInfo, ref opParams);
    }

    private void AckRename(CF_OPERATION_INFO opInfo, NTStatus status)
    {
        MarkChangeAnswered();
        var opParams = CF_OPERATION_PARAMETERS.Create(new CF_OPERATION_PARAMETERS.ACKRENAME { CompletionStatus = status });
        CfExecute(opInfo, ref opParams);
    }

    private static void RunChange(Action change) => new Thread(() =>
    {
        try
        {
            change();
        }
        catch (Exception e)
        {
            // An unanswered change times out in Windows, which leaves both sides as they were
#if !DEPLOY
            DebugLog.PrintLine($"Mount change failed: {e.Message}");
#endif
        }
    })
    {
        IsBackground = true,
        Name = "Mount change",
    }.Start();

    /// <summary>Into the app's recycle bin when it's enabled, as the app's own delete does, else for good.</summary>
    private NTStatus DeleteOnDevice(string? devicePath, bool isDirectory)
    {
        if (devicePath is null || !CanChange(devicePath))
            return NTStatus.STATUS_ACCESS_DENIED;

        var item = new FileClass(FileHelper.GetFullName(devicePath), devicePath, isDirectory ? FileType.Folder : FileType.File);
        List<FileOperation> operations;
        if (Data.Settings.EnableRecycle)
        {
            ShellFileOperation.MakeDir(Device, AdbExplorerConst.RECYCLE_PATH).GetAwaiter().GetResult();
            operations = [.. ShellFileOperation.MoveItems(Device, [item], AdbExplorerConst.RECYCLE_PATH, item.ParentPath, Array.Empty<string>(), App.AppDispatcher)];
        }
        else
        {
            operations = App.AppDispatcher.Invoke(() => ShellFileOperation.DeleteItems(Device, [item], App.AppDispatcher));
        }

        // Windows applies these itself: no refresh is needed for them
        AddOwnOperations(operations);

        return AwaitOperations(operations);
    }

    /// <summary>A rename, or a move to another folder of the mount (the app's move keeps the name).</summary>
    private NTStatus RenameOnDevice(string? source, string? target, bool isDirectory)
    {
        if (source is null || target is null || !CanChange(source) || !CanChange(target))
            return NTStatus.STATUS_ACCESS_DENIED;

        var item = new FileClass(FileHelper.GetFullName(source), source, isDirectory ? FileType.Folder : FileType.File);
        var targetParent = FileHelper.GetParentPath(target);
        List<FileOperation> operations;
        if (targetParent != item.ParentPath && FileHelper.GetFullName(target) == item.FullName)
        {
            operations = [.. ShellFileOperation.MoveItems(Device, [item], targetParent, item.ParentPath, Array.Empty<string>(), App.AppDispatcher, DragDropEffects.Move)];
        }
        else
        {
            operations = [App.AppDispatcher.Invoke(() => ShellFileOperation.Rename(item, target, Device))];
        }

        // Windows applies these itself: no refresh is needed for them
        AddOwnOperations(operations);

        return AwaitOperations(operations);
    }

    /// <summary>What the app itself won't change: a drive's root, the recycle bin, Android's protected folders, read-only places.</summary>
    private bool CanChange(string devicePath)
        => !IsDriveFolderOrIcon(devicePath)
        && IsMounted(devicePath)
        && !ShellAccessHelper.IsFuseProtectedAndroidRoot(devicePath)
        && DriveHelper.GetRestrictions(devicePath, Device).ReadOnly is not true;

    /// <summary>Waits for the queued operations, canceling them if they don't finish before Windows stops waiting.</summary>
    private static NTStatus AwaitOperations(List<FileOperation> operations)
    {
        if (operations.Count == 0)
            return NTStatus.STATUS_UNSUCCESSFUL;

        using var finished = new CountdownEvent(operations.Count);
        var failed = 0;
        foreach (var operation in operations)
        {
            var reported = 0;
            void Report(FileOperation.OperationStatus status)
            {
                if (Interlocked.Exchange(ref reported, 1) == 1)
                    return;

                if (status is not FileOperation.OperationStatus.Completed)
                    Interlocked.Increment(ref failed);

                finished.Signal();
            }

            operation.WhenFinished(Report);

            // It may have finished before there was anyone to tell
            if (IsFinal(operation.Status))
                Report(operation.Status);
        }

        if (!finished.Wait(CHANGE_TIMEOUT))
        {
            foreach (var operation in operations.Where(op => !IsFinal(op.Status)))
                operation.Cancel();

            return NTStatus.STATUS_IO_TIMEOUT;
        }

        if (failed == 0)
            return NTStatus.STATUS_SUCCESS;

        return NTStatus.STATUS_UNSUCCESSFUL;
    }

    private static bool IsFinal(FileOperation.OperationStatus status)
        => status is not (FileOperation.OperationStatus.None or FileOperation.OperationStatus.Waiting or FileOperation.OperationStatus.InProgress);
}
