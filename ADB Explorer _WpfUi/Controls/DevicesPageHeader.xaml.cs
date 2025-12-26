using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;

namespace ADB_Explorer.Controls
{
    /// <summary>
    /// Interaction logic for DevicesPageHeader.xaml
    /// </summary>
    public partial class DevicesPageHeader : UserControl
    {
        public DevicesPageHeader()
        {
            Data.RuntimeSettings.PropertyChanged += RuntimeSettings_PropertyChanged;

            InitializeComponent();
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

        private void RefreshDevicesButton_Click(object sender, RoutedEventArgs e)
        {
            // TODO: Refresh devices
        }

        private void RestartAdbButton_Click(object sender, RoutedEventArgs e)
        {
            ADBService.KillAdbServer();
            Data.MdnsService.State = MDNS.MdnsState.Disabled;
            AdbHelper.UpdateMdns();
        }
    }
}
