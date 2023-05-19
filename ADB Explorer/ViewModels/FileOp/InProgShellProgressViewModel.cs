namespace ADB_Explorer.ViewModels;

internal class InProgShellProgressViewModel : FileOpProgressViewModel
{
    public InProgShellProgressViewModel() : base(Services.FileOperation.OperationStatus.InProgress)
    {

    }
}
