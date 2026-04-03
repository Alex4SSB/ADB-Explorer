using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Views;

public class DriveControl : UserControl
{
    protected void Card_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DriveViewModel drive })
            drive.DriveSelected = true;
    }

    protected void Card_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DriveViewModel drive })
            drive.BrowseCommand.Execute();
    }

    protected void Card_KeyUp(object sender, KeyEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: DriveViewModel drive })
            return;

        if (e.Key is Key.Enter)
        {
            drive.BrowseCommand.Execute();
            e.Handled = true;
        }
        else if (e.Key is Key.Escape)
        {
            drive.DriveSelected = false;
            e.Handled = true;
        }
    }
}
