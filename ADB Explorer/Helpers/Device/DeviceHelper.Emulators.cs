using Windows.Management.Deployment;

namespace ADB_Explorer.Helpers;

public static partial class DeviceHelper
{
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

        var retCode = AdbService.ExecuteCommand("cmd.exe",
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

    private static DateTime _lastEmulatorAvdScan = DateTime.MinValue;

    private static string[] _cachedAvdList = [];

    public static void UpdateEmulatorPackages()
    {
        if (!Data.Settings.EnableEmulatorDiscovery)
        {
            if (Data.DevicesObject.UIList.Any(d => d is EmulatorPackageDeviceViewModel && !d.IsTestDevice))
            {
                Data.DevicesObject.UIList.RemoveAll(d => d is EmulatorPackageDeviceViewModel && !d.IsTestDevice);
            }

            _lastEmulatorAvdScan = DateTime.MinValue;
            _cachedAvdList = [];
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

        if (_lastEmulatorAvdScan == DateTime.MinValue
            || DateTime.Now - _lastEmulatorAvdScan >= AdbExplorerConst.EMULATOR_AVD_SCAN_INTERVAL)
        {
            _cachedAvdList = EmulatorHelper.ListAvds();
            _lastEmulatorAvdScan = DateTime.Now;
        }

        var packages = _cachedAvdList.Select(avd => new EmulatorPackageDeviceViewModel(new(avd))).ToList();
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

    private static readonly HashSet<string> _poweredOnEmulators = [];

    private static readonly HashSet<string> _pendingEmulatorPowerOn = [];

    public static void HandleEmulatorPostPoll(IEnumerable<DeviceSnapshot> snapshots)
    {
        if (snapshots is null)
            return;

        var emulatorIds = snapshots.Where(s => s.Type is DeviceType.Emulator).Select(s => s.ID).ToHashSet();
        _poweredOnEmulators.RemoveWhere(id => !emulatorIds.Contains(id));
        _pendingEmulatorPowerOn.RemoveWhere(id => !emulatorIds.Contains(id));

        UpdateLogicalEmulatorAvdNames();

        foreach (var snapshot in snapshots.Where(s => s.Type is DeviceType.Emulator))
        {
            if (snapshot.Status is DeviceStatus.Offline)
            {
                if (_pendingEmulatorPowerOn.Add(snapshot.ID))
                    Task.Run(() => EmulatorHelper.EnsurePoweredOn(snapshot.ID));

                continue;
            }

            _pendingEmulatorPowerOn.Remove(snapshot.ID);

            if (!_poweredOnEmulators.Add(snapshot.ID))
                continue;

            Task.Run(() => EmulatorHelper.EnsurePoweredOn(snapshot.ID));
        }

        RefreshEmulatorPackageVisibility();
    }

    private static bool[] _lastEmulatorPackageVisibility = [];

    /// <summary>A launch package hides once its logical emulator is online and named, which
    /// is a property change, not a UIList change - so the view must be refreshed explicitly.</summary>
    private static void RefreshEmulatorPackageVisibility()
    {
        App.SafeInvoke(() =>
        {
            if (Data.DevicesObject is not { } devices)
                return;

            var visibility = devices.UIList.OfType<EmulatorPackageDeviceViewModel>()
                .Select(pkg => EvaluateDevicePredicate(pkg, devices))
                .ToArray();

            if (visibility.SequenceEqual(_lastEmulatorPackageVisibility))
                return;

            _lastEmulatorPackageVisibility = visibility;
            App.Services.GetService<DevicesViewModel>()?.EmulatorDevicesView?.Refresh();
        });
    }
}
