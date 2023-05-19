namespace ADB_Explorer.ViewModels;

internal class CompletedShellProgressViewModel : FileOpProgressViewModel
{
    public CompletedShellProgressViewModel() : base(Services.FileOperation.OperationStatus.Completed)
    {

    }
}
