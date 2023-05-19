namespace ADB_Explorer.ViewModels;

internal class FailedOpProgressViewModel : FileOpProgressViewModel
{
    public string Error { get; }

    public FailedOpProgressViewModel(string error) : base(Services.FileOperation.OperationStatus.Failed)
    {
        Error = error;
    }
}
