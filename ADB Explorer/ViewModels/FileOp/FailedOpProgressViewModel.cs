namespace ADB_Explorer.ViewModels;

internal class FailedOpProgressViewModel : FileOpProgressViewModel
{
    public string Error { get; }

    public FailedOpProgressViewModel(string error) : base(Services.FileOperation.OperationStatus.Failed)
    {
        error = error.TrimEnd('\r', '\n');

        var firstDoubleSlash = error.StartsWith(@"\\");
        error = string.Join(@"\", error.Split(@"\\"));
        if (firstDoubleSlash)
            error = @"\\" + error;

        Error = error;
    }
}
