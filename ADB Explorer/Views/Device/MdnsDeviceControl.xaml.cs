using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;

namespace ADB_Explorer.Views;

/// <summary>
/// Interaction logic for MdnsDeviceControl.xaml
/// </summary>
public partial class MdnsDeviceControl : UserControl
{
    public MdnsDeviceControl()
    {
        InitializeComponent();

        AdbHelper.CurrentAdbState.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(AdbHelper.CurrentAdbState.Status))
            {
                InitMdns();
            }
        };

        InitMdns();
    }

    private static void InitMdns()
    {
        if (AdbHelper.CurrentAdbState.Status is AdbHelper.AdbStatus.Valid)
        {
            AdbHelper.EnableMdns();
        }
    }

    private void RestartAdbButton_Click(object sender, RoutedEventArgs e)
    {
        Data.MdnsService.Restart();
    }
}
