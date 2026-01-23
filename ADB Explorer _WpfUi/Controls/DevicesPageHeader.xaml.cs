using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;

namespace ADB_Explorer.Controls;

/// <summary>
/// Interaction logic for DevicesPageHeader.xaml
/// </summary>
public partial class DevicesPageHeader : UserControl
{
    public DevicesPageHeader()
    {
        Thread.CurrentThread.CurrentCulture =
        Thread.CurrentThread.CurrentUICulture = Data.Settings.UICulture;

        InitializeComponent();

        Data.DevicesObject.UIList.CollectionChanged += UIList_CollectionChanged;
        Data.DevicesObject.PropertyChanged += DevicesObject_PropertyChanged;
    }

    private void RefreshDevicesButton_Click(object sender, RoutedEventArgs e)
    {
        DevicePollingService.RefreshDevices();
    }

    private void UIList_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        FilterDevices();
    }

    private void DevicesObject_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModels.Devices.UIList))
            FilterDevices();
    }

    private void FilterDevices()
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            Thread.CurrentThread.CurrentCulture =
            Thread.CurrentThread.CurrentUICulture = Data.Settings.UICulture;

            DeviceHelper.FilterDevices(CollectionViewSource.GetDefaultView(DevicesList.ItemsSource));
        });
    }
}
