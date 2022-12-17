using ADB_Explorer.Models;
using ADB_Explorer.Services;

namespace ADB_Explorer.Helpers
{
    public class DeviceHelpers : AbstractDevice
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
    }
}
