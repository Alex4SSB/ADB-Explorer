using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Views;

/// <summary>
/// Interaction logic for VirtualDriveControl.xaml
/// </summary>
public partial class VirtualDriveControl : UserControl
{
    public VirtualDriveControl()
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

    private void Button_KeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter)
        {
            ((DriveViewModel)((Button)sender).DataContext).BrowseCommand.Action();
            e.Handled = true;
        }
    }
}
