namespace ADB_Explorer.Services;

/// <summary>
/// Extracts a selected archive member (file or directory) to a real device path.
/// Used when pasting archive clipboard items onto the device.
/// </summary>
public class FileExtractOperation : AbstractShellFileOperation
{
    public string ArchiveSourcePath { get; }
    public string ArchiveInternalPath { get; }
    public bool IsArchiveDirectory { get; }

    public FileExtractOperation(
        FileClass source,
        SyncFile targetPath,
        LogicalDeviceViewModel device,
        Dispatcher dispatcher)
        : base(source, device, dispatcher)
    {
        if (!ArchivePath.TryParse(source.FullPath, out var archivePath, out var internalPath, device.ID))
            throw new ArgumentException("Source is not an archive path.", nameof(source));

        ArchiveSourcePath = archivePath;
        ArchiveInternalPath = internalPath;
        IsArchiveDirectory = source.IsDirectory;
        TargetPath = targetPath;
        OperationName = OperationType.Extract;
    }

    public override void Start()
    {
        BeginInProgress();

        var operationTask = Task.Run(() =>
        {
            var toc = ArchiveListing.GetOrFetchToc(Device.ID, ArchiveSourcePath, CancelTokenSource!.Token);
            var memberBytes = ArchiveVerboseProgress.MemberBytesForSelection(
                toc.Entries,
                ArchiveInternalPath,
                IsArchiveDirectory);
            var session = new ArchiveOpProgressSession(this, memberBytes);

            ArchiveExtract.ExtractSelection(
                Device.ID,
                ArchiveSourcePath,
                ArchiveInternalPath,
                IsArchiveDirectory,
                TargetPath.FullPath,
                CancelTokenSource!.Token,
                session.OnLine);

            session.Finish();
        }, CancelTokenSource!.Token);

        TrackTask(operationTask, "Extract failed");
    }
}
