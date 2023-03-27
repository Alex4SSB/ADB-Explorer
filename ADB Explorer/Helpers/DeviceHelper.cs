using ADB_Explorer.Models;
using ADB_Explorer.Resources;
using ADB_Explorer.Services;
using ADB_Explorer.Services.AppInfra;
using ADB_Explorer.ViewModels;
using static ADB_Explorer.Models.AbstractDevice;

namespace ADB_Explorer.Helpers;

public static class DeviceHelper
{
    public static DeviceStatus GetStatus(string status) => status switch
    {
        "device" or "recovery" or "sideload" => DeviceStatus.Ok,
        "offline" => DeviceStatus.Offline,
        "unauthorized" or "authorizing" => DeviceStatus.Unauthorized,
        _ => throw new NotImplementedException(),
    };

    public static DeviceType GetType(string id, string status)
    {
        if (status == "recovery")
            return DeviceType.Recovery;
        else if (status == "sideload")
            return DeviceType.Sideload;
        else if (id.Contains("._adb-tls-"))
            return DeviceType.Service;
        else if (id.Contains('.'))
            return DeviceType.Remote;
        else if (id.Contains("emulator"))
            return DeviceType.Emulator;
        else
            return DeviceType.Local;
    }

    public static LogicalDrive GetMmcDrive(IEnumerable<LogicalDrive> drives, string deviceID)
    {
        if (drives is null)
            return null;

        // Try to find the MMC in the props
        if (Data.CurrentADBDevice.MmcProp is string mmcId)
        {
            return drives.FirstOrDefault(d => d.ID == mmcId);
        }
        // If OTG exists, but no MMC ID - there is no MMC
        else if (Data.CurrentADBDevice.OtgProp is not null)
            return null;

        var externalDrives = drives.Where(d => d.Type == AbstractDrive.DriveType.Unknown);

        switch (externalDrives.Count())
        {
            // MMC ID has to be acquired if more than one extension drive exists
            case > 1:
                var mmc = ADBService.GetMmcId(deviceID);
                return drives.FirstOrDefault(d => d.ID == mmc);

            // Only check whether MMC exists if there's only one drive
            case 1:
                return ADBService.MmcExists(deviceID) ? externalDrives.First() : null;
            default:
                return null;
        }
    }

    /// <summary>
    /// Sets type of all drives with unknown type as external. Changes the <see cref="Drive"/> object itself.
    /// </summary>
    /// <param name="drives">The collection of <see cref="Drive"/>s to change</param>
    public static void SetExternalDrives(IEnumerable<LogicalDrive> drives)
    {
        if (drives is null)
            return;

        foreach (var item in drives.Where(d => d.Type == AbstractDrive.DriveType.Unknown))
        {
            item.Type = AbstractDrive.DriveType.External;
        }
    }

    public static string ParseDeviceName(string model, string device)
    {
        var name = device;
        if (device == device.ToLower())
            name = model;

        return name.Replace('_', ' ');
    }

    public static void BrosweDeviceAction(LogicalDeviceViewModel device)
    {
        Data.CurrentADBDevice = new(device);
        Data.RuntimeSettings.DeviceToOpen = device;
    }

    private static async void RemoveDeviceAction(DeviceViewModel device)
    {
        var dialogTask = await DialogService.ShowConfirmation(Strings.S_REM_DEVICE(device), Strings.S_REM_DEVICE_TITLE(device));
        if (dialogTask.Item1 is not ContentDialogResult.Primary)
            return;

        if (device.Type is DeviceType.Emulator)
        {
            try
            {
                ADBService.KillEmulator(device.ID);
            }
            catch (Exception ex)
            {
                DialogService.ShowMessage(ex.Message, Strings.S_DISCONN_FAILED_TITLE, DialogService.DialogIcon.Critical);
                return;
            }
        }
        else if (device.Type is DeviceType.Remote)
        {
            try
            {
                ADBService.DisconnectNetworkDevice(device.ID);
            }
            catch (Exception ex)
            {
                DialogService.ShowMessage(ex.Message, Strings.S_DISCONN_FAILED_TITLE, DialogService.DialogIcon.Critical);
                return;
            }
        }
        else if (device.Type is DeviceType.History)
        { } // No additional action is required
        else
        {
            throw new NotImplementedException();
        }

        RemoveDevice(device);
    }

