using AdvancedSharpAdbClient;
using Vanara.Extensions;
using Vanara.PInvoke;
using static ADB_Explorer.Models.AbstractFile;
using static Vanara.PInvoke.CldApi;

namespace ADB_Explorer.Services;

public sealed partial class DeviceMount
{
    private readonly ConcurrentDictionary<CF_TRANSFER_KEY, FetchCancellation> _fetches = new();

    /// <summary>The flag a synchronous pull checks between chunks.</summary>
    private sealed class FetchCancellation
    {
        public bool IsCancelled;
    }

    /// <summary>
    /// Folders and regular files. Where <paramref name="mapNames"/>, under their Windows names (<see cref="FileNameEncoding"/>),
    /// and a name differing from an earlier one only in case gets a spelling Windows tells apart; elsewhere names Windows refuses
    /// are left out. The app's recycle bin is left out too, as are links, sockets and devices.
    /// </summary>
    private sealed class MountableFilter(bool mapNames)
    {
        private readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase);

        public FileStat? Map(FileStat entry)
        {
            if (entry.Type is not (FileType.Folder or FileType.File) || entry.IsLink || FileHelper.IsInTrash(entry.FullPath))
                return null;

            if (!mapNames)
            {
                if (FileNameEncoding.IsValidWindowsName(entry.FullName) && _names.Add(entry.FullName))
                    return entry;

                return null;
            }

            if (FileNameEncoding.ToWindows(entry.FullName) is not { } name)
                return null;

            if (_names.Add(name))
                return entry with { FullName = name };

            foreach (var variant in FileNameEncoding.CaseVariants(name))
            {
                if (_names.Add(variant))
                    return entry with { FullName = variant };
            }

            return null;
        }
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
        => TransferPlaceholders(OperationInfo(info, CF_OPERATION_TYPE.CF_OPERATION_TYPE_TRANSFER_PLACEHOLDERS), [], NTStatus.STATUS_SUCCESS, true);

    /// <summary>
    /// Files need an identity, but hydration finds the device path from the placeholder's own path.
    /// A long identity (the device path, past ~130 bytes) corrupts the placeholders after it in the batch.
    /// </summary>
    private static readonly IntPtr PLACEHOLDER_IDENTITY = Marshal.StringToHGlobalAnsi(""); // a single zero byte, for the app's lifetime

    private static CF_PLACEHOLDER_CREATE_INFO CreateInfo(FileStat entry) => CreateInfo(entry, 0);

    private static CF_PLACEHOLDER_CREATE_INFO CreateInfo(FileStat entry, FileFlagsAndAttributes extraAttributes)
    {
        var time = (entry.ModifiedTime ?? DateTime.Now).ToFileTimeStruct();

        // LIST v1 has only the modified time, which then stands in for the others
        var accessTime = entry.AccessTime?.UtcDateTime.ToFileTimeStruct() ?? time;
        var changeTime = entry.ChangeTime?.UtcDateTime.ToFileTimeStruct() ?? time;
        var isFolder = entry.Type is FileType.Folder;
        var attributes = FileFlagsAndAttributes.FILE_ATTRIBUTE_DIRECTORY | extraAttributes;
        if (!isFolder)
            attributes = FileFlagsAndAttributes.FILE_ATTRIBUTE_READONLY | extraAttributes;

        // Dot entries, which Android (and the app, unless shown) treats as hidden
        if (FilePath.IsHiddenName(entry.FullName))
            attributes |= FileFlagsAndAttributes.FILE_ATTRIBUTE_HIDDEN;

        return new()
        {
            RelativeFileName = entry.FullName,
            FileIdentity = PLACEHOLDER_IDENTITY,
            FileIdentityLength = 1,
            Flags = CF_PLACEHOLDER_CREATE_FLAGS.CF_PLACEHOLDER_CREATE_FLAG_MARK_IN_SYNC,
            FsMetadata = new CF_FS_METADATA
            {
                FileSize = isFolder ? 0 : entry.Size ?? 0,
                BasicInfo = new Kernel32.FILE_BASIC_INFO
                {
                    FileAttributes = attributes,
                    CreationTime = time,
                    LastWriteTime = time,
                    LastAccessTime = accessTime,
                    ChangeTime = changeTime,
                },
            },
        };
    }

    /// <param name="isComplete">Every child is in, so the folder needn't be asked for again.</param>
    private static void TransferPlaceholders(CF_OPERATION_INFO opInfo, List<FileStat> entries, NTStatus status, bool isComplete,
                                             FileFlagsAndAttributes extraAttributes = 0)
    {
        var size = Marshal.SizeOf<CF_PLACEHOLDER_CREATE_INFO>();
        var array = entries.Count > 0 ? Marshal.AllocHGlobal(size * entries.Count) : IntPtr.Zero;
        var written = 0;

        try
        {
            foreach (var entry in entries)
            {
                Marshal.StructureToPtr(CreateInfo(entry, extraAttributes), array + (written * size), false);
                written++;
            }

            var flags = CF_OPERATION_TRANSFER_PLACEHOLDERS_FLAGS.CF_OPERATION_TRANSFER_PLACEHOLDERS_FLAG_NONE;
            if (isComplete)
                flags = CF_OPERATION_TRANSFER_PLACEHOLDERS_FLAGS.CF_OPERATION_TRANSFER_PLACEHOLDERS_FLAG_DISABLE_ON_DEMAND_POPULATION;

            var opParams = CF_OPERATION_PARAMETERS.Create(new CF_OPERATION_PARAMETERS.TRANSFERPLACEHOLDERS
            {
                Flags = flags,
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

            if (array != IntPtr.Zero)
                Marshal.FreeHGlobal(array);
        }
    }

    private void OnFetchData(in CF_CALLBACK_INFO info, in CF_CALLBACK_PARAMETERS parameters)
    {
        // The callback's buffers only live until it returns, so copy what the pull needs
        var opInfo = OperationInfo(info, CF_OPERATION_TYPE.CF_OPERATION_TYPE_TRANSFER_DATA);
        var devicePath = ToDevicePath(info.VolumeDosName + info.NormalizedPath);
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

    private static CF_OPERATION_INFO OperationInfo(in CF_CALLBACK_INFO info, CF_OPERATION_TYPE type) => new()
    {
        StructSize = (uint)Marshal.SizeOf<CF_OPERATION_INFO>(),
        Type = type,
        ConnectionKey = info.ConnectionKey,
        TransferKey = info.TransferKey,
        RequestKey = info.RequestKey,
    };
}
