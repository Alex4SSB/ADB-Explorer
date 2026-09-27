namespace ADB_Explorer.Services;

public class FileRenameOperation : AbstractShellFileOperation
{
    public FileRenameOperation(FileClass filePath, string targetPath, LogicalDeviceViewModel device, Dispatcher dispatcher)
        : base(new(filePath), device, dispatcher)
    {
        OperationName = OperationType.Rename;

        TargetPath = new(targetPath, FilePath.Type);
    }

    public override void Start()
    {
        BeginInProgress();

        Task operationTask;
        if (ArchivePath.TryParse(FilePath.FullPath, out var archivePath, out var oldInternal, Device.ID)
            && ArchivePath.TryParse(TargetPath.FullPath, out var destArchive, out var newInternal, Device.ID)
            && archivePath == destArchive
            && !string.IsNullOrEmpty(oldInternal)
            && !string.IsNullOrEmpty(newInternal)
            && ArchiveHelper.CanPasteIntoArchive(FilePath.FullPath, Device.ID))
        {
            operationTask = Task.Run(() =>
            {
                var session = ArchiveOpProgressSession.FromToc(
                    this,
                    Device.ID,
                    archivePath,
                    CancelTokenSource!.Token,
                    phases: 2);

                ArchiveExtract.RenameTarMember(
                    Device.ID,
                    archivePath,
                    oldInternal,
                    newInternal,
                    CancelTokenSource!.Token,
                    session.OnLine,
                    session.BeginPhase);

                session.Finish();
            }, CancelTokenSource!.Token);
        }
        else
        {
            operationTask = AdbService.ExecuteVoidShellCommand(Device.ID,
                CancelTokenSource!.Token,
                "mv",
                AdbService.EscapeAdbShellString(FilePath.FullPath),
                AdbService.EscapeAdbShellString(TargetPath.FullPath));
        }

        if (operationTask is Task<string> shellTask)
            TrackTask(shellTask, "Rename failed", SetShellResult);
        else
            TrackTask(operationTask, "Rename failed");
    }
}
