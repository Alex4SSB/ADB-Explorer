namespace ADB_Explorer.ViewModels;

internal class CompletedShellProgressViewModel : FileOpProgressViewModel
{
    public string Message { get; }

    public bool NoConfirmation => Message == Strings.Resources.S_SYNC_NO_CONFIRM;

    public CompletedShellProgressViewModel(string message = null) : base(Services.FileOperation.OperationStatus.Completed)
    {
        if (message is null)
            message = Strings.Resources.S_FILEOP_COMPLETED;

        Message = message;
    }
}
