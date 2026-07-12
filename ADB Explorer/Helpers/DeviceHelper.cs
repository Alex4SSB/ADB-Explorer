using ADB_Explorer.Models;
using ADB_Explorer.Services;
using ADB_Explorer.Services.AppInfra;
using ADB_Explorer.ViewModels;
using ADB_Explorer.Views.Pages;
using Windows.Management.Deployment;

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
        if (Data.DevicesObject.Current.MmcProp is string mmcId)
        {
            return drives.FirstOrDefault(d => d.ID == mmcId);
        }
        // If OTG exists, but no MMC ID - there is no MMC
        else if (Data.DevicesObject.Current.OtgProp is not null)
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

    public static DriveSnapshot? GetMmcDrive(IEnumerable<DriveSnapshot> snapshots, string deviceID)
    {
        if (snapshots is null)
            return null;

        if (Data.DevicesObject.Current.MmcProp is string mmcId)
            return snapshots.Where(d => d.ID == mmcId).Select(d => (DriveSnapshot?)d).FirstOrDefault();
        else if (Data.DevicesObject.Current.OtgProp is not null)
            return null;

        var externalDrives = snapshots.Where(d => d.Type is AbstractDrive.DriveType.Unknown).ToList();

        switch (externalDrives.Count)
        {
            case > 1:
                var mmc = ADBService.GetMmcId(deviceID);
                return snapshots.Where(d => d.ID == mmc).Select(d => (DriveSnapshot?)d).FirstOrDefault();
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
        if (device.Equals(device, StringComparison.InvariantCultureIgnoreCase))
            name = model;

        return name.Replace('_', ' ');
    }

    public static void BrowseDeviceAction(LogicalDeviceViewModel device)
    {
        Data.DevicesObject.DeviceToOpen = device;
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
                                           : DialogService.DialogIcon.Critical,
                                  error: res == 0 ? null : DialogError.SideloadFailed);
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
                DialogService.ShowMessage(ex.Message,
                                          Strings.Resources.S_DISCONN_FAILED_TITLE,
                                          DialogService.DialogIcon.Critical,
                                          copyToClipboard: true,
                                          error: DialogError.DisconnectFailed);
                return;
            }
        }
        else if (device.Type is DeviceType.Remote)
        {
            try
            {
                ADBService.DisconnectNetworkDevice(device.ID, CancellationToken.None);
            }
            catch (Exception ex)
            {
                DialogService.ShowMessage(ex.Message,
                                          Strings.Resources.S_DISCONN_FAILED_TITLE,
                                          DialogService.DialogIcon.Critical,
                                          copyToClipboard: true,
                                          error: DialogError.DisconnectFailed);
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
                || !Data.DevicesObject.IsManualPairingInProgress
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
            App.SafeInvoke(() => DialogService.ShowMessage(Strings.Resources.S_ROOT_FORBID,
                                                           Strings.Resources.S_ROOT_FORBID_TITLE,
                                                           DialogService.DialogIcon.Critical,
                                                           copyToClipboard: true,
                                                           error: DialogError.RootForbidden));
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
        () => Data.DevicesObject.DeviceToConnect = device);

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

    public static DeviceAction LaunchEmulator(EmulatorPackageDeviceViewModel device) => new(
        () => device.Status is DeviceStatus.Ok,
        () =>
        {
            try
            {
                device.SetLastLaunch();
                device.SetStatus(DeviceStatus.Unauthorized);
                EmulatorHelper.LaunchAvd(device.AvdName);
            }
            catch (Exception ex)
            {
                device.SetStatus(DeviceStatus.Ok);
                DialogService.ShowMessage(ex.Message,
                                          Strings.Resources.S_EMULATOR_DIALOG_TITLE,
                                          DialogService.DialogIcon.Critical,
                                          copyToClipboard: true,
                                          error: DialogError.EmulatorLaunchFailed);
            }
        });

    public static bool EvaluateDevicePredicate(DeviceViewModel device, Devices devicesObject)
    {
        if (devicesObject is null)
            return false;
        // The mDNS device cannot hide itself when in a listview
        if (device is MdnsDeviceViewModel)
            return Data.Settings.EnableMdns;

        // current device cannot be hidden
        if (device is LogicalDeviceViewModel { IsOpen: true })
            return true;

        if (device is LogicalDeviceViewModel logDev && device.Type is DeviceType.Service)
        {
            if (device.Status is DeviceStatus.Offline)
            {
                // if a logical service is offline, and we have one of its services - hide the logical service
                return devicesObject.ServiceDeviceViewModels.All(s => s.IpAddress != device.IpAddress);
            }

            // if a USB device with the same serial is online - hide the mDNS service
            var res = !devicesObject.LogicalDeviceViewModels.Any(l => l.Type is DeviceType.Local
                                                    && l.Status is DeviceStatus.Ok
                                                    && logDev.SerialNumber == l.SerialNumber);

            if (res)
                logDev.UseIdForName = false;

            return res;
        }

        if (device is LogicalDeviceViewModel remote && device.Type is DeviceType.Remote)
        {
            // hide WiFi when the same device is connected over USB or mDNS
            return !devicesObject.LogicalDeviceViewModels.Any(other => 
                other.Status is DeviceStatus.Ok
                && (remote.SerialNumber == other.SerialNumber || remote.IpAddress == other.IpAddress)
                && (other.Type is DeviceType.Local or DeviceType.Service)
            );
        }

        if (device is HistoryDeviceViewModel hist)
        {
            // if there's any device with the IP of a history device - hide the history device
            return Data.Settings.SaveDevices && !devicesObject.LogicalDeviceViewModels.Any(logical => logical.IpAddress == hist.IpAddress || logical.IpAddress == hist.HostName)
                    && !devicesObject.ServiceDeviceViewModels.Any(service => service.IpAddress == hist.IpAddress || service.IpAddress == hist.HostName);
        }

        if (device is ServiceDeviceViewModel service)
        {
            // connect services are always hidden
            if (service.ConnectionKind is ServiceConnectionKind.Connect)
                return false;

            // if there's any online logical device with the IP of a pairing service - hide the pairing service
            if (devicesObject.LogicalDeviceViewModels.Any(logical => logical.Status is not DeviceStatus.Offline && logical.IpAddress == service.IpAddress))
                return false;

            // if there's any QR service with the IP of a code pairing service - hide the code pairing service
            if (service.MdnsType is ServiceDevice.PairingMode.PairingCode
                && devicesObject.ServiceDeviceViewModels.Any(qr => qr.MdnsType is ServiceDevice.PairingMode.QrCode
                                                          && qr.IpAddress == service.IpAddress))
                return false;
        }

        if (device is WsaPkgDeviceViewModel wsaPkg)
        {
            // if WSA is not installed - hide it
            if (wsaPkg.Status is DeviceStatus.Offline)
                return false;

            // if an online logical WSA device exists, the WSA package is hidden
            if (devicesObject.LogicalDeviceViewModels.Any(logical => logical.Type is DeviceType.WSA && logical.Status is not DeviceStatus.Offline))
                return false;
        }

        if (device is EmulatorPackageDeviceViewModel emuPkg)
        {
            if (!Data.Settings.EnableEmulatorDiscovery)
                return false;

            if (emuPkg.Status is DeviceStatus.Offline)
                return false;

            if (devicesObject.LogicalDeviceViewModels.Any(logical =>
                    logical.Type is DeviceType.Emulator
                    && logical.Status is not DeviceStatus.Offline
                    && EmulatorMatchesPackage(logical, emuPkg)))
                return false;
        }

        // if there's an offline WSA device - hide it
        if (device is LogicalDeviceViewModel { Type: DeviceType.WSA, Status: DeviceStatus.Offline })
            return false;

        if (device is LogicalDeviceViewModel logicalDev && logicalDev.Type is not DeviceType.Emulator)
        {
            // if there are multiple logical devices of the same model, display their ID instead
            logicalDev.UseIdForName = devicesObject.LogicalDeviceViewModels.Count(dev => dev.Device.Name.Equals(logicalDev.Device.Name) && dev.IpAddress != logicalDev.IpAddress) > 1;
        }

        return true;
    }

    public static readonly Predicate<DeviceViewModel> DevicePredicate = device => EvaluateDevicePredicate(device, Data.DevicesObject);

    public static readonly Predicate<object> DevicesFilter = d => EvaluateDevicePredicate((DeviceViewModel)d, Data.DevicesObject);

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

    public static void UpdateDevicesBatInfo(CancellationToken cancellationToken)
    {
        Data.DevicesObject.Current?.UpdateBattery(cancellationToken);

        if (DateTime.Now - Data.DevicesObject.LastUpdate <= AdbExplorerConst.BATTERY_UPDATE_INTERVAL && Data.CurrentPage.Value != typeof(DevicesPage))
            return;

        var items = Data.DevicesObject.LogicalDeviceViewModels.Where(device => !device.IsOpen).ToList();
        foreach (var item in items)
        {
            item.UpdateBattery(cancellationToken);
        }

        Data.DevicesObject.LastUpdate = DateTime.Now;
    }

    public static async void ListServices(IEnumerable<ServiceSnapshot> snapshots, CancellationToken cancellationToken)
    {
        if (snapshots is null)
            return;

        if (!Data.DevicesObject.ServicesChanged(snapshots))
            return;

        var viewModels = snapshots.Select(s => new ServiceDeviceViewModel(ServiceDevice.From(s)));

        Data.DevicesObject.UpdateServices(viewModels);

        var qrClass = Data.MdnsService?.QrClass;
        if (qrClass is null)
            return;

        var qrServices = Data.DevicesObject.ServiceDeviceViewModels.Where(service =>
            service.MdnsType == ServiceDevice.PairingMode.QrCode
            && service.ID == qrClass.ServiceName);

        if (qrServices.Any())
        {
            var qrService = qrServices.First();
            if (!qrService.IsPairingInProgress)
                await PairService(qrService, cancellationToken);
        }
    }

    public static async Task<bool> PairService(ServiceDeviceViewModel service, CancellationToken cancellationToken)
    {
        var code = service.MdnsType == ServiceDevice.PairingMode.QrCode
            ? Data.MdnsService?.QrClass?.Password
            : service.PairingCode;

        if (string.IsNullOrEmpty(code) || service.IsPairingInProgress)
            return false;

        App.SafeInvoke(service.BeginPairing);

        var success = false;
        string error = null;

        try
        {
            await Task.Run(() => ADBService.PairNetworkDevice(service.ID, code, cancellationToken), cancellationToken);
            success = true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }
        finally
        {
            App.SafeInvoke(() => service.EndPairing(success, error));
        }

        return success;
    }

    public static void UpdateDevicesRootAccess()
    {
        var devices = Data.DevicesObject.LogicalDeviceViewModels.Where(d => d.Root is RootStatus.Unchecked).ToList();
        foreach (var device in devices)
        {
            var identity = ADBService.GetShellIdentity(device.ID);
            bool root = identity?.IsRoot ?? false;
            bool rootDisabled = Data.DevicesObject.RootDevices.Contains(device.ID);
            App.SafeInvoke(() =>
            {
                device.SetShellIdentity(identity);
                device.SetRootStatus(root ? RootStatus.Enabled
                    : rootDisabled ? RootStatus.Disabled
                        : RootStatus.Unchecked);
            });
        }
    }

    public static async void PairNewDevice()
    {
        var dev = Data.DevicesObject.DeviceToConnect;
        if (dev is null)
            return;

        await Task.Run(() =>
        {
            try
            {
                ADBService.PairNetworkDevice(dev.PairingAddress, dev.PairingCode, CancellationToken.None);
                return true;
            }
            catch (Exception ex)
            {
                App.SafeInvoke(() => DialogService.ShowMessage(ex.Message,
                                                               Strings.Resources.S_PAIR_ERR_TITLE,
                                                               DialogService.DialogIcon.Critical,
                                                               copyToClipboard: true,
                                                               error: DialogError.PairingFailed));
                return false;
            }
        }).ContinueWith(t =>
        {
            if (t.IsCanceled)
                return;

            App.SafeInvoke(() =>
            {
                if (t.Result)
                    ConnectNewDevice();

                Data.DevicesObject.DeviceToConnect = null;
                Data.DevicesObject.IsManualPairingInProgress = false;
            });
        });
    }

    public static async void ConnectNewDevice()
    {
        var dev = Data.DevicesObject.DeviceToConnect;
        if (dev is null)
            return;

        await Task.Run(() =>
        {
            try
            {
                ADBService.ConnectNetworkDevice(dev.ConnectAddress, CancellationToken.None);
                return true;
            }
            catch (Exception ex)
            {
                if (AdbExplorerConst.LOOPBACK_ADDRESSES.Contains(dev.IpAddress))
                    return true;

                if (ex.Message.Contains("failed to connect to " + dev.ConnectAddress)
                    && !dev.IsPairingEnabled)
                {
                    Data.DevicesObject.CurrentNewDevice.EnablePairing();
                }
                else
                    App.SafeInvoke(() => DialogService.ShowMessage(ex.Message,
                                                                   Strings.Resources.S_FAILED_CONN_TITLE,
                                                                   DialogService.DialogIcon.Critical,
                                                                   copyToClipboard: true,
                                                                   error: DialogError.ConnectionFailed));

                return false;
            }
        }).ContinueWith(t =>
        {
            if (t.IsCanceled)
                return;

            App.SafeInvoke(() =>
            {
                if (t.Result)
                {
                    string newDeviceAddress = "";
                    var newDevice = Data.DevicesObject.DeviceToConnect ?? Data.DevicesObject.CurrentNewDevice;

                    if (newDevice.Type is DeviceType.New && !AdbExplorerConst.LOOPBACK_ADDRESSES.Contains(newDevice.IpAddress))
                    {
                        if (Data.Settings.SaveDevices)
                            Data.DevicesObject.AddHistoryDevice(HistoryDeviceViewModel.FromNewDevice(dev));

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

                    DeviceListSetup(newDeviceAddress);
                }

                Data.DevicesObject.DeviceToConnect = null;
                Data.DevicesObject.IsManualPairingInProgress = false;
            });
        });
    }

    public static IEnumerable<LogicalDeviceViewModel> ReconnectFileOpDevice(IEnumerable<LogicalDeviceViewModel> devices)
    {
        if (Data.FileOpQ is null)
            return [];

        var pastOps = Data.FileOpQ.Operations.Where(op => op.IsPastOp);

        // get the newly acquired devices with similar IDs to devices of the past file ops [the objects of] which also do not exist in the devices UI list
        var exceptDevices = devices.Where(d => pastOps.Any(op => op.Device.ID == d.ID && !Data.DevicesObject.UIList.Contains(op.Device)));

        // get the corresponding file op devices
        var fileOpDevices = pastOps.Select(op => op.Device).Where(d => exceptDevices.Any(e => e.ID == d.ID));

        return devices.Except(exceptDevices, new LogicalDeviceViewModelEqualityComparer()).AppendRange(fileOpDevices.Distinct());
    }

    public static void DeviceListSetup(string selectedAddress = "")
    {
        Task.Run(() => ADBService.GetDevices(CancellationToken.None)).ContinueWith((t) 
            => App.SafeInvoke(() => DeviceListSetup(t.Result.Select(s => new LogicalDeviceViewModel(LogicalDevice.From(s))), selectedAddress)));
    }

    public static void DeviceListSetup(IEnumerable<LogicalDeviceViewModel> devices, string selectedAddress = "")
    {
        devices = ReconnectFileOpDevice(devices);
        Data.DevicesObject.UpdateDevices(devices);

        if (Data.DevicesObject.Current is null || Data.DevicesObject.Current.IsOpen && Data.DevicesObject.Current.Status is not DeviceStatus.Ok)
        {
            Data.DeviceCts.Cancel();
            Data.DeviceCts.Dispose();
            Data.DeviceCts = new();

            ThumbnailService.StopLoading();
            DriveHelper.ClearDrives();
            Devices.SetOpenDevice(null);
        }

        if (Data.DevicesObject.DevicesAvailable(true))
            return;

        Devices.SetOpenDevice(null);

        App.SafeInvoke(Data.CopyPaste.GetClipboardPasteItems);

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
            var available = devices.ToList();
            if (available.Count == 1)
                device = available[0];
            else if (string.IsNullOrEmpty(Data.Settings.LastDevice))
                device = available.FirstOrDefault();
            else
                device = available.FirstOrDefault(d => d.Name == Data.Settings.LastDevice);
        }
        else
            device = Data.DevicesObject.Current;

        Task.Run(() =>
        {
            if (device is null)
                return false;

            // Also wait for the capability probe (ShellCommands.FindCommands populates DeviceCommands) to finish
            // before auto-opening. Opening while that probe is still running races the device setup and freezes
            // the UI - a manual "Browse" click works precisely because by then the probe is already done.
            while (device.Status is not DeviceStatus.Ok
                   || !ShellCommands.DeviceCommands.ContainsKey(device.ID))
            {
                if (DateTime.Now - startTime > TimeSpan.FromSeconds(6))
                    break;

                Thread.Sleep(500);
            }

            // Proceed once the device is usable, even if the capability probe was slow to register.
            return device.Status is DeviceStatus.Ok;
        }).ContinueWith(t => App.SafeInvoke(() =>
        {
            if (!t.Result)
                return;

            OpenDevice(device);
        }));
    }

    public static async void InitDevice()
    {
        var device = Data.DevicesObject.Current;

        // Pause the periodic polling while we set the device up. Otherwise the poll (device list, root re-check,
        // IP, drive counts) hammers the same device concurrently with this setup and all their UI updates land on
        // the UI thread together - that's the "everything fires at once" freeze on connect. Guaranteed to resume.
        DevicePollingService.IsDeviceSetupInProgress = true;
        try
        {
            device.EnsureDefaultDrives();
            var internalDrive = device.Drives.First(d => d.Type is AbstractDrive.DriveType.Internal).Drive as LogicalDrive;

            // Run both ADB calls concurrently on background threads instead of blocking the UI thread.
            // Props (getprop) is needed by CombineDisplayNames (BrandName) and SetAndroidVersion.
            // AdbFeatures is needed for sync/list size capability checks.
            // GetInternalStorage (readlink) is independent and updates the internal drive path.
            var propsTask = Task.Run(() => device.Props);
            var featuresTask = Task.Run(() => device.AdbFeatures);
            var shellTask = Task.Run(() => device.GetOrLoadShellIdentity());

            internalDrive.UpdateInternalStorage(device.ID);

            // Start drive enumeration and battery update immediately — both are independent of Props
            FileActionLogic.RefreshDrives(true, CancellationToken.None);
            Task.Run(() => device.UpdateBattery(CancellationToken.None));

            // Suspend until Props is loaded without blocking the UI thread.
            // CombineDisplayNames and DriveViewNav must run after Props so that
            // BrandName and CurrentDisplayNames are populated before breadcrumbs render.
            await propsTask;
            await featuresTask;
            await shellTask;

            if (Data.DevicesObject.Current != device)
                return;

            device.SetAndroidVersion();
            FolderHelper.CombineDisplayNames();
            Data.RuntimeSettings.DriveViewNav = true;
            NavHistory.Navigate(Navigation.SpecialLocation.DriveView);

            if (Data.Settings.ThumbsMode is AppSettings.ThumbnailMode.OnConnect)
                Task.Run(() => ThumbnailService.ForceLoad(device));

            Data.CopyPaste.GetClipboardPasteItems();
            Data.RuntimeSettings.FilterDrives = true;

            Data.FileActions.PushPackageEnabled = Data.Settings.EnableApk && device?.Type is not DeviceType.Recovery;

            Data.FileOpQ.MoveOperationsToPast();
            FileActionLogic.UpdateFileActions();

            // Setup is complete: now it's safe to auto-root. Enabling root restarts adbd, so doing it earlier
            // (on connect) collides with the setup above. Gate on the ACTUAL shell identity (already loaded above),
            // not just the Root flag: after the restart the device reconnects and re-runs this setup, and the VM
            // can come back "Unchecked" - but the shell is now root, so !HasRootShell stops a second fire/restart.
            if (Data.Settings.AutoRoot && device.Root is RootStatus.Unchecked && !device.HasRootShell)
                _ = Task.Run(() => device.EnableRoot(true));
        }
        finally
        {
            DevicePollingService.IsDeviceSetupInProgress = false;
        }
    }

    #region Test device injection (DEBUG only)

    private static int _testDeviceCounter = 0;

    private static LogicalDeviceViewModel MakeLogicalVM(string nameSuffix, string id, string ipAddress, DeviceType type, DeviceStatus status)
        => new(LogicalDevice.From(new DeviceSnapshot(id, nameSuffix, status, type, RootStatus.Unchecked, ipAddress, default))) { IsTestDevice = true };

    private static IEnumerable<LogicalDeviceViewModel> CurrentLogical()
        => Data.DevicesObject.LogicalDeviceViewModels.ToList();

    private static IEnumerable<ServiceDeviceViewModel> CurrentServices()
        => Data.DevicesObject.ServiceDeviceViewModels.ToList();

    public static void TestDevices_AddLocal()
    {
        int n = ++_testDeviceCounter;
        var vm = MakeLogicalVM($"USB Device {n}", $"TEST_USB_{n}", "", DeviceType.Local, DeviceStatus.Ok);
        Data.DevicesObject.UpdateDevices([.. CurrentLogical(), vm]);
    }

    public static void TestDevices_AddRemote()
    {
        int n = ++_testDeviceCounter;
        var ip = $"192.168.{n / 256}.{n % 256}";
        var vm = MakeLogicalVM($"Wi-Fi Device {n}", $"{ip}:5555", ip, DeviceType.Remote, DeviceStatus.Ok);
        Data.DevicesObject.UpdateDevices([.. CurrentLogical(), vm]);
    }

    public static void TestDevices_AddEmulator()
    {
        int n = ++_testDeviceCounter;
        var vm = MakeLogicalVM($"emulator-{5554 + n}", $"emulator-{5554 + n}", "", DeviceType.Emulator, DeviceStatus.Ok);
        Data.DevicesObject.UpdateDevices([.. CurrentLogical(), vm]);
    }

    public static void TestDevices_AddRecovery()
    {
        int n = ++_testDeviceCounter;
        var vm = MakeLogicalVM($"Recovery Device {n}", $"TEST_RECOVERY_{n}", "", DeviceType.Recovery, DeviceStatus.Ok);
        Data.DevicesObject.UpdateDevices([.. CurrentLogical(), vm]);
    }

    public static void TestDevices_AddUnauthorized()
    {
        int n = ++_testDeviceCounter;
        var vm = MakeLogicalVM($"Unauthorized Device {n}", $"TEST_UNAUTH_{n}", "", DeviceType.Local, DeviceStatus.Unauthorized);
        Data.DevicesObject.UpdateDevices([.. CurrentLogical(), vm]);
    }

    public static void TestDevices_AddOffline()
    {
        int n = ++_testDeviceCounter;
        var vm = MakeLogicalVM($"Offline Device {n}", $"TEST_OFFLINE_{n}", "", DeviceType.Local, DeviceStatus.Offline);
        Data.DevicesObject.UpdateDevices([.. CurrentLogical(), vm]);
    }

    public static void TestDevices_AddPairingService()
    {
        int n = ++_testDeviceCounter;
        var ip = $"10.0.{n / 256}.{n % 256}";
        var svc = new ServiceDeviceViewModel(new ServiceDevice($"test-code-{n}_adb-tls-pairing._tcp.", ip, $"{5555 + n}", ServiceConnectionKind.Pairing)
        {
            MdnsType = ServiceDevice.PairingMode.PairingCode
        }) { IsTestDevice = true };
        Data.DevicesObject.UpdateServices([.. CurrentServices(), svc]);
    }

    public static void TestDevices_AddQrService()
    {
        int n = ++_testDeviceCounter;
        var ip = $"10.1.{n / 256}.{n % 256}";
        var svc = new ServiceDeviceViewModel(new ServiceDevice($"ADB_WIFI_QR_{n}_adb-tls-pairing._tcp.", ip, $"{5555 + n}", ServiceConnectionKind.Pairing)
        {
            MdnsType = ServiceDevice.PairingMode.QrCode
        }) { IsTestDevice = true };
        Data.DevicesObject.UpdateServices([.. CurrentServices(), svc]);
    }

    public static void TestDevices_Clear()
    {
        _testDeviceCounter = 0;
        Data.DevicesObject.UpdateDevices([]);
        Data.DevicesObject.UpdateServices([]);
    }

    #endregion

    public static void ConnectDevice(NewDeviceViewModel device)
    {
        Data.DevicesObject.IsManualPairingInProgress = true;
        Data.DevicesObject.CurrentNewDevice = device;

        if (device.IsPairingEnabled)
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
                    Data.DeviceCts.Cancel();
                    Data.DeviceCts.Dispose();
                    Data.DeviceCts = new();

                    ThumbnailService.StopLoading();
                    DriveHelper.ClearDrives();
                    FileActionLogic.ClearExplorer();
                    NavHistory.Reset();
                    Data.FileActions.IsExplorerVisible = false;
                    Data.DirList = null;
                    Data.DevicesObject.DeviceToOpen = null;
                }

                Data.DevicesObject.UIList.Remove(device);
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
        Data.DeviceCts.Cancel();
        Data.DeviceCts.Dispose();
        Data.DeviceCts = new();

        Devices.SetOpenDevice(device);

        Data.CurrentPage.Value = typeof(ExplorerPage);
        Data.RuntimeSettings.InitLister = true;
        
        FileActionLogic.ClearExplorer();
        NavHistory.Reset();
        InitDevice();
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

    private static bool IsWsaInstalled()
    {
        try
        {
            return new PackageManager().FindPackagesForUser("")?
                .Any(pkg => pkg.DisplayName.Contains(AdbExplorerConst.WSA_PACKAGE_NAME))
                is true;
        }
        catch
        {
            return false;
        }
    }

    public static void UpdateWsaPkgStatus()
    {
        if (!Data.Settings.EnableWsa)
            return;

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
            App.SafeInvoke(() => wsa.SetStatus(newStatus));
        }
    }

    private static DateTime lastEmulatorAvdScan = DateTime.MinValue;
    private static string[] cachedAvdList = [];

    public static void UpdateEmulatorPackages()
    {
        if (!Data.Settings.EnableEmulatorDiscovery)
        {
            if (Data.DevicesObject.UIList.Any(d => d is EmulatorPackageDeviceViewModel && !d.IsTestDevice))
            {
                Data.DevicesObject.UIList.RemoveAll(d => d is EmulatorPackageDeviceViewModel && !d.IsTestDevice);
            }

            lastEmulatorAvdScan = DateTime.MinValue;
            cachedAvdList = [];
            return;
        }

        if (!EmulatorHelper.IsAvailable())
        {
            if (Data.DevicesObject.UIList.Any(d => d is EmulatorPackageDeviceViewModel && !d.IsTestDevice))
            {
                Data.DevicesObject.UIList.RemoveAll(d => d is EmulatorPackageDeviceViewModel && !d.IsTestDevice);
            }

            return;
        }

        if (lastEmulatorAvdScan == DateTime.MinValue
            || DateTime.Now - lastEmulatorAvdScan >= AdbExplorerConst.EMULATOR_AVD_SCAN_INTERVAL)
        {
            cachedAvdList = EmulatorHelper.ListAvds();
            lastEmulatorAvdScan = DateTime.Now;
        }

        var packages = cachedAvdList.Select(avd => new EmulatorPackageDeviceViewModel(new(avd))).ToList();
        Data.DevicesObject.UpdateEmulatorPackages(packages);
    }

    public static void UpdateEmulatorPackageStatus()
    {
        if (!Data.Settings.EnableEmulatorDiscovery)
            return;

        foreach (var emuPkg in Data.DevicesObject.UIList.OfType<EmulatorPackageDeviceViewModel>())
        {
            if (emuPkg.LastLaunch == DateTime.MaxValue
                || DateTime.Now - emuPkg.LastLaunch < AdbExplorerConst.EMULATOR_LAUNCH_DELAY)
                continue;

            if (Data.DevicesObject.LogicalDeviceViewModels.Any(logical =>
                    logical.Type is DeviceType.Emulator
                    && logical.Status is not DeviceStatus.Offline
                    && EmulatorMatchesPackage(logical, emuPkg)))
            {
                if (emuPkg.Status is DeviceStatus.Unauthorized)
                {
                    App.SafeInvoke(() =>
                    {
                        emuPkg.SetStatus(DeviceStatus.Ok);
                        emuPkg.SetLastLaunch(DateTime.MaxValue);
                    });
                }

                continue;
            }

            if (emuPkg.Status is DeviceStatus.Unauthorized
                && DateTime.Now - emuPkg.LastLaunch > AdbExplorerConst.EMULATOR_BOOT_TIMEOUT)
            {
                App.SafeInvoke(() =>
                {
                    emuPkg.SetStatus(DeviceStatus.Ok);
                    emuPkg.SetLastLaunch(DateTime.MaxValue);
                });
            }
        }
    }

    public static void UpdateLogicalEmulatorAvdNames()
    {
        foreach (var device in Data.DevicesObject.LogicalDeviceViewModels
            .Where(d => d.Type is DeviceType.Emulator && d.Status is not DeviceStatus.Offline && string.IsNullOrEmpty(d.AvdName))
            .ToList())
        {
            try
            {
                var name = device.GetAvdNameFromProps()
                    ?? EmulatorHelper.TryGetAvdNameFromEmuOrConsole(device.ID);
                if (!string.IsNullOrWhiteSpace(name))
                    App.SafeInvoke(() => device.SetAvdName(name));
            }
            catch
            {
                // Emulator may not be ready yet
            }
        }

        TryAssignAvdNamesFromRecentLaunches();
    }

    private static bool EmulatorMatchesPackage(LogicalDeviceViewModel logical, EmulatorPackageDeviceViewModel emuPkg)
    {
        if (string.Equals(logical.AvdName, emuPkg.AvdName, StringComparison.Ordinal))
            return true;

        return IsRecentSingleEmulatorLaunch(logical, emuPkg);
    }

    private static bool IsRecentSingleEmulatorLaunch(LogicalDeviceViewModel logical, EmulatorPackageDeviceViewModel emuPkg)
    {
        if (emuPkg.LastLaunch == DateTime.MinValue || emuPkg.LastLaunch == DateTime.MaxValue)
            return false;

        if (DateTime.Now - emuPkg.LastLaunch > AdbExplorerConst.EMULATOR_BOOT_TIMEOUT)
            return false;

        var onlineEmulators = Data.DevicesObject.LogicalDeviceViewModels
            .Where(d => d.Type is DeviceType.Emulator && d.Status is not DeviceStatus.Offline)
            .ToList();

        return onlineEmulators.Count == 1 && onlineEmulators[0].ID == logical.ID;
    }

    private static void TryAssignAvdNamesFromRecentLaunches()
    {
        var onlineEmulators = Data.DevicesObject.LogicalDeviceViewModels
            .Where(d => d.Type is DeviceType.Emulator && d.Status is not DeviceStatus.Offline && string.IsNullOrEmpty(d.AvdName))
            .ToList();

        if (onlineEmulators.Count != 1)
            return;

        var recentPackages = Data.DevicesObject.UIList.OfType<EmulatorPackageDeviceViewModel>()
            .Where(p => p.LastLaunch != DateTime.MinValue
                && p.LastLaunch != DateTime.MaxValue
                && DateTime.Now - p.LastLaunch < AdbExplorerConst.EMULATOR_BOOT_TIMEOUT)
            .ToList();

        if (recentPackages.Count != 1)
            return;

        App.SafeInvoke(() => onlineEmulators[0].SetAvdName(recentPackages[0].AvdName));
    }

    private static readonly HashSet<string> poweredOnEmulators = [];
    private static readonly HashSet<string> pendingEmulatorPowerOn = [];

    public static void HandleEmulatorPostPoll(IEnumerable<DeviceSnapshot> snapshots)
    {
        if (snapshots is null)
            return;

        var emulatorIds = snapshots.Where(s => s.Type is DeviceType.Emulator).Select(s => s.ID).ToHashSet();
        poweredOnEmulators.RemoveWhere(id => !emulatorIds.Contains(id));
        pendingEmulatorPowerOn.RemoveWhere(id => !emulatorIds.Contains(id));

        UpdateLogicalEmulatorAvdNames();

        foreach (var snapshot in snapshots.Where(s => s.Type is DeviceType.Emulator))
        {
            if (snapshot.Status is DeviceStatus.Offline)
            {
                if (pendingEmulatorPowerOn.Add(snapshot.ID))
                    Task.Run(() => EmulatorHelper.EnsurePoweredOn(snapshot.ID));

                continue;
            }

            pendingEmulatorPowerOn.Remove(snapshot.ID);

            if (!poweredOnEmulators.Add(snapshot.ID))
                continue;

            Task.Run(() => EmulatorHelper.EnsurePoweredOn(snapshot.ID));
        }
    }
}
