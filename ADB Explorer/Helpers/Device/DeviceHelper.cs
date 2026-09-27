namespace ADB_Explorer.Helpers;

public static partial class DeviceHelper
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

    public static string ParseDeviceName(string model, string device)
    {
        var name = device;
        if (device.Equals(device, StringComparison.InvariantCultureIgnoreCase))
            name = model;

        return name.Replace('_', ' ');
    }

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
        var active = Data.ActiveDevice;
        active?.UpdateBattery(cancellationToken);

        if (DateTime.Now - Data.DevicesObject.LastUpdate <= AdbExplorerConst.BATTERY_UPDATE_INTERVAL && Data.CurrentPage.Value != typeof(DevicesPage))
            return;

        var items = Data.DevicesObject.LogicalDeviceViewModels.Where(device => device.ID != active?.ID).ToList();
        foreach (var item in items)
        {
            item.UpdateBattery(cancellationToken);
        }

        Data.DevicesObject.LastUpdate = DateTime.Now;
    }

    public static void UpdateDevicesRootAccess()
    {
        var devices = Data.DevicesObject.LogicalDeviceViewModels
            .Where(d => d.Status is DeviceStatus.Ok && d.Root is RootStatus.Unchecked)
            .ToList();
        foreach (var device in devices)
        {
            var identity = AdbService.GetShellIdentity(device.ID);
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

    /// <summary>Points tabs still holding a device's replaced (offline) view-model at its current one.</summary>
    private static void RebindTabDevices()
    {
        if (App.Services.GetService<ExplorerTabsViewModel>() is not { } tabs)
            return;

        foreach (var instance in tabs.AllInstances)
        {
            if (instance.Device is not { } stale)
                continue;

            var current = Data.DevicesObject.LogicalDeviceViewModels.FirstOrDefault(d => d.ID == stale.ID);
            if (current is not null && !ReferenceEquals(current, stale))
                instance.Device = current;
        }
    }

    /// <summary>Reopens the active tab's device once it is back online (e.g. after a root toggle), at the
    /// location the tab was left on, even when AutoOpen is off. Does nothing while the tab shows a page.</summary>
    private static bool TryResumeActiveTabDevice()
    {
        // A tab showing a page (Settings...) isn't browsing the device, so it has nothing to resume.
        if (Data.ActiveExplorerInstance is not { Device: { } tabDevice, IsShowingPage: false } instance)
            return false;

        var device = Data.DevicesObject.LogicalDeviceViewModels
            .FirstOrDefault(d => d.ID == tabDevice.ID && d.Status is DeviceStatus.Ok);

        if (device is null)
            return false;

        Data.RuntimeSettings.PendingLocationAfterDeviceOpen = instance.History.LastExplorerLocation;
        Devices.SetOpenDevice(device);

        return true;
    }

    public static void DeviceListSetup(string selectedAddress = "")
    {
        Task.Run(() => AdbService.GetDevices(CancellationToken.None)).ContinueWith((t) 
            => App.SafeInvoke(() => DeviceListSetup(t.Result.Select(s => new LogicalDeviceViewModel(LogicalDevice.From(s))), selectedAddress)));
    }

    public static void DeviceListSetup(IEnumerable<LogicalDeviceViewModel> devices, string selectedAddress = "")
    {
        devices = ReconnectFileOpDevice(devices);
        Data.DevicesObject.UpdateDevices(devices);
        RebindTabDevices();

        if (Data.DevicesObject.Current is null || Data.DevicesObject.Current.IsOpen && Data.DevicesObject.Current.Status is not DeviceStatus.Ok)
        {
            Data.DeviceCts.Cancel();
            Data.DeviceCts.Dispose();
            Data.DeviceCts = new();

            ThumbnailService.StopLoading();
            ApkIconService.CancelPending();
            DriveHelper.ClearDrives();
            Devices.SetOpenDevice(null);
        }

        if (Data.DevicesObject.DevicesAvailable(true))
            return;

        Devices.SetOpenDevice(null);

        App.SafeInvoke(Data.CopyPaste.GetClipboardPasteItems);

        FileActionLogic.ClearExplorer();
        Data.FileActions.IsExplorerVisible = false;

        DriveHelper.ClearDrives();

        if (string.IsNullOrEmpty(selectedAddress))
        {
            if (TryResumeActiveTabDevice())
                return;

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

            while (device.Status is not DeviceStatus.Ok)
            {
                if (DateTime.Now - startTime > TimeSpan.FromSeconds(6))
                    return false;

                Thread.Sleep(500);
            }
            return true;
        }).ContinueWith(t => App.SafeInvoke(() =>
        {
            if (!t.Result)
                return;

            OpenDevice(device);
        }));
    }

    public static async void InitDevice(ExplorerInstance instance)
    {
        if (instance.Device is not { } device)
            return;

        device.EnsureDefaultDrives();

        // Without a dispatcher (in tests) no drives get made, and an async void throw would end the process.
        if (device.Drives.FirstOrDefault(d => d.Type is AbstractDrive.DriveType.Internal)?.Drive is not LogicalDrive internalDrive)
            return;

        // Run both ADB calls concurrently on background threads instead of blocking the UI thread.
        // Props (getprop) is needed by CombineDisplayNames (BrandName) and SetAndroidVersion.
        // AdbFeatures is needed for sync/list size capability checks.
        // GetInternalStorage (readlink) is independent and updates the internal drive path.
        var propsTask = Task.Run(() => device.Props);
        var featuresTask = Task.Run(() => device.AdbFeatures);
        var shellTask = Task.Run(() => device.GetOrLoadShellIdentity());

        internalDrive.UpdateInternalStorage(device.ID);

        // Start drive enumeration and battery update immediately — both are independent of Props
        FileActionLogic.RefreshDrives(true, CancellationToken.None, device);
        Task.Run(() => device.UpdateBattery(CancellationToken.None));

        // Suspend until Props is loaded without blocking the UI thread.
        // CombineDisplayNames and DriveViewNav must run after Props so that
        // BrandName and CurrentDisplayNames are populated before breadcrumbs render.
        await propsTask;
        await featuresTask;
        await shellTask;

        // Not Data.DevicesObject.Current - this tab's own device never changes underneath it, and
        // checking the app-wide current device would abandon this tab whenever another one opened
        // a different device while this await was in flight. The tab itself may have closed, though.
        if (App.Services.GetService<ExplorerTabsViewModel>() is not { } tabs || !tabs.OwnsInstance(instance))
            return;

        device.SetAndroidVersion();
        FolderHelper.CombineDisplayNames(device);

        var pending = Data.RuntimeSettings.PendingLocationAfterDeviceOpen;
        Data.RuntimeSettings.PendingLocationAfterDeviceOpen = null;

        var location = pending is not null
            && pending.IsNavigable
            && pending.Location is not Navigation.SpecialLocation.DriveView
                ? pending
                : new AdbLocation(Navigation.SpecialLocation.DriveView);

        // Calls straight into this tab's own explorer content (even if it's not the active one right now)
        // instead of the RuntimeSettings-signal path, which only ever reaches whichever tab is
        // active at the moment this async continuation happens to resume.
        if (App.Current.MainWindow is ADB_Explorer.Views.Windows.MainWindow mainWindow)
            mainWindow.GetOrCreateExplorerContent(instance).NavigateToLocation(location);

        if (Data.Settings.ThumbsMode is AppSettings.ThumbnailMode.OnConnect)
            Task.Run(() => ThumbnailService.ForceLoad(device));

        Data.CopyPaste.GetClipboardPasteItems();
        Data.RequestExplorer(ExplorerRequest.FilterDrives);

        instance.FileList.Actions.PushPackageEnabled = Data.Settings.EnableApk && device.Type is not DeviceType.Recovery;

        Data.FileOpQ.MoveOperationsToPast();
        FileActionLogic.UpdateFileActions();
    }
}
