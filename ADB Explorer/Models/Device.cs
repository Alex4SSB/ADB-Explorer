using ADB_Explorer.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace ADB_Explorer.Models
{
    public class Devices : INotifyPropertyChanged
    {
        private List<Device> devices = new();
        public List<Device> DeviceList
        {
            get { return devices; }
            private set
            {
                devices = value;
                NotifyPropertyChanged();
                NotifyPropertyChanged(nameof(UIList));
            }
        }
        public List<UIDevice> UIList => GetUIDevices(DeviceList);
        public IEnumerable<LogicalDevice> LogicalDevices => DeviceList?.OfType<LogicalDevice>();
        public IEnumerable<ServiceDevice> ServiceDevices => DeviceList?.OfType<ServiceDevice>();
        public IEnumerable<UILogicalDevice> UILogicalDevices => UIList?.OfType<UILogicalDevice>();
        public IEnumerable<UIServiceDevice> UIServiceDevices => UIList?.OfType<UIServiceDevice>();

        public LogicalDevice Current
        {
            get
            {
                var devices = UILogicalDevices?.Where(device => device.IsOpen);
                return devices.Any() && devices.First().DeviceRef is LogicalDevice device ? device : null;
            }
        }

        public void SetOpen(LogicalDevice device, bool openState = true) => device.UIRef.SetOpen(UILogicalDevices.ToList(), openState);

        public void CloseAll() => UILogicalDevice.SetOpen(UILogicalDevices.ToList());

        public void SetSelected(UIDevice device, bool selectState = true) => device.SetSelected(UIList, selectState);

        public void UnselectAll() => UIDevice.SetSelected(UIList);

        private static List<UIDevice> GetUIDevices(List<Device> devices)
        {
            List<UIDevice> result = new();

            result.AddRange(from service in devices.Where(d => d is ServiceDevice)
                            select new UIServiceDevice((ServiceDevice)service));

            result.AddRange(from device in devices.Where(d => d is LogicalDevice)
                            select new UILogicalDevice((LogicalDevice)device));

            return result;
        }

        public bool Update(IEnumerable<LogicalDevice> other)
        {
            bool isCurrentTypeUpdated = false;

            // First remove all devices that no longer exist
            DeviceList.RemoveAll(thisDevice => !other.Any(otherDevice => otherDevice.ID == thisDevice.ID));

            // Then update existing devices' types and names
            foreach (var item in other)
            {
                if (DeviceList?.Find(thisDevice => thisDevice.ID == item.ID) is LogicalDevice device)
                {
                    // Return (at the end of the function) true if current device status has changed
                    if (device.UIRef is not null && device.UIRef.IsOpen && device.Status != item.Status)
                        isCurrentTypeUpdated = true;

                    device.UpdateDevice(item);
                }
                else
                {
                    // And add the new devices
                    DeviceList.Add(item);
                }
            }

            return isCurrentTypeUpdated;
        }

        private IEnumerable<LogicalDevice> AvailableDevices(bool current = false)
        {
            return UILogicalDevices.Where(
                    device => (!current || device.IsOpen)
                    && device.Status is Device.DeviceStatus.Online
                ).Select(d => d.DeviceRef as LogicalDevice);
        }

        public bool DevicesAvailable(bool current = false) => AvailableDevices(current).Any();

        public bool SetCurrentDevice(string selectedId)
        {
            var availableDevices = AvailableDevices();

            if (availableDevices.Count() > 1)
            {
                availableDevices = availableDevices.Where(device => device.ID == selectedId);
            }

            if (availableDevices.Any() && availableDevices.First().UIRef is UILogicalDevice ui)
            {
                ui.SetOpen(UILogicalDevices.ToList());
                return true;
            }
            else
                return false;
        }

        public bool DevicesChanged(IEnumerable<LogicalDevice> other)
        {
            return other is not null
                && !DeviceList.OrderBy(thisDevice => thisDevice.ID).SequenceEqual(other.OrderBy(otherDevice => otherDevice.ID), new DeviceTypeEqualityComparer());
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public abstract class Device
    {
        public enum DeviceType
        {
            Local,
            Remote,
            Emulator,
            Service
        }

        public enum DeviceStatus
        {
            Online,
            Offline,
            Unauthorized
        }

        public DeviceType Type { get; protected set; }
        public DeviceStatus Status { get; protected set; }
        public string ID { get; protected set; }

        public static implicit operator bool(Device obj)
        {
            return obj is not null && !string.IsNullOrEmpty(obj.ID);
        }

        public override int GetHashCode()
        {
            return ID is null ? 0 : ID.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            return obj is Device device &&
                   ID == device.ID;
        }
    }

    public abstract class UIDevice : Device
    {
        public Device DeviceRef { get; protected set; }
        public bool IsSelected { get; protected set; }

        public string TypeIcon => Type switch
        {
            DeviceType.Local => "\uE839",
            DeviceType.Remote => "\uEE77",
            DeviceType.Emulator => "\uE99A",
            DeviceType.Service => "\uEDE4",
            _ => throw new NotImplementedException(),
        };
        public string StatusIcon => Status switch
        {
            DeviceStatus.Online => "",
            DeviceStatus.Offline => "\uEBFF",
            DeviceStatus.Unauthorized => "\uEC00",
            _ => throw new NotImplementedException(),
        };

        public void SetSelected(List<UIDevice> devices, bool selectedState = true)
        {
            devices.ForEach(device => device.IsSelected =
                device.Equals(this) && selectedState);
        }

        public static void SetSelected(List<UIDevice> devices)
        {
            devices.ForEach(device => device.IsSelected = false);
        }
    }

    public class LogicalDevice : Device, INotifyPropertyChanged
    {
        public UILogicalDevice UIRef { get; set; }

        private string name;
        public string Name
        {
            get
            {
                return Type == DeviceType.Emulator ? ID : name;
            }
            private set
            {
                name = value;
            }
        }
        
        private List<Drive> drives;
        public List<Drive> Drives
        {
            get => drives;
            set
            {
                drives = value;

                if (drives is not null)
                {
                    var mmcTask = Task.Run(() => { return GetMmcDrive(); });
                    mmcTask.ContinueWith((t) =>
                    {
                        App.Current.Dispatcher.BeginInvoke(() =>
                        {
                            t.Result?.SetMmc();
                            SetExternalDrives();
                        });
                    });
                }

                NotifyPropertyChanged();
            }
        }

        public LogicalDevice(string name, string id)
        {
            Name = name;
            ID = id;
        }

        public LogicalDevice(string name, string id, string status) : this(name, id)
        {
            Type = GetType(id);
            Status = GetStatus(status);
        }

        private static DeviceStatus GetStatus(string status)
        {
            return status switch
            {
                "device" => DeviceStatus.Online,
                "offline" => DeviceStatus.Offline,
                "unauthorized" => DeviceStatus.Unauthorized,
                "authorizing" => DeviceStatus.Unauthorized,
                _ => throw new NotImplementedException(),
            };
        }

        private static DeviceType GetType(string id)
        {
            if (id.Contains("._adb-tls-connect."))
                return DeviceType.Service;
            else if (id.Contains('.'))
                return DeviceType.Remote;
            else if (id.Contains("emulator"))
                return DeviceType.Emulator;
            else
                return DeviceType.Local;
        }

        public void UpdateDevice(LogicalDevice other)
        {
            Name = other.Name;
            Status = other.Status;
        }

        private void DrivesSetBlocking(List<Drive> val)
        {
            drives = val;
            NotifyPropertyChanged(nameof(Drives));
        }

        internal void SetDrives(List<Drive> value, bool findMmc = false)
        {
            if (findMmc)
            {
                DrivesSetBlocking(value);
                GetMmcDrive().SetMmc();
                SetExternalDrives();
            }
            else
                Drives = value;
        }

        public Drive GetMmcDrive()
        {
            var externalDrives = Drives.Where(d => d.Type == DriveType.Unknown);
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

        public void SetExternalDrives()
        {
            foreach (var item in Drives.Where(d => d.Type == DriveType.Unknown))
            {
                item.SetOtg();
            }
        }

        public static string DeviceName(string model, string device)
        {
            var name = device;
            if (device == device.ToLower())
                name = model;

            return name.Replace('_', ' ');
        }

        public override string ToString() => Name;

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

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

    public class UILogicalDevice : UIDevice
    {
        public bool IsOpen { get; protected set; }

        public string Name => ((LogicalDevice)DeviceRef).Name;

        public UILogicalDevice(LogicalDevice device)
        {
            DeviceRef = device;
            device.UIRef = this;
        }

        public void SetOpen(List<UILogicalDevice> list, bool openState = true)
        {
            list.ForEach(device => device.IsOpen =
                device.Equals(this) && openState);
        }

        public static void SetOpen(List<UILogicalDevice> list)
        {
            list.ForEach((device) => device.IsOpen = false);
        }
    }

    public class ServiceDevice : Device
    {
        public ServiceDevice()
        {
            Type = DeviceType.Service;
            Status = DeviceStatus.Unauthorized;
        }

        public ServiceDevice(string id, string ipAddress) : this()
        {
            ID = id;
            IpAddress = ipAddress;
        }

        public enum ServiceType
        {
            QrCode,
            PairingCode
        }

        public UIServiceDevice UIRef { get; set; }
        public string IpAddress { get; set; }
        public string PairingPort { get; set; }
        public string ConnectPort { get; set; }
        public ServiceType MdnsType { get; set; }

        public string PairingAddress => $"{IpAddress}:{PairingPort}";
    }

    public class UIServiceDevice : UIDevice
    {
        public UIServiceDevice(ServiceDevice service)
        {
            DeviceRef = service;
            service.UIRef = this;
        }

    }

    public class DeviceTypeEqualityComparer : IEqualityComparer<Device>
    {
        public bool Equals(Device x, Device y)
        {
            return x.ID == y.ID && x.Status == y.Status;
        }

        public int GetHashCode([DisallowNull] Device obj)
        {
            throw new NotImplementedException();
        }
    }
}
