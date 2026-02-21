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
        if (((Button)sender).DataContext is DriveViewModel drive)
            drive.BrowseCommand.Execute();
    }

    private void Button_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (((Button)sender).DataContext is DriveViewModel drive)
            drive.SelectCommand.Execute();
    }
}
