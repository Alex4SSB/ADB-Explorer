using Vanara.PInvoke;
using static ADB_Explorer.Models.AbstractFile;
using static Vanara.PInvoke.CldApi;

namespace ADB_Explorer.Services;

/// <summary>
/// Deletes and renames made in File Explorer, carried out on the device before Windows applies them locally.
/// They run as the app's own operations, so they show in its queue, keep its listings current and use its recycle bin.
/// </summary>
public sealed partial class DeviceMount
{
    // Windows gives up on an unanswered delete or rename after 60 seconds
    private static readonly TimeSpan CHANGE_TIMEOUT = TimeSpan.FromSeconds(50);

    private void OnNotifyDelete(in CF_CALLBACK_INFO info, in CF_CALLBACK_PARAMETERS parameters)
    {
        var opInfo = OperationInfo(info, CF_OPERATION_TYPE.CF_OPERATION_TYPE_ACK_DELETE);
        var devicePath = ToDevicePath(info.VolumeDosName + info.NormalizedPath);
        var isDirectory = parameters.Delete.Flags.HasFlag(CF_CALLBACK_DELETE_FLAGS.CF_CALLBACK_DELETE_FLAG_IS_DIRECTORY);

        // Answered from a thread of its own, so the callback returns at once
        RunChange(() =>
        {
            var status = DeleteOnDevice(devicePath, isDirectory);
            var opParams = CF_OPERATION_PARAMETERS.Create(new CF_OPERATION_PARAMETERS.ACKDELETE { CompletionStatus = status });
            CfExecute(opInfo, ref opParams);
        });
    }

    private void OnNotifyRename(in CF_CALLBACK_INFO info, in CF_CALLBACK_PARAMETERS parameters)
    {
        var opInfo = OperationInfo(info, CF_OPERATION_TYPE.CF_OPERATION_TYPE_ACK_RENAME);
        var rename = parameters.Rename;
        var source = ToDevicePath(info.VolumeDosName + info.NormalizedPath);
        var isDirectory = rename.Flags.HasFlag(CF_CALLBACK_RENAME_FLAGS.CF_CALLBACK_RENAME_FLAG_IS_DIRECTORY);

        // Moving out of the mount would leave a placeholder no provider serves: not supported yet
        string? target = null;
        if (rename.Flags.HasFlag(CF_CALLBACK_RENAME_FLAGS.CF_CALLBACK_RENAME_FLAG_TARGET_IN_SCOPE))
            target = ToDevicePath(info.VolumeDosName + rename.TargetPath);

        RunChange(() =>
        {
            var status = RenameOnDevice(source, target, isDirectory);
            var opParams = CF_OPERATION_PARAMETERS.Create(new CF_OPERATION_PARAMETERS.ACKRENAME { CompletionStatus = status });
            CfExecute(opInfo, ref opParams);
        });
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
