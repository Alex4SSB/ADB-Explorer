namespace ADB_Explorer.ViewModels;

internal class WaitingOpProgressViewModel : FileOpProgressViewModel
{
    public WaitingOpProgressViewModel() : base(Services.FileOperation.OperationStatus.Waiting)
    {

    }
}
