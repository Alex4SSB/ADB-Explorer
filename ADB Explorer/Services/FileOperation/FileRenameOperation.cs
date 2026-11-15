namespace ADB_Explorer.Services;

public class FileRenameOperation : AbstractShellFileOperation
{
    public FileRenameOperation(FileClass filePath, string targetPath, LogicalDeviceViewModel device, Dispatcher dispatcher)
        : base(new(filePath), device, dispatcher)
    {
        OperationName = OperationType.Rename;

        TargetPath = new(targetPath, FilePath.Type);
    }

    private bool IsCaseOnlyRename
        => FilePath.FullPath != TargetPath.FullPath
        && string.Equals(FilePath.FullPath, TargetPath.FullPath, StringComparison.OrdinalIgnoreCase);

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
        else if (IsCaseOnlyRename)
        {
            // Case-insensitive storage takes "a" and "A" for the same file, so mv leaves it be: go through a temporary name
            var temporary = AdbService.EscapeAdbShellString($"{TargetPath.FullPath}.adbx-rename-{Guid.NewGuid():N}");
            operationTask = AdbService.ExecuteVoidShellCommand(Device.ID,
                CancelTokenSource!.Token,
                "mv",
                AdbService.EscapeAdbShellString(FilePath.FullPath),
                temporary,
                "&&",
                "mv",
                temporary,
                AdbService.EscapeAdbShellString(TargetPath.FullPath));
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
