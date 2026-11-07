using Wpf.Ui.Controls;

namespace ADB_Explorer.Controls;

public partial class ConflictResolutionPanel
{
    public static readonly DependencyProperty MessageProperty =
        DependencyProperty.Register(nameof(Message), typeof(string), typeof(ConflictResolutionPanel));

    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    /// <summary>Set before the panel loads; the option texts are read once by their bindings.</summary>
    public int ConflictCount { get; init; }

    public string ReplaceText => Strings.Plurals.ReplaceFilesInDestination(ConflictCount);
    public string SkipText => Strings.Plurals.SkipFiles(ConflictCount);
    public string DecideText => Strings.Plurals.DecideForEachFile(ConflictCount);

    public FileMergeHelper.ConflictResolution? Choice { get; private set; }

    private ContentDialog? _host;

    public ConflictResolutionPanel()
    {
        InitializeComponent();
    }

    public void Attach(ContentDialog host) => _host = host;

    private void Replace_Click(object sender, RoutedEventArgs e)
    {
        Choice = FileMergeHelper.ConflictResolution.Replace;
        _host?.Hide(ContentDialogResult.Primary);
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        Choice = FileMergeHelper.ConflictResolution.SkipConflicts;
        _host?.Hide(ContentDialogResult.Primary);
    }

    private void Decide_Click(object sender, RoutedEventArgs e)
    {
        Choice = FileMergeHelper.ConflictResolution.PerFile;
        _host?.Hide(ContentDialogResult.Primary);
    }
}
