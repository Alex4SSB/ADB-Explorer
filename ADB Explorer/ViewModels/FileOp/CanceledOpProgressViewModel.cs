namespace ADB_Explorer.ViewModels;

internal class CanceledOpProgressViewModel : FileOpProgressViewModel
{
    public CanceledOpProgressViewModel() : base(Services.FileOperation.OperationStatus.Canceled)
    {
        
    }
}
