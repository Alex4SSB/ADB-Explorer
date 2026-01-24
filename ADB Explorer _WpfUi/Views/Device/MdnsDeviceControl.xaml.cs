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
        Data.RuntimeSettings.PropertyChanged += RuntimeSettings_PropertyChanged;

        InitializeComponent();

        AdbHelper.UpdateMdns();
    }

    private void RuntimeSettings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppRuntimeSettings.RefreshQrImage):
                PairingQrImage.Source = Data.QrClass.Image;
                break;
            default:
                break;
        }
    }

    private void MdnsOffTextBlock_Loaded(object sender, RoutedEventArgs e) => TextHelper.BuildLocalizedInlines(sender, e);

    private void MdnsCheckBox_Click(object sender, RoutedEventArgs e) => AdbHelper.UpdateMdns();

    private void RestartAdbButton_Click(object sender, RoutedEventArgs e)
    {
        ADBService.KillAdbServer();
        Data.MdnsService.State = MDNS.MdnsState.Disabled;
        AdbHelper.UpdateMdns();
    }
}
