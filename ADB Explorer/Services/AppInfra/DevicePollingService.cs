using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services.AppInfra;
using ADB_Explorer.ViewModels;
using ADB_Explorer.Views.Pages;
using Microsoft.Extensions.Hosting;

namespace ADB_Explorer.Services;

public class DevicePollingService : BackgroundService
{
    private static bool isDevicesPage = true;

    // Set while a device is being opened/set up (DeviceHelper.InitDevice); pauses polling so it doesn't
    // race the setup and pile concurrent UI updates onto the UI thread.
    private static volatile bool _deviceSetupInProgress;
    private static long _deviceSetupStartedTick;

    // Safety cap: InitDevice awaits blocking adb calls with no timeout, so if a device wedges its `finally`
    // may never clear the flag. Never pause polling for longer than this, or the device list would freeze.
    private const long DeviceSetupPauseCapMs = 10_000;

    public static bool IsDeviceSetupInProgress
    {
        get => _deviceSetupInProgress;
        set
        {
            _deviceSetupInProgress = value;
            if (value)
                _deviceSetupStartedTick = Environment.TickCount64;
        }
    }

    private static bool ShouldPauseForSetup =>
        _deviceSetupInProgress && Environment.TickCount64 - _deviceSetupStartedTick < DeviceSetupPauseCapMs;

    private static int pollingInterval => isDevicesPage ? 500 : 2000;

    public DevicePollingService()
    {
        Data.CurrentPage.PropertyChanged += (s, e) =>
        {
            isDevicesPage = Data.CurrentPage.Value == typeof(DevicesPage);
        };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested && !App.IsShuttingDown)
        {
            try
            {
                if (!Data.RuntimeSettings.IsPollingStopped
                    && !ShouldPauseForSetup
                    && AdbHelper.CurrentAdbState.Status is AdbHelper.AdbStatus.Valid
                    && Data.DevicesObject is not null)
                {
                    await PollAsync(stoppingToken);
                }

                await Task.Delay(pollingInterval, stoppingToken);
            }
            catch (OperationCanceledException) { }
        }
    }

    private static Task PollAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested || App.IsShuttingDown)
            return Task.CompletedTask;

        if (Data.Settings.PollDevices)
        {
            RefreshDevices(cancellationToken);
        }

        if (Data.Settings.PollBattery)
        {
            DeviceHelper.UpdateDevicesBatInfo(cancellationToken);
        }

        // Drive tile counts (storage, recycle, installers, packages) change rarely, but refreshing them runs
        // ~8 adb commands (df/find/pm + the su count fallback) plus a UI update. At the 2s poll rate that is a
        // constant churn/stutter while the drive view is open, so throttle it well below the base poll rate.
        if (Data.FileActions.IsDriveViewVisible && Data.Settings.PollDrives
            && Environment.TickCount64 - _lastDrivePollTick >= DrivePollIntervalMs)
        {
            _lastDrivePollTick = Environment.TickCount64;
            FileActionLogic.RefreshDrives(true, cancellationToken);
        }

        return Task.CompletedTask;
    }

    public static void RefreshDevices(CancellationToken cancellationToken)
    {
        if (Data.DevicesObject is null)
            return;

        var snapshots = ADBService.GetDevices(cancellationToken)?.ToList();
        if (snapshots is null)
            return;

        ListDevices(snapshots);
        DeviceHelper.HandleEmulatorPostPoll(snapshots);

        if (Data.Settings.EnableWsa)
            DeviceHelper.ConnectWsaDevice();

        if (!isDevicesPage)
            return;

        // The Devices page polls every 500ms, but the work below (IP lookup, mDNS scan, per-device root
        // re-check, WSA/emulator status) is expensive and marshals onto the UI thread synchronously - running
        // it twice a second is what causes the stutter. Throttle it; the device LIST above still updates every poll.
        if (Environment.TickCount64 - _lastHeavyDevicePollTick < HeavyDevicePollIntervalMs)
            return;
        _lastHeavyDevicePollTick = Environment.TickCount64;

        Data.DevicesObject.UpdateLogicalIp();

        if (Data.MdnsService?.State is MDNS.MdnsState.Running)
            DeviceHelper.ListServices(WiFiPairingService.GetServices(cancellationToken), cancellationToken);

        DeviceHelper.UpdateDevicesRootAccess();

        DeviceHelper.UpdateWsaPkgStatus();

        if (Data.Settings.EnableEmulatorDiscovery)
        {
            DeviceHelper.UpdateEmulatorPackages();
            DeviceHelper.UpdateEmulatorPackageStatus();
        }
    }

    private static long _lastHeavyDevicePollTick;
    private const long HeavyDevicePollIntervalMs = 2000;

    private static long _lastDrivePollTick;
    private const long DrivePollIntervalMs = 6000;

    private static void ListDevices(IEnumerable<DeviceSnapshot> snapshots)
    {
        if (snapshots is null)
            return;

        if (!Data.DevicesObject.DevicesChanged(snapshots))
            return;

        var deviceVMs = snapshots.Select(s => new LogicalDeviceViewModel(LogicalDevice.From(s)));

        DeviceHelper.DeviceListSetup(deviceVMs);

        // AutoRoot is NOT triggered here: enabling root restarts adbd, and doing that the instant a device
        // connects collides with the concurrent device setup. It now runs at the end of DeviceHelper.InitDevice,
        // once setup has completed.
    }
}

