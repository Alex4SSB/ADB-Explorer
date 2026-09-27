namespace ADB_Explorer.Services;

public class FileChangeModifiedOperation : AbstractShellFileOperation
{
    public readonly DateTime NewDate;

    public FileChangeModifiedOperation(FileClass filePath, DateTime newDate, LogicalDeviceViewModel device, Dispatcher dispatcher)
        : base(filePath, device, dispatcher)
    {
        OperationName = OperationType.Update;
        NewDate = newDate;
    }

    public override void Start()
    {
        BeginInProgress();

        var operationTask = AdbService.ExecuteVoidShellCommand(Device.ID,
                                                                    CancelTokenSource!.Token,
                                                                    "touch",
                                                                    "-m",
                                                                    "-t",
                                                                    NewDate.ToString("yyyyMMddHHmm.ss"),
                                                                    AdbService.EscapeAdbShellString(FilePath.FullPath));

        TrackTask(operationTask, "Update failed", SetShellResult);
    }
}