    public static DeviceAction RemoveDeviceCommand(DeviceViewModel device) => new(
            () => device.Type is DeviceType.History
                || !Data.RuntimeSettings.IsManualPairingInProgress
                    && device.Type is DeviceType.Remote or DeviceType.Emulator,
            () => RemoveDeviceAction(device),
            device.Type switch
            {
                DeviceType.Remote => Strings.S_REM_DEV,
                DeviceType.Emulator => Strings.S_REM_EMU,
                DeviceType.History => Strings.S_REM_HIST_DEV,
                _ => "",
            });

    public static DeviceAction ToggleRootDeviceCommand(LogicalDeviceViewModel device) => new(
        () => device.Root is not RootStatus.Forbidden
            && device.Status is DeviceStatus.Ok
            && device.Type is not DeviceType.Sideload and not DeviceType.Recovery,
        () => ToggleRootAction(device));

    private static async void ToggleRootAction(LogicalDeviceViewModel device)
    {
        bool rootEnabled = device.Root is RootStatus.Enabled;

        await Task.Run(() => device.EnableRoot(!rootEnabled));

        if (device.Root is RootStatus.Forbidden)
        {
            App.Current.Dispatcher.Invoke(() => DialogService.ShowMessage(Strings.S_ROOT_FORBID, Strings.S_ROOT_FORBID_TITLE, DialogService.DialogIcon.Critical));
        }
    }

    public static DeviceAction ConnectDeviceCommand(NewDeviceViewModel device) => new(
        () => {
            if (!device.IsConnectPortValid)
                return false;

            if ((!device.IsIpAddressValid && device.IsHostNameActive is false)
            || (!device.IsHostNameValid && device.IsHostNameActive is true))
                return false;

            if (device is not null and not HistoryDeviceViewModel
                && device.IsPairingEnabled
                && (!device.IsPairingCodeValid || !device.IsPairingPortValid))
                return false;

            return true;
        },
        () => Data.RuntimeSettings.ConnectNewDevice = device);

    public static void FilterDevices(ICollectionView collectionView)
    {
        if (collectionView is null)
            return;

        if (collectionView.Filter is not null)
        {
            collectionView.Refresh();
            return;
        }

        Predicate<object> predicate = d =>
        {
            var device = (DeviceViewModel)d;

            // current device cannot be hidden
            if (device is LogicalDeviceViewModel ui && ui.IsOpen)
                return true;

            if (device is LogicalDeviceViewModel && device.Type is DeviceType.Service)
            {
                if (device.Status is DeviceStatus.Offline)
                {
                    // if a logical service is offline, and we have one of its services - hide the logical service
                    return !Data.DevicesObject.ServiceDeviceViewModels.Any(s => s.IpAddress == device.IpAddress);
                }

                // if there's a logical service and a remote device with the same IP - hide the logical service
                return !Data.DevicesObject.LogicalDeviceViewModels.Any(l => l.IpAddress == device.IpAddress
                                                        && l.Type is DeviceType.Remote or DeviceType.Local
                                                        && l.Status is DeviceStatus.Ok);
            }

            if (device is LogicalDeviceViewModel && device.Type is DeviceType.Remote)
            {
                // if a remote device is also connected by USB and both are authorized - hide the remote device
                return !Data.DevicesObject.LogicalDeviceViewModels.Any(usb => usb.Type is DeviceType.Local
                    && usb.Status is DeviceStatus.Ok
                    && usb.IpAddress == device.IpAddress);
            }

            if (device is HistoryDeviceViewModel)
            {
                // if there's any device with the IP of a history device - hide the history device
                return Data.Settings.SaveDevices && !Data.DevicesObject.LogicalDeviceViewModels.Any(logical => logical.IpAddress == device.IpAddress)
                        && !Data.DevicesObject.ServiceDeviceViewModels.Any(service => service.IpAddress == device.IpAddress);
            }

            if (device is ServiceDeviceViewModel service)
            {
                // connect services are always hidden
                if (service is ConnectServiceViewModel)
                    return false;

                // if there's any online logical device with the IP of a pairing service - hide the pairing service
                if (Data.DevicesObject.LogicalDeviceViewModels.Any(logical => logical.Status is not DeviceStatus.Offline && logical.IpAddress == service.IpAddress))
                    return false;

                // if there's any QR service with the IP of a code pairing service - hide the code pairing service
                if (service.MdnsType is ServiceDevice.ServiceType.PairingCode
                    && Data.DevicesObject.ServiceDeviceViewModels.Any(qr => qr.MdnsType is ServiceDevice.ServiceType.QrCode
                                                              && qr.IpAddress == service.IpAddress))
                    return false;
            }

            return true;
        };

        collectionView.Filter = new(predicate);
        collectionView.SortDescriptions.Clear();
        collectionView.SortDescriptions.Add(new SortDescription(nameof(DeviceViewModel.Type), ListSortDirection.Ascending));
    }

