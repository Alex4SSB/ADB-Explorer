using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;
using System.Windows.Media.Animation;

namespace ADB_Explorer.Controls.Pages;

/// <summary>
/// Interaction logic for DevicesPageHeader.xaml
/// </summary>
public partial class DevicesPageHeader : UserControl
{
    private bool _devicesHandlersAttached;

    public DevicesPageHeader()
    {
        Thread.CurrentThread.CurrentCulture = Data.Settings.ActualFormatCulture;

        InitializeComponent();

        Loaded += (_, _) => AttachDevicesHandlers();
        Data.DevicesObjectCreated += (_, _) => AttachDevicesHandlers();
        Data.Settings.PropertyChanged += Settings_PropertyChanged;
    }

    private void AttachDevicesHandlers()
    {
        if (_devicesHandlersAttached || Data.DevicesObject is null)
            return;

        _devicesHandlersAttached = true;
        Data.DevicesObject.UIList.CollectionChanged += UIList_CollectionChanged;
        Data.DevicesObject.PropertyChanged += DevicesObject_PropertyChanged;
    }

    private void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppSettings.EnableMdns):
            case nameof(AppSettings.EnableEmulatorDiscovery):
                FilterDevices();

                break;
            default:
                break;
        }
    }

    private void RefreshDevicesButton_Click(object sender, RoutedEventArgs e)
    {
        Task.Run(() => DevicePollingService.RefreshDevices(CancellationToken.None));

        var clockwise = !Data.RuntimeSettings.IsRTL;
        var transform = new RotateTransform();
        RefreshDevicesIcon.RenderTransform = transform;
        RefreshDevicesIcon.RenderTransformOrigin = new Point(0.5, 0.5);
        transform.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(clockwise ? 0 : 360, clockwise ? 360 : 0, TimeSpan.FromMilliseconds(200)));
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
        App.SafeInvoke(() =>
        {
            Thread.CurrentThread.CurrentCulture = Data.Settings.ActualFormatCulture;

            DeviceHelper.FilterDevices(CollectionViewSource.GetDefaultView(LogicalDevicesList.ItemsSource));
            DeviceHelper.FilterDevices(CollectionViewSource.GetDefaultView(EmulatorDevicesList.ItemsSource));
            DeviceHelper.FilterDevices(CollectionViewSource.GetDefaultView(VirtualDevicesList.ItemsSource));
        });
    }
}
