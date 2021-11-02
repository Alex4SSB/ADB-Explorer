using ADB_Explorer.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace ADB_Explorer.Models
{
    public static class Devices
    {
        public static List<DeviceClass> List { get; private set; } = new();

        public static DeviceClass Current => List?.Find(device => device.IsOpen);

        public static bool Update(IEnumerable<DeviceClass> other)
        {
            bool isCurrentTypeUpdated = false;

            // First remove all devices that no longer exist
            List.RemoveAll(thisDevice => !other.Any(otherDevice => otherDevice.ID == thisDevice.ID));

            // Then update existing devices' types and names
            foreach (var item in other)
            {
                if (List?.Find(thisDevice => thisDevice.ID == item.ID) is DeviceClass device)
                {
                    // Return (at the end of the function) true if current device status has changed
                    if (device.IsOpen && device.Status != item.Status)
                        isCurrentTypeUpdated = true;

                    device.UpdateDevice(item);
                }
                else
                {
                    // And add the new devices
                    List.Add(item);
                }
            }

            return isCurrentTypeUpdated;
        }

        private static IEnumerable<DeviceClass> AvailableDevices(bool current = false)
        {
            return List.Where(device => (!current || device.IsOpen)
                && device.Type is DeviceClass.DeviceType.Local or DeviceClass.DeviceType.Remote);
        }

        public static bool DevicesAvailable(bool current = false) => AvailableDevices(current).Any();

        public static bool SetCurrentDevice(string selectedId)
        {
            var availableDevices = AvailableDevices();

            if (availableDevices.Count() > 1)
            {
                availableDevices = availableDevices.Where(device => device.ID == selectedId);
            }

            if (availableDevices.Any())
            {
                availableDevices.First().SetOpen();
                return true;
            }
            else
                return false;
        }

        public static bool DevicesChanged(IEnumerable<DeviceClass> other)
        {
            return other is not null
                && !List.OrderBy(thisDevice => thisDevice.ID).SequenceEqual(other.OrderBy(otherDevice => otherDevice.ID), new DeviceTypeEqualityComparer());
        }

        public static void UnselectAll()
        {
            DeviceClass.SetSelected();
        }
    }


    public class DeviceClass
    {
        public enum DeviceType
        {
            Local,
            Remote,
            Emulator
        }

        public enum DeviceStatus
        {
            Online,
            Offline,
            Unauthorized
        }

        public string Name { get; private set; }
        public string ID { get; private set; }
        public DeviceType Type { get; private set; }
        public DeviceStatus Status { get; private set; }
        public string TypeIcon => Type switch
        {
            DeviceType.Local => "\uE839",
            DeviceType.Remote => "\uEE77",
            DeviceType.Emulator => "\uE99A",
            _ => throw new NotImplementedException(),
        };
        public string StatusIcon => Status switch
        {
            DeviceStatus.Online => "",
            DeviceStatus.Offline => "\uEBFF",
            DeviceStatus.Unauthorized => "\uEC00",
            _ => throw new NotImplementedException(),
        };
        public bool IsOpen { get; private set; }
        private List<Drive> drives;
        public List<Drive> Drives
        {
            get => drives;
            set
            {
                drives = value;
                // TODO: uncomment these when async update is ready
                //var mmcTask = Task.Run(() => { return GetMmcDrive(); });
                //mmcTask.ContinueWith((t) => { App.Current.Dispatcher.BeginInvoke(() => { t.Result?.SetMmc(); }); });
            }
        }

        public void SetOpen(bool openState = true)
        {
            Devices.List.ForEach(device => device.IsOpen =
                device == this && openState);
        }

        public bool IsSelected { get; private set; }

        public void SetSelected(bool selectedState = true)
        {
            Devices.List.ForEach(device => device.IsSelected =
                device == this && selectedState);
        }

        public static void SetSelected()
        {
            Devices.List.ForEach(device => device.IsSelected = false);
        }

        public override bool Equals(object obj)
        {
            return obj is DeviceClass device &&
                   ID == device.ID;
        }

        public DeviceClass(string name, string id)
        {
            Name = name;
            ID = id;
        }

        public DeviceClass(string name, string id, string status) : this(name, id)
        {
            Type = id.Contains('.')
                ? DeviceType.Remote : id.Contains("emulator")
                    ? DeviceType.Emulator
                    : DeviceType.Local;

            Status = status switch
            {
                "device" => DeviceStatus.Online,
                "offline" => DeviceStatus.Offline,
                "unauthorized" => DeviceStatus.Unauthorized,
                "authorizing" => DeviceStatus.Unauthorized,
                _ => throw new NotImplementedException(),
            };
        }

        public DeviceClass()
        {
        }

        public static implicit operator bool(DeviceClass obj)
        {
            return obj is object && !string.IsNullOrEmpty(obj.ID);
        }

        public override int GetHashCode()
        {
            return ID.GetHashCode();
        }

        public void UpdateDevice(DeviceClass other)
        {
            Name = other.Name;
            Status = other.Status;
        }

        internal void SetDrives(List<Drive> drives)
        {
            Drives = drives;

            GetMmcDrive().SetMmc(); // TODO: remove this line when INotify is implemented
        }

        public static string DeviceName(string model, string device)
        {
            var name = device;
            if (device == device.ToLower())
                name = model;

            return name.Replace('_', ' ');
        }

        public Drive GetMmcDrive()
        {
            var externalDrives = Drives.Where(d => d.Type == DriveType.External);
            switch (externalDrives.Count())
            {
                case > 1:
                    var mmc = ADBService.GetMmcId(ID);
                    return Drives.Find(d => d.ID == mmc);
                case 1:
                    return ADBService.MmcExists(ID) ? externalDrives.First() : null;
                default:
                    return null;
            }
        }

        //private DispatcherTimer dispatcherTimer;

        //public void StartRefresh()
        //{
        //    dispatcherTimer = new()
        //    {
        //        Interval = AdbExplorerConst.DRIVE_UPDATE_INTERVAL
        //    };
        //    dispatcherTimer.Tick += DispatcherTimer_Tick;

        //    dispatcherTimer.Start();
        //}

        //public void StopRefresh()
        //{
        //    dispatcherTimer.Stop();
        //    dispatcherTimer = null;
        //}

        //private void DispatcherTimer_Tick(object sender, EventArgs e)
        //{
        //    var prev = Drives;


        //}
    }

    public class DeviceTypeEqualityComparer : IEqualityComparer<DeviceClass>
    {
        public bool Equals(DeviceClass x, DeviceClass y)
        {
            return x.ID == y.ID && x.Status == y.Status;
        }

        public int GetHashCode([DisallowNull] DeviceClass obj)
        {
            throw new NotImplementedException();
        }
    }
}