    public static void UpdateDevicesBatInfo()
    {
        Data.DevicesObject.Current?.UpdateBattery();

        if (DateTime.Now - Data.DevicesObject.LastUpdate <= AdbExplorerConst.BATTERY_UPDATE_INTERVAL && !Data.RuntimeSettings.IsDevicesPaneOpen)
            return;

        var items = Data.DevicesObject.LogicalDeviceViewModels.Where(device => !device.IsOpen);
        foreach (var item in items)
        {
            item.UpdateBattery();
        }

        Data.DevicesObject.LastUpdate = DateTime.Now;
    }

    public static void ListServices(IEnumerable<ServiceDevice> services)
    {
        Data.RuntimeSettings.LastServerResponse = DateTime.Now;

        if (services is null)
            return;

        var viewModels = services.Select(s => ServiceDeviceViewModel.New(s));

        if (Data.DevicesObject.ServicesChanged(viewModels))
        {
            Data.DevicesObject.UpdateServices(viewModels);

            var qrServices = Data.DevicesObject.ServiceDeviceViewModels.Where(service =>
                service.MdnsType == ServiceDevice.ServiceType.QrCode
                && service.ID == Data.QrClass.ServiceName);

            if (qrServices.Any())
            {
                PairService(qrServices.First()).ContinueWith((t) => Data.RuntimeSettings.LastServerResponse = DateTime.Now);
            }
        }
    }

    public static async Task<bool> PairService(ServiceDeviceViewModel service)
    {
        var code = service.MdnsType == ServiceDevice.ServiceType.QrCode
            ? Data.QrClass.Password
            : service.PairingCode;

        return await Task.Run(() =>
        {
            try
            {
                ADBService.PairNetworkDevice(service.ID, code);
            }
            catch (Exception ex)
            {
                App.Current.Dispatcher.Invoke(() => DialogService.ShowMessage(ex.Message, Strings.S_PAIR_ERR_TITLE, DialogService.DialogIcon.Critical));
                return false;
            }

            return true;
        });
    }

    public static void CollapseDevices()
    {
        // To make sure value changes to true
        Data.RuntimeSettings.CollapseDevices = false;
        Data.RuntimeSettings.CollapseDevices = true;

        Data.RuntimeSettings.IsPathBoxFocused = false;
    }

    public static void UpdateDevicesRootAccess()
    {
        foreach (var device in Data.DevicesObject.LogicalDeviceViewModels.Where(d => d.Root is RootStatus.Unchecked))
        {
            bool root = ADBService.WhoAmI(device.ID);
            bool rootDisabled = Data.DevicesObject.RootDevices.Contains(device.ID);
            App.Current.Dispatcher.Invoke(() =>
            {
                return device.SetRootStatus(root ? RootStatus.Enabled
                    : rootDisabled ? RootStatus.Disabled
                        : RootStatus.Unchecked);
            });
        }
    }

