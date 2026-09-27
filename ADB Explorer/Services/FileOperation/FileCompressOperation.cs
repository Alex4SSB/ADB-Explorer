namespace ADB_Explorer.Services;

/// <summary>
/// Creates a tar-family archive on the device from selected paths (or an empty archive).
/// </summary>
public class FileCompressOperation : AbstractShellFileOperation
{
    public IReadOnlyList<string> SourcePaths { get; }

    public FileCompressOperation(
        FileClass archiveFile,
        IReadOnlyList<string> sourcePaths,
        LogicalDeviceViewModel device,
        Dispatcher dispatcher)
        : base(archiveFile, device, dispatcher)
    {
        SourcePaths = sourcePaths;
        TargetPath = new SyncFile(archiveFile);
        OperationName = OperationType.Compress;
    }

    public override void Start()
    {
        BeginInProgress();

        var operationTask = CreateArchiveAsync();

        TrackTask(operationTask, "Compress failed", SetArchiveResult);
    }

    private async Task<string> CreateArchiveAsync()
    {
        Dictionary<string, long> memberBytes;
        try
        {
            memberBytes = await Task.Run(
                () => ArchiveExtract.CollectCreateMemberBytes(Device.ID, SourcePaths, CancelTokenSource!.Token),
                CancelTokenSource!.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return "Canceled";
        }
        catch (Exception e)
        {
            return e.Message;
        }

        var session = new ArchiveOpProgressSession(this, memberBytes);
        var result = await ArchiveExtract.CreateTarArchiveAsync(
            Device.ID,
            FilePath.FullPath,
            SourcePaths,
            CancelTokenSource!.Token,
            session.OnLine).ConfigureAwait(false);

        if (result == "")
            session.Finish();

        return result;
    }
}
