namespace ADB_Explorer.ViewModels;

internal class CompletedShellProgressViewModel : FileOpProgressViewModel
{
    public string Message { get; }

    public CompletedShellProgressViewModel(string message = "Completed") : base(Services.FileOperation.OperationStatus.Completed)
    {
        Message = message;
    }
}
