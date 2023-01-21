using ADB_Explorer.Models;
using ADB_Explorer.Resources;
using ADB_Explorer.Services;
using ADB_Explorer.ViewModels;
using static ADB_Explorer.Models.AbstractDevice;

namespace ADB_Explorer.Helpers
{
    public static class DeviceHelpers
    {
        public static DeviceStatus GetStatus(string status) => status switch
        {
            "device" or "recovery" => DeviceStatus.Ok,
            "offline" => DeviceStatus.Offline,
            "unauthorized" or "authorizing" => DeviceStatus.Unauthorized,
            _ => throw new NotImplementedException(),
        };

        public static DeviceType GetType(string id, string status)
        {
            if (status == "recovery")
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

            Data.RuntimeSettings.DeviceToRemove = device;
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
            () => device.Root is not RootStatus.Forbidden && device.Status is DeviceStatus.Ok && device.Type is not DeviceType.Sideload,
            () => ToggleRootAction(device));

        private static async void ToggleRootAction(LogicalDeviceViewModel device)
        {
            bool rootEnabled = device.Root is RootStatus.Enabled;

            await Task.Run(() => device.EnableRoot(!rootEnabled));

            if (device.Root is RootStatus.Forbidden)
                Data.RuntimeSettings.RootAttemptForbidden = true;
        }

        public static DeviceAction ConnectDeviceCommand(NewDeviceViewModel device) => new(
            () => {
                if (!device.IsIpAddressValid || !device.IsConnectPortValid)
                    return false;

                if (device is not null and not HistoryDeviceViewModel
                    && device.IsPairingEnabled
                    && (!device.IsPairingCodeValid || !device.IsPairingPortValid))
                    return false;

                return true;
            },
            () => Data.RuntimeSettings.ConnectNewDevice = device);
    }
}
