using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services.AppInfra;
using ADB_Explorer.ViewModels;
using Microsoft.Extensions.Hosting;

namespace ADB_Explorer.Services;

public class DevicePollingService : BackgroundService
{
    public DevicePollingService()
    {
        
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!Data.RuntimeSettings.IsPollingStopped)
                    await PollAsync(stoppingToken);

                await Task.Delay(AdbExplorerConst.CONNECT_TIMER_INTERVAL, stoppingToken);
            }
            catch (OperationCanceledException) { }
        }

    }

    private Task PollAsync(CancellationToken ct)
    {
        if (Data.Settings.PollDevices)
        {
            RefreshDevices();
        }

        if (Data.Settings.PollBattery)
        {
            DeviceHelper.UpdateDevicesBatInfo();
        }

        if (Data.FileActions.IsDriveViewVisible && Data.Settings.PollDrives)
        {
            FileActionLogic.RefreshDrives(true);
        }

        if (Data.RuntimeSettings.IsDevicesView)
        {
            DeviceHelper.UpdateDevicesRootAccess();

            DeviceHelper.UpdateWsaPkgStatus();
        }

        return Task.CompletedTask;
    }

    public static void RefreshDevices()
    {
        ListDevices(ADBService.GetDevices());

        DeviceHelper.ConnectWsaDevice();

        if (!Data.RuntimeSettings.IsDevicesView)
            return;

        Data.DevicesObject.UpdateLogicalIp();

        if (Data.MdnsService?.State is MDNS.MdnsState.Running)
            DeviceHelper.ListServices(WiFiPairingService.GetServices());
    }

    private static void ListDevices(IEnumerable<LogicalDevice> devices)
    {
        if (devices is null)
            return;

        var deviceVMs = devices.Select(d => new LogicalDeviceViewModel(d));

        if (!Data.DevicesObject.DevicesChanged(deviceVMs))
            return;

        DeviceHelper.DeviceListSetup(deviceVMs);

        if (!Data.Settings.AutoRoot)
            return;

        foreach (var item in Data.DevicesObject.LogicalDeviceViewModels.Where(device => device.Root is AbstractDevice.RootStatus.Unchecked))
        {
            item.EnableRoot(true);
        }
    }
}

