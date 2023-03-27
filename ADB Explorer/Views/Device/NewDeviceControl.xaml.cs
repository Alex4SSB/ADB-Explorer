using ADB_Explorer.Controls;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Views;

/// <summary>
/// Interaction logic for NewDeviceControl.xaml
/// </summary>
public partial class NewDeviceControl : UserControl
{
    public NewDeviceControl()
    {
        InitializeComponent();
    }

    private void IpBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is MaskedTextBox mtb && mtb.DataContext is NewDeviceViewModel device)
        {
            device.IsHostNameActive = false;
        }
    }

    private void HostNameBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is MaskedTextBox mtb && mtb.DataContext is NewDeviceViewModel device)
        {
            device.IsHostNameActive = true;
        }
    }

    private void IpBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is MaskedTextBox mtb && mtb.DataContext is NewDeviceViewModel device && string.IsNullOrEmpty(mtb.Text))
        {
            device.IsHostNameActive = null;
        }
    }
}
