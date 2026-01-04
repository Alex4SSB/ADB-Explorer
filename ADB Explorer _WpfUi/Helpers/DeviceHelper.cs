using ADB_Explorer.Models;
using ADB_Explorer.Services;
using ADB_Explorer.Services.AppInfra;
using ADB_Explorer.ViewModels;
using Windows.Management.Deployment;
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

        if (status == "sideload")
            return DeviceType.Sideload;

        if (id.Contains("._adb-tls-"))
            return DeviceType.Service;
        if (id.Contains(':'))
        {
            return AdbExplorerConst.LOOPBACK_ADDRESSES.Contains(id.Split(':')[0])
                ? DeviceType.WSA
                : DeviceType.Remote;
        }

        return id.Contains("emulator")
            ? DeviceType.Emulator
            : DeviceType.Local;
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

        var externalDrives = drives.Where(d => d.Type is AbstractDrive.DriveType.Unknown);

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

    public static void BrowseDeviceAction(LogicalDeviceViewModel device)
    {
        Data.RuntimeSettings.DeviceToOpen = device;
    }

    public static void SideloadDeviceAction(LogicalDeviceViewModel device)
    {
        OpenFileDialog dialog = new()
        {
            Title = Strings.Resources.S_SIDELOAD_ROM_TITLE,
            Filter = $"{Strings.Resources.S_ROM_FILE}|*.zip",
            Multiselect = false,
        };

        if (dialog.ShowDialog() is not true)
            return;

        var res = ADBService.ExecuteDeviceAdbCommand(device.ID, "sideload", out string stdout, out string stderr, CancellationToken.None, ADBService.EscapeAdbString(dialog.FileName));
        DialogService.ShowMessage(string.Join('\n', stdout, stderr),
                                  Strings.Resources.S_REBOOT_SIDELOAD,
                                  res == 0 ? DialogService.DialogIcon.Informational
                                           : DialogService.DialogIcon.Critical);
    }

    private static async void RemoveDeviceAction(DeviceViewModel device)
    {
        var message = device.Type is DeviceType.Emulator
            ? Strings.Resources.S_KILL_EMULATOR
            : Strings.Resources.S_REM_DEVICE;

        var name = device switch
        {
            HistoryDeviceViewModel dev when string.IsNullOrEmpty(dev.DeviceName) => dev.IpAddress,
            HistoryDeviceViewModel dev => dev.DeviceName,
            LogicalDeviceViewModel dev => dev.Name,
            _ => throw new NotImplementedException(),
        };

        var title = device.Type is DeviceType.Emulator
            ? Strings.Resources.S_KILL_EMULATOR_TITLE
            : Strings.Resources.S_REM_DEVICE_TITLE;

        var dialogTask = await DialogService.ShowConfirmation(message, string.Format(title, name));
        if (dialogTask.Item1 is not Wpf.Ui.Controls.ContentDialogResult.Primary)
            return;

        if (device.Type is DeviceType.Emulator)
        {
            try
            {
                ADBService.KillEmulator(device.ID);
            }
            catch (Exception ex)
            {
                DialogService.ShowMessage(ex.Message, Strings.Resources.S_DISCONN_FAILED_TITLE, DialogService.DialogIcon.Critical, copyToClipboard: true);
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
                DialogService.ShowMessage(ex.Message, Strings.Resources.S_DISCONN_FAILED_TITLE, DialogService.DialogIcon.Critical, copyToClipboard: true);
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
                DeviceType.Remote => Strings.Resources.S_REM_DEV,
                DeviceType.Emulator => Strings.Resources.S_REM_EMU,
                DeviceType.History => Strings.Resources.S_REM_HIST_DEV,
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
            App.Current.Dispatcher.Invoke(() => DialogService.ShowMessage(Strings.Resources.S_ROOT_FORBID, Strings.Resources.S_ROOT_FORBID_TITLE, DialogService.DialogIcon.Critical, copyToClipboard: true));
        }
    }

    public static DeviceAction ConnectDeviceCommand(NewDeviceViewModel device) => new(
        () => {
            if (!device.IsConnectPortValid)
                return false;

            if (!device.IsIpAddressValid && !device.IsHostNameValid)
                return false;

            return !device.IsPairingEnabled
                   || (device.IsPairingCodeValid && device.IsPairingPortValid);
        },
        () => Data.RuntimeSettings.ConnectNewDevice = device);

    public static DeviceAction LaunchWsa(WsaPkgDeviceViewModel device) => new(
        () => device.Status is DeviceStatus.Ok,
        async () =>
        {
            if (Data.Settings.ShowLaunchWsaMessage)
            {
                var result = await DialogService.ShowConfirmation(Strings.Resources.S_WSA_LAUNCH,
                                                                  Strings.Resources.S_WSA_DIALOG_TITLE,
                                                                  primaryText: Strings.Resources.S_BUTTON_LAUNCH,
                                                                  checkBoxText: Strings.Resources.S_DONT_SHOW_AGAIN,
                                                                  icon: DialogService.DialogIcon.Exclamation,
                                                                  censorContent: false);

                Data.Settings.ShowLaunchWsaMessage = !result.Item2;

                if (result.Item1 is not Wpf.Ui.Controls.ContentDialogResult.Primary)
                    return;
            }

            device.SetLastLaunch();
            device.SetStatus(DeviceStatus.Unauthorized);
            Process.Start($"{AdbExplorerConst.WSA_PROCESS_NAME}.exe");
        });

    public static readonly Predicate<DeviceViewModel> DevicePredicate = device =>
    {
        // The mDNS device cannot hide itself when in a listview
        if (device is MdnsDeviceViewModel)
            return Data.Settings.EnableMdns;

        // current device cannot be hidden
        if (device is LogicalDeviceViewModel { IsOpen: true })
            return true;

        if (device is LogicalDeviceViewModel && device.Type is DeviceType.Service)
        {
            if (device.Status is DeviceStatus.Offline)
            {
                // if a logical service is offline, and we have one of its services - hide the logical service
                return Data.DevicesObject.ServiceDeviceViewModels.All(s => s.IpAddress != device.IpAddress);
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
                && (device.ID.Contains(usb.ID)
                    || usb.IpAddress == device.IpAddress));
        }

        if (device is HistoryDeviceViewModel hist)
        {
            // if there's any device with the IP of a history device - hide the history device
            return Data.Settings.SaveDevices && !Data.DevicesObject.LogicalDeviceViewModels.Any(logical => logical.IpAddress == hist.IpAddress || logical.IpAddress == hist.HostName)
                    && !Data.DevicesObject.ServiceDeviceViewModels.Any(service => service.IpAddress == hist.IpAddress || service.IpAddress == hist.HostName);
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

        if (device is WsaPkgDeviceViewModel wsaPkg)
        {
            // if WSA is not installed - hide it
            if (wsaPkg.Status is DeviceStatus.Offline)
                return false;

            // if an online logical WSA device exists, the WSA package is hidden
            if (Data.DevicesObject.LogicalDeviceViewModels.Any(logical => logical.Type is DeviceType.WSA && logical.Status is not DeviceStatus.Offline))
                return false;
        }

        // if there's an offline WSA device - hide it
        if (device is LogicalDeviceViewModel { Type: DeviceType.WSA, Status: DeviceStatus.Offline })
            return false;

        if (device is LogicalDeviceViewModel logicalDev && logicalDev.Type is not DeviceType.Emulator)
        {
            // if there are multiple logical devices of the same model, display their ID instead
            logicalDev.UseIdForName = Data.DevicesObject.LogicalDeviceViewModels.Count(dev => dev.Name.Equals(logicalDev.Name)) > 1;
        }

        return true;
    };

    public static readonly Predicate<object> DevicesFilter = d => DevicePredicate((DeviceViewModel)d);

    public static void FilterDevices(ICollectionView collectionView)
    {
        if (collectionView is null)
            return;

        if (collectionView.Filter is not null)
        {
            collectionView.Refresh();
            return;
        }

        collectionView.Filter = new(DevicesFilter);
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

    public static async void ListServices(IEnumerable<ServiceDevice> services)
    {
        if (services is null)
            return;

        var viewModels = services.Select(ServiceDeviceViewModel.New);

        if (!Data.DevicesObject.ServicesChanged(viewModels))
            return;

        Data.DevicesObject.UpdateServices(viewModels);

        var qrServices = Data.DevicesObject.ServiceDeviceViewModels.Where(service =>
            service.MdnsType == ServiceDevice.ServiceType.QrCode
            && service.ID == Data.QrClass.ServiceName);

        if (qrServices.Any())
        {
            await PairService(qrServices.First());
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
                App.Current.Dispatcher.Invoke(() => DialogService.ShowMessage(ex.Message, Strings.Resources.S_PAIR_ERR_TITLE, DialogService.DialogIcon.Critical, copyToClipboard: true));
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
        var devices = Data.DevicesObject.LogicalDeviceViewModels.Where(d => d.Root is RootStatus.Unchecked).ToList();
        foreach (var device in devices)
        {
            bool root = ADBService.WhoAmI(device.ID);
            bool rootDisabled = Data.DevicesObject.RootDevices.Contains(device.ID);
            App.Current?.Dispatcher?.Invoke(() =>
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
                App.Current.Dispatcher.Invoke(() => DialogService.ShowMessage(ex.Message, Strings.Resources.S_PAIR_ERR_TITLE, DialogService.DialogIcon.Critical, copyToClipboard: true));
                return false;
            }
        }).ContinueWith(t =>
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
                if (AdbExplorerConst.LOOPBACK_ADDRESSES.Contains(dev.IpAddress))
                    return true;

                if (ex.Message.Contains(Strings.Resources.S_FAILED_CONN + dev.ConnectAddress)
                    && !((NewDeviceViewModel)Data.RuntimeSettings.ConnectNewDevice).IsPairingEnabled)
                {
                    Data.DevicesObject.CurrentNewDevice.EnablePairing();
                }
                else
                    App.Current.Dispatcher.Invoke(() => DialogService.ShowMessage(ex.Message, Strings.Resources.S_FAILED_CONN_TITLE, DialogService.DialogIcon.Critical, copyToClipboard: true));

                return false;
            }
        }).ContinueWith(t =>
        {
            if (t.IsCanceled)
                return;

            App.Current.Dispatcher.Invoke(() =>
            {
                if (t.Result)
                {
                    string newDeviceAddress = "";
                    var newDevice = Data.RuntimeSettings.ConnectNewDevice is null ? Data.DevicesObject.CurrentNewDevice : Data.RuntimeSettings.ConnectNewDevice;

                    if (newDevice.Type is DeviceType.New && !AdbExplorerConst.LOOPBACK_ADDRESSES.Contains(newDevice.IpAddress))
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

    public static IEnumerable<LogicalDeviceViewModel> ReconnectFileOpDevice(IEnumerable<LogicalDeviceViewModel> devices)
    {
        var pastOps = Data.FileOpQ.Operations.Where(op => op.IsPastOp);

        // get the newly acquired devices with similar IDs to devices of the past file ops [the objects of] which also do not exist in the devices UI list
        var exceptDevices = devices.Where(d => pastOps.Any(op => op.Device.ID == d.ID && !Data.DevicesObject.UIList.Contains(op.Device.Device)));

        // get the corresponding file op devices
        var fileOpDevices = pastOps.Select(op => op.Device.Device).Where(d => exceptDevices.Any(e => e.ID == d.ID));

        return devices.Except(exceptDevices, new LogicalDeviceViewModelEqualityComparer()).AppendRange(fileOpDevices.Distinct());
    }

    public static void DeviceListSetup(string selectedAddress = "")
    {
        Task.Run(ADBService.GetDevices).ContinueWith((t) => App.Current.Dispatcher.Invoke(() => DeviceListSetup(t.Result.Select(l => new LogicalDeviceViewModel(l)), selectedAddress)));
    }

    public static void DeviceListSetup(IEnumerable<LogicalDeviceViewModel> devices, string selectedAddress = "")
    {
        devices = ReconnectFileOpDevice(devices);
        Data.DevicesObject.UpdateDevices(devices);
        Data.RuntimeSettings.FilterDevices = true;

        if (Data.DevicesObject.Current is null || Data.DevicesObject.Current.IsOpen && Data.DevicesObject.Current.Status is not DeviceStatus.Ok)
        {
            DriveHelper.ClearDrives();
            Data.DevicesObject.SetOpenDevice((LogicalDeviceViewModel)null);
        }

        if (Data.DevicesObject.DevicesAvailable(true))
            return;

        CollapseDevices();

        Data.DevicesObject.SetOpenDevice((LogicalDeviceViewModel)null);

        Data.CopyPaste.GetClipboardPasteItems();

        FileActionLogic.ClearExplorer();
        Data.FileActions.IsExplorerVisible = false;

        NavHistory.Reset();
        DriveHelper.ClearDrives();

        if (string.IsNullOrEmpty(selectedAddress))
        {
            if (!Data.Settings.AutoOpen)
                return;
        }
        else
        {
            if (!Data.DevicesObject.SetOpenDevice(selectedAddress))
                return;
        }

        if (!devices.Any() && Data.DevicesObject.Current is null)
            return;

        var startTime = DateTime.Now;
        LogicalDeviceViewModel device;

        if (Data.DevicesObject.Current is null)
        {
            if (string.IsNullOrEmpty(Data.Settings.LastDevice))
                device = devices.First();
            else
                device = devices.FirstOrDefault(d => d.Name == Data.Settings.LastDevice);
        }
        else
            device = Data.DevicesObject.Current;

        Task.Run(() =>
        {
            if (device is null)
                return false;

            while (device.Status is not DeviceStatus.Ok)
            {
                if (DateTime.Now - startTime > TimeSpan.FromSeconds(6))
                    return false;

                Thread.Sleep(500);
            }
            return true;
        }).ContinueWith(t => App.Current.Dispatcher.Invoke(() =>
        {
            if (!t.Result)
                return;

            Data.DevicesObject.SetOpenDevice(device);
            Data.CurrentADBDevice = new(Data.DevicesObject.Current);
            Data.RuntimeSettings.InitLister = true;
        }));
    }

    public static void InitDevice()
    {
        SetAndroidVersion();
        FileActionLogic.RefreshDrives(true);

        FolderHelper.CombineDisplayNames();
        Data.RuntimeSettings.DriveViewNav = true;
        NavHistory.Navigate(Navigation.SpecialLocation.DriveView);

        Data.CopyPaste.GetClipboardPasteItems();
        Data.RuntimeSettings.FilterDrives = true;

        Data.RuntimeSettings.CurrentDevice = Data.DevicesObject.Current;
        Data.FileActions.PushPackageEnabled = Data.Settings.EnableApk && Data.DevicesObject?.Current?.Type is not DeviceType.Recovery;

        Data.FileOpQ.MoveOperationsToPast();
        FileActionLogic.UpdateFileActions();
    }

    public static void TestDevices()
    {
        //ConnectTimer.IsEnabled = false;

        //DevicesObject.UpdateServices(new List<ServiceDevice>() { new PairingService("sdfsdfdsf_adb-tls-pairing._tcp.", "192.168.1.20", "5555") { MdnsType = ServiceDevice.ServiceType.PairingCode } });
        //DevicesObject.UpdateDevices(new List<LogicalDevice>() { LogicalDevice.New("Test", "test.ID", "device") });
    }

    public static void SetAndroidVersion()
    {
        var versionTask = Task.Run(Data.CurrentADBDevice.GetAndroidVersion);
        versionTask.ContinueWith(t =>
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
        Data.DevicesObject.CurrentNewDevice = (NewDeviceViewModel)device;

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
        Data.CurrentADBDevice = new(device);
        Data.DevicesObject.SetOpenDevice(device);
        Data.RuntimeSettings.InitLister = true;
        FileActionLogic.ClearExplorer();
        NavHistory.Reset();
        InitDevice();

        Data.RuntimeSettings.IsDevicesPaneOpen = false;
    }

    public static void ConnectWsaDevice()
    {
        if (Data.DevicesObject.UIList.OfType<WsaPkgDeviceViewModel>().Any(wsa => wsa.Status is not DeviceStatus.Unauthorized))
            return;

        if (Data.DevicesObject.LogicalDeviceViewModels.Any(dev => dev.Type is DeviceType.WSA && dev.Status is not DeviceStatus.Offline))
            return;

        var wsaPid = GetWsaPid();
        if (wsaPid is null)
            return;

        var wsaIp = Network.GetWsaIp();
        if (wsaIp is null)
            return;

        var retCode = ADBService.ExecuteCommand("cmd.exe",
                                                "/C",
                                                out string stdout,
                                                out _,
                                                Encoding.UTF8,
                                                CancellationToken.None, "\"netstat", "-nao", "|", "findstr", $"{wsaPid.Value}\"");

        if (retCode != 0)
            return;

        var match = AdbRegEx.RE_NETSTAT_TCP_SOCK().Match(stdout);
        if (match.Groups?.Count < 2)
            return;

        var netstatIp = match.Groups["IP"].Value;
        Data.DevicesObject.WsaPort = match.Groups["Port"].Value;
        if (!AdbExplorerConst.LOOPBACK_ADDRESSES.Contains(netstatIp))
            return;

        Data.DevicesObject.CurrentNewDevice = new(new())
        {
            IpAddress = AdbExplorerConst.WIN_LOOPBACK_ADDRESS,
            ConnectPort = Data.DevicesObject.WsaPort,
        };
        Data.DevicesObject.CurrentNewDevice.ConnectCommand.Execute();
    }

    public static int? GetWsaPid() =>
        Process.GetProcessesByName(AdbExplorerConst.WSA_PROCESS_NAME).FirstOrDefault()?.Id;

    private static bool IsWsaInstalled() =>
        new PackageManager().FindPackagesForUser("")?.Any(pkg => pkg.DisplayName.Contains(AdbExplorerConst.WSA_PACKAGE_NAME))
        is true;

    public static void UpdateWsaPkgStatus()
    {
        var wsa = Data.DevicesObject.UIList.OfType<WsaPkgDeviceViewModel>().FirstOrDefault();
        if (wsa is null)
            return;

        if (Data.DevicesObject.LogicalDeviceViewModels.Any(dev => dev.Type is DeviceType.WSA && dev.Status is not DeviceStatus.Offline))
            return;

        if (wsa.LastLaunch == DateTime.MaxValue || DateTime.Now - wsa.LastLaunch < AdbExplorerConst.WSA_LAUNCH_DELAY)
            return;

        DeviceStatus newStatus;
        var oldStatus = wsa.Status;

        if (oldStatus is DeviceStatus.Unauthorized && DateTime.Now - wsa.LastLaunch > AdbExplorerConst.WSA_CONNECT_TIMEOUT)
        {
            if (wsa.LastLaunch == DateTime.MinValue)
            {
                wsa.SetLastLaunch();
                return;
            }

            newStatus = DeviceStatus.Ok;
            wsa.SetLastLaunch(DateTime.MaxValue);
        }
        else
        {
            if (GetWsaPid() is not null)
                newStatus = DeviceStatus.Unauthorized;
            else if (IsWsaInstalled())
                newStatus = DeviceStatus.Ok;
            else
                newStatus = DeviceStatus.Offline;
        }

        if (newStatus != oldStatus)
        {
            App.Current.Dispatcher.Invoke(() => wsa.SetStatus(newStatus));
            Data.RuntimeSettings.FilterDevices = true;
        }
    }
}
