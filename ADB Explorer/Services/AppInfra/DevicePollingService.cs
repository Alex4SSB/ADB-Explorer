using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services.AppInfra;
using ADB_Explorer.ViewModels;
using ADB_Explorer.Views.Pages;
using Microsoft.Extensions.Hosting;

namespace ADB_Explorer.Services;

public class DevicePollingService : BackgroundService
{
    private static bool isDevicesPage = false;

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
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!Data.RuntimeSettings.IsPollingStopped
                    && AdbHelper.CurrentAdbState.Status is AdbHelper.AdbStatus.Valid)
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
        if (Data.Settings.PollDevices)
        {
            RefreshDevices(cancellationToken);
        }

        if (Data.Settings.PollBattery)
        {
            DeviceHelper.UpdateDevicesBatInfo(cancellationToken);
        }

        if (Data.FileActions.IsDriveViewVisible && Data.Settings.PollDrives)
        {
            FileActionLogic.RefreshDrives(true, cancellationToken);
        }

        return Task.CompletedTask;
    }

    public static void RefreshDevices(CancellationToken cancellationToken)
    {
        ListDevices(ADBService.GetDevices(cancellationToken));

        if (Data.Settings.EnableWsa)
            DeviceHelper.ConnectWsaDevice();

        if (!isDevicesPage)
            return;

        Data.DevicesObject.UpdateLogicalIp();

        if (Data.MdnsService?.State is MDNS.MdnsState.Running)
            DeviceHelper.ListServices(WiFiPairingService.GetServices(cancellationToken), cancellationToken);

        DeviceHelper.UpdateDevicesRootAccess();

        DeviceHelper.UpdateWsaPkgStatus();
    }

    private static void ListDevices(IEnumerable<DeviceSnapshot> snapshots)
    {
        if (snapshots is null)
            return;

        if (!Data.DevicesObject.DevicesChanged(snapshots))
            return;

        var deviceVMs = snapshots.Select(s => new LogicalDeviceViewModel(LogicalDevice.From(s)));

        DeviceHelper.DeviceListSetup(deviceVMs);

        if (!Data.Settings.AutoRoot)
            return;

        foreach (var item in Data.DevicesObject.LogicalDeviceViewModels.Where(device => device.Root is RootStatus.Unchecked))
        {
            item.EnableRoot(true);
        }
    }
}

