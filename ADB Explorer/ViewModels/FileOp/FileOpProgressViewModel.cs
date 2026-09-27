namespace ADB_Explorer.ViewModels;

public abstract class FileOpProgressViewModel : ObservableObject
{
    public Services.FileOperation.OperationStatus Status { get; }

    private FileOpFilter.FilterType FilterType => Status switch
    {
        Services.FileOperation.OperationStatus.Waiting => FileOpFilter.FilterType.Pending,
        Services.FileOperation.OperationStatus.InProgress => FileOpFilter.FilterType.Running,
        Services.FileOperation.OperationStatus.Completed => FileOpFilter.FilterType.Completed,
        Services.FileOperation.OperationStatus.Canceled => FileOpFilter.FilterType.Canceled,
        Services.FileOperation.OperationStatus.Failed => FileOpFilter.FilterType.Failed,
        _ => throw new NotSupportedException(),
    };

    public string Name => FileOpFilter.GetFilterName(FilterType);

    private bool _isValidationInProgress = false;
    public bool IsValidationInProgress
    {
        get => _isValidationInProgress;
        set => SetProperty(ref _isValidationInProgress, value);
    }

    public FileOpProgressViewModel(Services.FileOperation.OperationStatus status)
    {
        Status = status;
    }
}
