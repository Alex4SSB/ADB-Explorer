using ADB_Explorer.Helpers;
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

    public string ReplaceText => Strings.Resources.S_REPLACE_FILES_IN_DESTINATION;
    public string SkipText => Strings.Resources.S_SKIP_THESE_FILES;
    public string DecideText => Strings.Resources.S_DECIDE_FOR_EACH_FILE;

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
