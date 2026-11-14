using AdvancedSharpAdbClient;
using Vanara.Extensions;
using Vanara.PInvoke;
using static ADB_Explorer.Models.AbstractFile;
using static Vanara.PInvoke.CldApi;

namespace ADB_Explorer.Services;

public sealed partial class DeviceMount
{
    private static readonly char[] WINDOWS_INVALID_NAME_CHARS = ['<', '>', ':', '"', '\\', '|', '?', '*'];

    private static readonly string[] WINDOWS_RESERVED_NAMES =
        ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
         "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"];

    private readonly ConcurrentDictionary<CF_TRANSFER_KEY, FetchCancellation> _fetches = new();

    /// <summary>The flag a synchronous pull checks between chunks.</summary>
    private sealed class FetchCancellation
    {
        public bool IsCancelled;
    }

    private void OnFetchPlaceholders(in CF_CALLBACK_INFO info, in CF_CALLBACK_PARAMETERS parameters)
    {
        var opInfo = OperationInfo(info, CF_OPERATION_TYPE.CF_OPERATION_TYPE_TRANSFER_PLACEHOLDERS);
        var devicePath = ToDevicePath(info.VolumeDosName + info.NormalizedPath);

        List<FileStat> entries = [];
        NTStatus status = NTStatus.STATUS_SUCCESS;
        try
        {
            if (devicePath is null)
                status = NTStatus.STATUS_OBJECT_PATH_NOT_FOUND;
            else
                entries = MountableEntries(AdbService.ListDirectoryEntries(DeviceId, devicePath, CancellationToken.None));
        }
        catch (Exception e)
        {
#if !DEPLOY
            DebugLog.PrintLine($"Mount listing of {devicePath} failed: {e.Message}");
#endif
            status = NTStatus.STATUS_UNSUCCESSFUL;
            entries = [];
        }

        TransferPlaceholders(opInfo, entries, status);
    }

    /// <summary>
    /// Folders and regular files whose names Windows can hold, without case-insensitive duplicates (first one wins).
    /// The app's recycle bin is left out, as are links, sockets and devices for now.
    /// </summary>
    private static List<FileStat> MountableEntries(IEnumerable<FileStat> entries)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return [.. entries.Where(e => e.Type is FileType.Folder or FileType.File
                                      && !e.IsLink
                                      && !FileHelper.IsInTrash(e.FullPath)
                                      && IsValidWindowsName(e.FullName)
                                      && names.Add(e.FullName))];
    }

    private static bool IsValidWindowsName(string name)
    {
        if (name.IndexOfAny(WINDOWS_INVALID_NAME_CHARS) >= 0 || name.Any(char.IsControl))
            return false;

        if (name.EndsWith('.') || name.EndsWith(' '))
            return false;

        var stem = name.Split('.')[0];

        return !WINDOWS_RESERVED_NAMES.Contains(stem, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// For emptying a sync root without its device: every folder lists as empty, and nothing refuses a delete.
    /// Static, so the delegates outlive the connection they're registered with.
    /// </summary>
    private static readonly CF_CALLBACK_REGISTRATION[] CleanupCallbacks =
    [
        new() { Type = CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_FETCH_PLACEHOLDERS, Callback = OnFetchPlaceholdersForCleanup },
        CF_CALLBACK_REGISTRATION.CF_CALLBACK_REGISTRATION_END,
    ];

    private static void OnFetchPlaceholdersForCleanup(in CF_CALLBACK_INFO info, in CF_CALLBACK_PARAMETERS parameters)
        => TransferPlaceholders(OperationInfo(info, CF_OPERATION_TYPE.CF_OPERATION_TYPE_TRANSFER_PLACEHOLDERS), [], NTStatus.STATUS_SUCCESS);

    private static void TransferPlaceholders(CF_OPERATION_INFO opInfo, List<FileStat> entries, NTStatus status)
    {
        var size = Marshal.SizeOf<CF_PLACEHOLDER_CREATE_INFO>();
        var array = entries.Count > 0 ? Marshal.AllocHGlobal(size * entries.Count) : IntPtr.Zero;
        List<IntPtr> identities = [];
        var written = 0;

        try
        {
            foreach (var entry in entries)
            {
                // A file's identity is its device path, which hydration reads back
                var identity = Marshal.StringToHGlobalUni(entry.FullPath);
                identities.Add(identity);

                var time = (entry.ModifiedTime ?? DateTime.Now).ToFileTimeStruct();
                var isFolder = entry.Type is FileType.Folder;
                var attributes = FileFlagsAndAttributes.FILE_ATTRIBUTE_DIRECTORY;
                if (!isFolder)
                    attributes = FileFlagsAndAttributes.FILE_ATTRIBUTE_READONLY;

                // Dot entries, which Android (and the app, unless shown) treats as hidden
                if (FilePath.IsHiddenName(entry.FullName))
                    attributes |= FileFlagsAndAttributes.FILE_ATTRIBUTE_HIDDEN;

                var create = new CF_PLACEHOLDER_CREATE_INFO
                {
                    RelativeFileName = entry.FullName,
                    FileIdentity = identity,
                    FileIdentityLength = (uint)((entry.FullPath.Length + 1) * sizeof(char)),
                    Flags = CF_PLACEHOLDER_CREATE_FLAGS.CF_PLACEHOLDER_CREATE_FLAG_MARK_IN_SYNC,
                    FsMetadata = new CF_FS_METADATA
                    {
                        FileSize = isFolder ? 0 : entry.Size ?? 0,
                        BasicInfo = new Kernel32.FILE_BASIC_INFO
                        {
                            FileAttributes = attributes,
                            CreationTime = time,
                            LastWriteTime = time,
                            LastAccessTime = time,
                            ChangeTime = time,
                        },
                    },
                };

                Marshal.StructureToPtr(create, array + (written * size), false);
                written++;
            }

            var opParams = CF_OPERATION_PARAMETERS.Create(new CF_OPERATION_PARAMETERS.TRANSFERPLACEHOLDERS
            {
                // Every child is listed at once, so the folder needn't be asked for again
                Flags = CF_OPERATION_TRANSFER_PLACEHOLDERS_FLAGS.CF_OPERATION_TRANSFER_PLACEHOLDERS_FLAG_DISABLE_ON_DEMAND_POPULATION,
                CompletionStatus = status,
                PlaceholderTotalCount = entries.Count,
                PlaceholderArray = array,
                PlaceholderCount = (uint)entries.Count,
            });

            var hr = CfExecute(opInfo, ref opParams);
#if !DEPLOY
            if (hr.Failed)
                DebugLog.PrintLine($"Mount placeholder transfer failed: {hr}");
#endif
        }
        finally
        {
            for (var i = 0; i < written; i++)
                Marshal.DestroyStructure<CF_PLACEHOLDER_CREATE_INFO>(array + (i * size));

            foreach (var identity in identities)
                Marshal.FreeHGlobal(identity);

            if (array != IntPtr.Zero)
                Marshal.FreeHGlobal(array);
        }
    }

    private void OnFetchData(in CF_CALLBACK_INFO info, in CF_CALLBACK_PARAMETERS parameters)
    {
        // The callback's buffers only live until it returns, so copy what the pull needs
        var opInfo = OperationInfo(info, CF_OPERATION_TYPE.CF_OPERATION_TYPE_TRANSFER_DATA);
        var devicePath = Marshal.PtrToStringUni(info.FileIdentity);
        var fileSize = info.FileSize;
        var fetch = parameters.FetchData;
        var cancellation = new FetchCancellation();
        _fetches[info.TransferKey] = cancellation;
        var transferKey = info.TransferKey;

        // A thread of its own, not the app's thread pool: when that's busy, a pull waits so long that the opening app gives up
        new Thread(() =>
        {
            try
            {
                Hydrate(opInfo, devicePath, fileSize, fetch.RequiredFileOffset, fetch.RequiredLength, cancellation);
            }
            finally
            {
                _fetches.TryRemove(KeyValuePair.Create(transferKey, cancellation));
            }
        })
        {
            IsBackground = true,
            Name = "Mount hydration",
        }.Start();
    }

    private void Hydrate(CF_OPERATION_INFO opInfo, string? devicePath, long fileSize, long requiredOffset, long requiredLength, FetchCancellation cancellation)
    {
        if (string.IsNullOrEmpty(devicePath))
        {
            CloudTransferStream.Fail(opInfo, requiredOffset, requiredLength);
            return;
        }

        using var transfer = new CloudTransferStream(opInfo, fileSize);
        try
        {
            using var service = new SyncService(Device.DeviceData);
            service.Pull(devicePath, transfer, null, Device.SupportsSyncV2, in cancellation.IsCancelled);

            // The platform cancelled it, so it expects nothing more
            if (Volatile.Read(ref cancellation.IsCancelled))
                return;

            transfer.Complete();
        }
        catch (Exception e)
        {
#if !DEPLOY
            DebugLog.PrintLine($"Mount hydration of {devicePath} failed: {e.Message}");
#endif
            if (!Volatile.Read(ref cancellation.IsCancelled))
                transfer.FailRemaining(requiredOffset, requiredLength);
        }
    }

    private void OnCancelFetchData(in CF_CALLBACK_INFO info, in CF_CALLBACK_PARAMETERS parameters)
    {
        if (_fetches.TryGetValue(info.TransferKey, out var cancellation))
            Volatile.Write(ref cancellation.IsCancelled, true);
    }

    private void CancelAllFetches()
    {
        foreach (var cancellation in _fetches.Values)
            Volatile.Write(ref cancellation.IsCancelled, true);
    }

    // Read-only for now: the device keeps its files as they are
    private void OnNotifyDelete(in CF_CALLBACK_INFO info, in CF_CALLBACK_PARAMETERS parameters)
    {
        var opInfo = OperationInfo(info, CF_OPERATION_TYPE.CF_OPERATION_TYPE_ACK_DELETE);
        var opParams = CF_OPERATION_PARAMETERS.Create(new CF_OPERATION_PARAMETERS.ACKDELETE { CompletionStatus = NTStatus.STATUS_ACCESS_DENIED });
        CfExecute(opInfo, ref opParams);
    }

    private void OnNotifyRename(in CF_CALLBACK_INFO info, in CF_CALLBACK_PARAMETERS parameters)
    {
        var opInfo = OperationInfo(info, CF_OPERATION_TYPE.CF_OPERATION_TYPE_ACK_RENAME);
        var opParams = CF_OPERATION_PARAMETERS.Create(new CF_OPERATION_PARAMETERS.ACKRENAME { CompletionStatus = NTStatus.STATUS_ACCESS_DENIED });
        CfExecute(opInfo, ref opParams);
    }

    private static CF_OPERATION_INFO OperationInfo(in CF_CALLBACK_INFO info, CF_OPERATION_TYPE type) => new()
    {
        StructSize = (uint)Marshal.SizeOf<CF_OPERATION_INFO>(),
        Type = type,
        ConnectionKey = info.ConnectionKey,
        TransferKey = info.TransferKey,
        RequestKey = info.RequestKey,
    };
}
