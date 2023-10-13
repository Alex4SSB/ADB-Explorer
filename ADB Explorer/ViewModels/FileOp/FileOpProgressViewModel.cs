namespace ADB_Explorer.ViewModels;

public abstract class FileOpProgressViewModel : ViewModelBase
{
    public DateTime TimeStamp { get; }

    public string Time => TimeStamp.ToLongTimeString();

    public Services.FileOperation.OperationStatus Status { get; }

    public string Name => Status switch
    {
        Services.FileOperation.OperationStatus.Waiting => "Pending",
        Services.FileOperation.OperationStatus.InProgress => "Current",
        Services.FileOperation.OperationStatus.Completed => "Completed",
        Services.FileOperation.OperationStatus.Canceled => "Canceled",
        Services.FileOperation.OperationStatus.Failed => "Failed",
        _ => throw new NotSupportedException(),
    };

    private bool isValidationInProgress = false;
    public bool IsValidationInProgress
    {
        get => isValidationInProgress;
        set => Set(ref isValidationInProgress, value);
    }

    public FileOpProgressViewModel(Services.FileOperation.OperationStatus status)
    {
        TimeStamp = DateTime.Now;
        Status = status;
    }
}
