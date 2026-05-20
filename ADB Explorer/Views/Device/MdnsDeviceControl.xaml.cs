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

        Data.RuntimeSettings.PropertyChanged += RuntimeSettings_PropertyChanged;

        InitMdns();
    }

    private static void InitMdns()
    {
        if (Data.RuntimeSettings.AdbVersion is not null && Data.RuntimeSettings.AdbVersion.Major > 0)
        {
            AdbHelper.EnableMdns();
        }
    }

    private static void RuntimeSettings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppRuntimeSettings.AdbVersion))
            InitMdns();
    }

    private void RestartAdbButton_Click(object sender, RoutedEventArgs e)
    {
        Data.MdnsService.Restart();
    }
}
