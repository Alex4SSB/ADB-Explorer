namespace ADB_Explorer.Services;

public class FileMoveOperation : AbstractShellFileOperation
{
    public string RecycleName = "";
    public string IndexerPath = "";
    public DateTime? DateModified;
    public readonly bool isLink;

    public FileMoveOperation(FileClass filePath, SyncFile targetPath, LogicalDeviceViewModel device, Dispatcher dispatcher, DragDropEffects cutType = DragDropEffects.None)
        : base(filePath, device, dispatcher)
    {
        if (cutType is DragDropEffects.Copy or DragDropEffects.Link)
            OperationName = OperationType.Copy;
        else if (targetPath.FullPath.StartsWith(AdbExplorerConst.RECYCLE_PATH))
        {
            OperationName = OperationType.Recycle;
            AltTarget = new(Navigation.SpecialLocation.RecycleBin);
        }
        else if (filePath.TrashIndex is not null)
        {
            OperationName = OperationType.Restore;
            AltSource = new(Navigation.SpecialLocation.RecycleBin);
        }
        else
            OperationName = OperationType.Move;

        TargetPath = targetPath;
        isLink = cutType is DragDropEffects.Link;
    }

    public override void Start()
    {
        BeginInProgress();

        if (OperationName is OperationType.Recycle)
        {
            RecycleName = $"{{{DateTimeOffset.Now.ToUnixTimeMilliseconds()}}}";
            TargetPath.UpdatePath(FileHelper.ConcatPaths(TargetPath.ParentPath, RecycleName));
            IndexerPath = $"{AdbExplorerConst.RECYCLE_PATH}/.{RecycleName}{AdbExplorerConst.RECYCLE_INDEX_SUFFIX}";
        }
        else if (OperationName is OperationType.Restore)
        {
            RecycleName = FilePath.TrashIndex!.RecycleName;
            IndexerPath = FilePath.TrashIndex!.IndexerPath;
        }

        var cmd = OperationName switch
        {
            OperationType.Copy when isLink => "ln",
            OperationType.Copy => "cp",
            _ => "mv",
        };
        var flag = "";

        if (isLink)
        {
            flag += "s";
        }
        else if (OperationName is OperationType.Copy)
        {
            flag += "p"; // Preserve timestamps, ownership, and mode
            if (FilePath.IsDirectory)
                flag += "r"; // Recurse into subdirectories (DEST must be a directory)
        }

        if (flag.Length > 0)
            flag = "-" + flag;

        if (OperationName is OperationType.Copy or OperationType.Recycle)
            DateModified = DateTime.Now;

        var operationTask = AdbService.ExecuteVoidShellCommand(Device.ID, CancelTokenSource!.Token, cmd, flag,
            AdbService.EscapeAdbShellString(FilePath.FullPath),
            AdbService.EscapeAdbShellString(TargetPath.FullPath));

        TrackTask(operationTask, "Move failed", result =>
        {
            SetParsedShellResult(result);

            if (Status is OperationStatus.Failed && OperationName is OperationType.Recycle)
                ShellFileOperation.SilentDelete(Device, TargetPath.FullPath, IndexerPath);
        });
    }
}
