using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Views;

/// <summary>
/// Interaction logic for LogicalDriveControl.xaml
/// </summary>
public partial class LogicalDriveControl : UserControl
{
    public LogicalDriveControl()
    {
        InitializeComponent();
    }

    private void Button_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        ((DriveViewModel)((Button)sender).DataContext).BrowseCommand.Action();
    }

    private void Button_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        ((DriveViewModel)((Button)sender).DataContext).SelectCommand.Action();
    }
}
