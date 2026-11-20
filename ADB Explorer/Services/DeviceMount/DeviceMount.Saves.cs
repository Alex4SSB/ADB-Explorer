namespace ADB_Explorer.Services;

// Apps that save safely (Word, for one) rename the original to a temporary name, give the new content the original's name,
// then delete the renamed original. Carried out on the device as they come, the device would lack the file midway, and
// the app's recycle bin would fill with old copies. Instead the rename and the delete stay local, and the new content
// replaces the device's file when it's pushed. Without a replacement in time they were the user's own, and reach the device late.
public sealed partial class DeviceMount
{
    // Long enough for an app's save, short for a rename the user made
    private static readonly TimeSpan SAVE_WINDOW = TimeSpan.FromSeconds(10);

    /// <summary>An original renamed to a temporary name, which the device still has under its own.</summary>
    private sealed class SetAsideOriginal(string devicePath)
    {
        public string DevicePath { get; } = devicePath;

        public DateTime Deadline { get; } = DateTime.UtcNow + SAVE_WINDOW;

        public volatile bool IsDeleted;

        /// <summary>A new file took its name: the device's file is to become that.</summary>
        public volatile bool IsReplaced;
    }

    /// <summary>Set-aside originals, by their local (temporary) path.</summary>
    private readonly ConcurrentDictionary<string, SetAsideOriginal> _setAside = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Names apps give files while saving or editing them: never pushed, and a rename to one may be a save.</summary>
    private static bool IsTemporaryName(string name)
        => SKIPPED_PREFIXES.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        || name.EndsWith(TEMP_EXTENSION, StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(BACKUP_SUFFIX);

    /// <summary>Whether a rename sets a file aside for a save: from a regular name to a temporary one, within the mount.</summary>
    /// <param name="targetLocal">Null when the target is outside the mount.</param>
    private bool TrySetAside(string sourceLocal, string? targetLocal, string? sourceDevicePath, bool isDirectory)
    {
        if (isDirectory || targetLocal is null || sourceDevicePath is null || !CanChange(sourceDevicePath)
            || IsTemporaryName(Path.GetFileName(sourceLocal)) || !IsTemporaryName(Path.GetFileName(targetLocal)))
            return false;

        _setAside[targetLocal] = new(sourceDevicePath);
        _uploadSignal.Set();

        return true;
    }

    /// <summary>A new file under a set-aside original's name: its save went through.</summary>
    private void MarkReplaced(string? devicePath)
    {
        if (devicePath is null)
            return;

        foreach (var (local, original) in _setAside)
        {
            if (original.DevicePath != devicePath)
                continue;

            original.IsReplaced = true;
            if (original.IsDeleted)
                _setAside.TryRemove(KeyValuePair.Create(local, original));
        }
    }

    /// <summary>Set-aside originals whose time is up: replaced, they're forgotten; otherwise their rename or delete reaches the device.</summary>
    private void ResolveSetAside()
    {
        var now = DateTime.UtcNow;
        foreach (var (local, original) in _setAside)
        {
            if (now < original.Deadline || !_setAside.TryRemove(KeyValuePair.Create(local, original)) || original.IsReplaced)
                continue;

            if (original.IsDeleted)
            {
                RunChange(() => ReportLateChange(original.DevicePath, DeleteOnDevice(original.DevicePath, false)));
            }
            else
            {
                var target = ToDevicePath(local);
                RunChange(() => ReportLateChange(original.DevicePath, RenameOnDevice(original.DevicePath, target, false)));
            }
        }
    }

    private static void ReportLateChange(string devicePath, Vanara.PInvoke.NTStatus status)
    {
#if !DEPLOY
        if (status != Vanara.PInvoke.NTStatus.STATUS_SUCCESS)
            DebugLog.PrintLine($"Mount couldn't carry out a late change of {devicePath}: {status}");
#endif
    }
}
