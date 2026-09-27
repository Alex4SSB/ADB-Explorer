namespace ADB_Explorer.Services;

/// <summary>
/// Base for operations that move a <see cref="SyncFile"/> tree through AdvancedSharpAdbClient sync,
/// including sources staged out of an archive on the device.
/// </summary>
public abstract class AbstractSyncFileOperation : FileOperation
{
    public override SyncFile FilePath { get; }

    public int? MaxThreads { get; set; }

    /// <summary>Device temp root used when pulling from an archive; cleaned when the op finishes.</summary>
    public string? ArchivePullStagingRoot { get; set; }

    /// <summary>Original archive file path for hash validation (<c>tar --to-command</c> / <c>-O</c>).</summary>
    public string? ArchiveSourcePath { get; set; }

    /// <summary>Internal archive path of the selected member (empty = archive root).</summary>
    public string? ArchiveInternalPath { get; set; }

    public bool IsArchivePull => !string.IsNullOrEmpty(ArchiveSourcePath);

    protected IEnumerable<SyncFile> Files => [FilePath, .. FilePath.AllChildren()];

    protected AbstractSyncFileOperation(SyncFile sourcePath, LogicalDeviceViewModel device, Dispatcher dispatcher)
        : base(sourcePath, device, dispatcher)
    {
        FilePath = sourcePath;
    }

    public void SetArchivePullSource(string archivePath, string internalPath, string stagingRoot, string displayPath)
    {
        ArchiveSourcePath = archivePath;
        ArchiveInternalPath = internalPath;
        ArchivePullStagingRoot = stagingRoot;
        AltSource = new(displayPath);
    }

    protected void CleanupArchiveStaging()
    {
        if (string.IsNullOrEmpty(ArchivePullStagingRoot))
            return;

        var root = ArchivePullStagingRoot;
        ArchivePullStagingRoot = null;
        var deviceId = Device.ID;
        Task.Run(() => ArchiveExtract.CleanupStaging(deviceId, root));
    }
}
