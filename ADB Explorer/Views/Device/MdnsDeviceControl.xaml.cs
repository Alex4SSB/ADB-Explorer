using ADB_Explorer.Models;
namespace ADB_Explorer.Views;

/// <summary>
/// Interaction logic for MdnsDeviceControl.xaml
/// </summary>
public partial class MdnsDeviceControl : UserControl
{
    public MdnsDeviceControl()
    {
        InitializeComponent();
    }

    private void RestartAdbButton_Click(object sender, RoutedEventArgs e)
    {
        Data.MdnsService.Restart();
    }
}
