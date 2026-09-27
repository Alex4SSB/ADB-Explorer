namespace ADB_Explorer.Services;

public class FileDeleteOperation : AbstractShellFileOperation
{
    public FileDeleteOperation(Dispatcher dispatcher, LogicalDeviceViewModel device, FileClass path)
        : base(path, device, dispatcher)
    {
        OperationName = OperationType.Delete;
        AltTarget = new(Navigation.SpecialLocation.devNull);

        if (path.TrashIndex is not null
            || path.FullPath.StartsWith(AdbExplorerConst.RECYCLE_PATH, StringComparison.Ordinal))
            AltSource = new(Navigation.SpecialLocation.RecycleBin);
    }

    public override void Start()
    {
        BeginInProgress();

        var task = AdbService.ExecuteVoidShellCommand(Device.ID, CancelTokenSource!.Token, "rm", "-rf", AdbService.EscapeAdbShellString(FilePath.FullPath));

        TrackTask(task, "Delete failed", SetParsedShellResult);
    }
}