    public static async void PairNewDevice()
    {
        var dev = (NewDeviceViewModel)Data.RuntimeSettings.ConnectNewDevice;
        await Task.Run(() =>
        {
            try
            {
                ADBService.PairNetworkDevice(dev.PairingAddress, dev.PairingCode);
                return true;
            }
            catch (Exception ex)
            {
                App.Current.Dispatcher.Invoke(() => DialogService.ShowMessage(ex.Message, Strings.S_PAIR_ERR_TITLE, DialogService.DialogIcon.Critical));
                return false;
            }
        }).ContinueWith((t) =>
        {
            if (t.IsCanceled)
                return;

            App.Current.Dispatcher.Invoke(() =>
            {
                if (t.Result)
                    ConnectNewDevice();

                Data.RuntimeSettings.ConnectNewDevice = null;
                Data.RuntimeSettings.IsManualPairingInProgress = false;
            });
        });
    }

    public static async void ConnectNewDevice()
    {
        var dev = (NewDeviceViewModel)Data.RuntimeSettings.ConnectNewDevice;
        await Task.Run(() =>
        {
            try
            {
                ADBService.ConnectNetworkDevice(dev.ConnectAddress);
                return true;
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains(Strings.S_FAILED_CONN + dev.ConnectAddress)
                    && dev.Type is DeviceType.New
                    && !((NewDeviceViewModel)Data.RuntimeSettings.ConnectNewDevice).IsPairingEnabled)
                {
                    Data.DevicesObject.NewDevice.EnablePairing();
                }
                else
                    App.Current.Dispatcher.Invoke(() => DialogService.ShowMessage(ex.Message, Strings.S_FAILED_CONN_TITLE, DialogService.DialogIcon.Critical));

                return false;
            }
        }).ContinueWith((t) =>
        {
            if (t.IsCanceled)
                return;

            App.Current.Dispatcher.Invoke(() =>
            {
                if (t.Result)
                {
                    string newDeviceAddress = "";
                    var newDevice = Data.RuntimeSettings.ConnectNewDevice is null ? Data.DevicesObject.NewDevice : Data.RuntimeSettings.ConnectNewDevice;

                    if (newDevice.Type is DeviceType.New)
                    {
                        if (Data.Settings.SaveDevices)
                            Data.DevicesObject.AddHistoryDevice(HistoryDeviceViewModel.New(dev));

                        newDeviceAddress = dev.ConnectAddress;
                        ((NewDeviceViewModel)newDevice).ClearDevice();
                    }
                    else if (newDevice.Type is DeviceType.History)
                    {
                        newDeviceAddress = ((HistoryDeviceViewModel)newDevice).ConnectAddress;

                        // In case user has changed the port of the history device
                        if (Data.Settings.SaveDevices)
                            Data.DevicesObject.StoreHistoryDevices();
                    }

                    CollapseDevices();
                    DeviceListSetup(newDeviceAddress);
                }

                Data.RuntimeSettings.ConnectNewDevice = null;
                Data.RuntimeSettings.IsManualPairingInProgress = false;
            });
        });
    }

    public static void DeviceListSetup(string selectedAddress = "")
    {
        Task.Run(() => ADBService.GetDevices()).ContinueWith((t) => App.Current.Dispatcher.Invoke(() => DeviceListSetup(t.Result.Select(l => new LogicalDeviceViewModel(l)), selectedAddress)));
    }

    public static void DeviceListSetup(IEnumerable<LogicalDeviceViewModel> devices, string selectedAddress = "")
    {
        var init = !Data.DevicesObject.UpdateDevices(devices);
        Data.RuntimeSettings.FilterDevices = true;

        if (Data.DevicesObject.Current is null || Data.DevicesObject.Current.IsOpen && Data.DevicesObject.Current.Status is not DeviceStatus.Ok)
        {
            DriveHelper.ClearDrives();
            Data.DevicesObject.SetOpenDevice((LogicalDeviceViewModel)null);
        }

        if (Data.DevicesObject.DevicesAvailable(true))
            return;

        DisconnectDevice();

        if (!Data.Settings.AutoOpen || !Data.DevicesObject.SetOpenDevice(selectedAddress))
            return;

        if (!Data.RuntimeSettings.IsDevicesViewEnabled)
            Data.RuntimeSettings.IsDevicesPaneOpen = false;

        Data.DevicesObject.SetOpenDevice(Data.DevicesObject.Current);
        Data.CurrentADBDevice = new(Data.DevicesObject.Current);
        Data.RuntimeSettings.InitLister = true;
        if (init)
            InitDevice();

        static void DisconnectDevice()
        {
            CollapseDevices();

            Data.DevicesObject.SetOpenDevice((LogicalDeviceViewModel)null);

            Data.CutItems.Clear();
            Data.FileActions.PasteState = FileClass.CutType.None;

            FileActionLogic.ClearExplorer();
            Data.FileActions.IsExplorerVisible = false;

            NavHistory.Reset();
            DriveHelper.ClearDrives();
        }
    }

    public static void InitDevice()
    {
        SetAndroidVersion();
        FileActionLogic.RefreshDrives(true);

        FolderHelper.CombineDisplayNames();
        Data.RuntimeSettings.DriveViewNav = true;
        NavHistory.Navigate(NavHistory.SpecialLocation.DriveView);

        FileHelper.ClearCutFiles();
        Data.RuntimeSettings.FilterDrives = true;

        Data.RuntimeSettings.CurrentBatteryContext = Data.DevicesObject.Current;
        Data.FileActions.PushPackageEnabled = Data.Settings.EnableApk && Data.DevicesObject?.Current?.Type is not DeviceType.Recovery;

        FileActionLogic.UpdateFileActions();

#if DEBUG
        FileOpHelper.TestCurrentOperation();
#endif

        AdbHelper.VerifyProgressRedirection();
    }

    public static void TestDevices()
    {
        //ConnectTimer.IsEnabled = false;

        //DevicesObject.UpdateServices(new List<ServiceDevice>() { new PairingService("sdfsdfdsf_adb-tls-pairing._tcp.", "192.168.1.20", "5555") { MdnsType = ServiceDevice.ServiceType.PairingCode } });
        //DevicesObject.UpdateDevices(new List<LogicalDevice>() { LogicalDevice.New("Test", "test.ID", "device") });
    }

    public static void SetAndroidVersion()
    {
        var versionTask = Task.Run(async () => await Data.CurrentADBDevice.GetAndroidVersion());
        versionTask.ContinueWith((t) =>
        {
            if (t.IsCanceled)
                return;

            App.Current.Dispatcher.Invoke(() =>
            {
                Data.DevicesObject.Current.SetAndroidVersion(t.Result);
            });
        });
    }

    public static void ConnectDevice(DeviceViewModel device)
    {
        Data.RuntimeSettings.IsManualPairingInProgress = true;

        if (device is NewDeviceViewModel newDevice && newDevice.IsPairingEnabled)
            PairNewDevice();
        else
            ConnectNewDevice();
    }

    public static void RemoveDevice(DeviceViewModel device)
    {
        switch (device)
        {
            case LogicalDeviceViewModel logical:
                if (logical.IsOpen)
                {
                    DriveHelper.ClearDrives();
                    FileActionLogic.ClearExplorer();
                    NavHistory.Reset();
                    Data.FileActions.IsExplorerVisible = false;
                    Data.CurrentADBDevice = null;
                    Data.DirList = null;
                    Data.RuntimeSettings.DeviceToOpen = null;
                }

                Data.DevicesObject.UIList.Remove(device);
                Data.RuntimeSettings.FilterDevices = true;
                DeviceListSetup();
                break;
            case HistoryDeviceViewModel hist:
                Data.DevicesObject.RemoveHistoryDevice(hist);
                break;
            default:
                throw new NotSupportedException();
        }
    }

    public static void OpenDevice(LogicalDeviceViewModel device)
    {
        Data.DevicesObject.SetOpenDevice(device);
        Data.RuntimeSettings.InitLister = true;
        FileActionLogic.ClearExplorer();
        NavHistory.Reset();
        InitDevice();

        Data.RuntimeSettings.IsDevicesPaneOpen = false;
    }
}
