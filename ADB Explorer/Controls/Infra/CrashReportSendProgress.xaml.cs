namespace ADB_Explorer.Controls;

/// <summary>
/// Progress UI shown while a crash report is being sent, then the send result message.
/// </summary>
[ObservableObject]
public partial class CrashReportSendProgress : UserControl
{
    [ObservableProperty]
    public partial bool IsProgressVisible { get; set; } = true;

    [ObservableProperty]
    public partial string StatusText { get; set; } = "";

    public CrashReportSendProgress()
    {
        InitializeComponent();
    }

    public void Start(string status)
    {
        IsProgressVisible = true;
        StatusText = status;
    }

    public void Complete(string message)
    {
        IsProgressVisible = false;
        StatusText = message;
    }
}
