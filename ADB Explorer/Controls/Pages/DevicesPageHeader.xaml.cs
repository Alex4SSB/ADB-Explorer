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

    private bool _isRefreshing;

    private async void RefreshDevicesButton_Click(object sender, RoutedEventArgs e)
    {
        // Ignore clicks while a refresh is already running - no point spawning concurrent scans.
        if (_isRefreshing)
            return;

        _isRefreshing = true;
        StartRefreshSpin();

        var startTick = Environment.TickCount64;

        try
        {
            // RefreshDevices makes several blocking adb calls (device list, per-device whoami/id, mDNS scan),
            // so run it off the UI thread - same as the polling loop does - to keep the app responsive.
            // A blocking adb call can throw; this is an async void handler, so an escaping exception would
            // crash the app and leave the button/spinner stuck - hence the try/finally.
            await Task.Run(() => DevicePollingService.RefreshDevices(CancellationToken.None));

            // Keep the spinner visible for a moment even on a fast refresh, so the click clearly registers.
            var elapsed = Environment.TickCount64 - startTick;
            if (elapsed < 500)
                await Task.Delay((int)(500 - elapsed));
        }
        catch
        {
            // A failed manual refresh shouldn't take the app down; the next poll will recover the device list.
        }
        finally
        {
            StopRefreshSpin();
            _isRefreshing = false;
        }
    }

    private void StartRefreshSpin()
    {
        var spin = new DoubleAnimation
        {
            From = 0,
            To = 360,
            Duration = TimeSpan.FromSeconds(1),
            RepeatBehavior = RepeatBehavior.Forever,
        };
        RefreshIconRotate.BeginAnimation(RotateTransform.AngleProperty, spin);
    }

    private void StopRefreshSpin()
    {
        RefreshIconRotate.BeginAnimation(RotateTransform.AngleProperty, null);
        RefreshIconRotate.Angle = 0;
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
