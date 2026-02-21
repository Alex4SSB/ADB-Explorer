namespace ADB_Explorer.ViewModels;

internal class InProgShellProgressViewModel : FileOpProgressViewModel
{
    public string CurrentFileName => null;

    public string CurrentFileNameWithoutExtension => null;

    public InProgShellProgressViewModel() : base(Services.FileOperation.OperationStatus.InProgress)
    {

    }
}
