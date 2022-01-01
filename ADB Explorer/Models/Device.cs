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
    public abstract class AbstractDevice : INotifyPropertyChanged
    {
        public enum DeviceType
        {
            Local,
            Remote,
            Emulator,
            Service,
            Sideload
        }

        public enum DeviceStatus
        {
            Online,
            Offline,
            Unauthorized
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public class Devices : AbstractDevice
    {
        private List<UIDevice> uiDevices = new();
        public List<UIDevice> UIList
        {
            get { return uiDevices; }
            private set
            {
                uiDevices = value;
                NotifyPropertyChanged();
            }
        }

        public IEnumerable<UILogicalDevice> UILogicalDevices => UIList?.OfType<UILogicalDevice>();
        public IEnumerable<UIServiceDevice> UIServiceDevices => UIList?.OfType<UIServiceDevice>();
        public IEnumerable<LogicalDevice> LogicalDevices => UILogicalDevices.Select(d => d.Device as LogicalDevice);
        public IEnumerable<ServiceDevice> ServiceDevices => UIServiceDevices.Select(d => d.Device as ServiceDevice);

        public UILogicalDevice Current
        {
            get
            {
                var devices = UILogicalDevices?.Where(device => device.IsOpen);
                return devices.Any() ? devices.First() : null;
            }
        }

        public LogicalDevice CurrentDevice => (LogicalDevice)Current?.Device;

        public UIDevice SelectedDevice => UIList?.Find(device => device.IsSelected);

        public void SetOpen(UILogicalDevice device, bool openState = true) => device.SetOpen(UILogicalDevices.ToList(), openState);

        public void CloseAll() => UILogicalDevice.SetOpen(UILogicalDevices.ToList());

        public void SetSelected(UIDevice device, bool selectState = true) => device.SetSelected(UIList, selectState);

        public void UnselectAll() => UIDevice.SetSelected(UIList);

        public void UpdateServices(IEnumerable<ServiceDevice> other) => UpdateServices(UIList, other);

        public static void UpdateServices(List<UIDevice> self, IEnumerable<ServiceDevice> other)
        {
            self.RemoveAll(thisDevice => thisDevice is UIServiceDevice && !other.Any(otherDevice => otherDevice.ID == thisDevice.Device.ID));

            foreach (var item in other)
            {
                if (self is null || !self.Any(thisDevice => thisDevice is UIServiceDevice && thisDevice.Device.ID == item.ID))
                {
                    // Services should always be displayed first in the list
                    self.Insert(0, new UIServiceDevice(item));
                }
            }

            ConsolidateDevices(self);
        }

        public bool UpdateDevices(IEnumerable<LogicalDevice> other) => UpdateDevices(UIList, other, Current);

        public static bool UpdateDevices(List<UIDevice> self, IEnumerable<LogicalDevice> other, UILogicalDevice current)
        {
            bool isCurrentTypeUpdated = false;

            // First remove all devices that no longer exist
            self.RemoveAll(thisDevice => thisDevice is UILogicalDevice && !other.Any(otherDevice => otherDevice.ID == thisDevice.Device.ID));

            // Then update existing devices' types and names
            foreach (var item in other)
            {
                if (self?.Find(thisDevice => thisDevice.Device.ID == item.ID) is UILogicalDevice device)
                {
                    // Return (at the end of the function) true if current device status has changed
                    if (device is not null && device.IsOpen && device.Device.Status != item.Status)
                        isCurrentTypeUpdated = true;

                    ((LogicalDevice)device.Device).UpdateDevice(item);
                }
                else
                {
                    // And add the new devices
                    self.Add(new UILogicalDevice(item));
                }
            }

            ConsolidateDevices(self);

            return isCurrentTypeUpdated;
        }

        private IEnumerable<UILogicalDevice> AvailableDevices(bool current = false)
        {
            return UILogicalDevices.Where(
                    device => (!current || device.IsOpen)
                    && device.Device.Status is DeviceStatus.Online);
        }

        public bool DevicesAvailable(bool current = false) => AvailableDevices(current).Any();

        public bool SetCurrentDevice(string selectedId)
        {
            var availableDevices = AvailableDevices();

            if (availableDevices.Count() > 1)
            {
                availableDevices = availableDevices.Where(device => device.Device.ID == selectedId);
            }

            if (availableDevices.Any() && availableDevices.First() is UILogicalDevice ui)
            {
                ui.SetOpen(UILogicalDevices.ToList());
                return true;
            }
            else
                return false;
        }

        public bool ServicesChanged(IEnumerable<ServiceDevice> other)
        {
            return other is not null
                && other.All(service => !LogicalDevices.Any(device => device.Status == DeviceStatus.Online && device.BaseID == service.ID))
                && !ServiceDevices.OrderBy(thisDevice => thisDevice.ID).SequenceEqual(
                    other.OrderBy(otherDevice => otherDevice.ID), new ServiceDeviceEqualityComparer());
        }

        public bool DevicesChanged(IEnumerable<LogicalDevice> other)
        {
            return other is not null
                && !LogicalDevices.OrderBy(thisDevice => thisDevice.ID).SequenceEqual(
                    other.OrderBy(otherDevice => otherDevice.ID), new LogicalDeviceEqualityComparer());
        }

        public void ConsolidateDevices() => ConsolidateDevices(UIList);

        public static void ConsolidateDevices(List<UIDevice> devices)
        {
            devices.RemoveAll(device => device is UIServiceDevice && devices.OfType<UILogicalDevice>().Any(logical => logical.Device is LogicalDevice ld && ld.Status == DeviceStatus.Online && ld.BaseID == device.Device.ID));
        }
    }

    public abstract class Device : AbstractDevice
    {
        private DeviceType type;
        public DeviceType Type
        {
            get { return type; }
            protected set
            {
                type = value;
                NotifyPropertyChanged();
            }
        }

        private DeviceStatus status;
        public DeviceStatus Status
        {
            get { return status; }
            protected set
            {
                status = value;
                NotifyPropertyChanged();
            }
        }

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

    public abstract class UIDevice : AbstractDevice
    {
        public Device Device { get; protected set; }

        private bool isSelected;
        public bool IsSelected
        {
            get { return isSelected; }
            protected set
            {
                isSelected = value;
                NotifyPropertyChanged();
            }
        }
        public bool IsMdns { get; protected set; }

        public string TypeIcon => Device.Type switch
        {
            DeviceType.Local => "\uE839",
            DeviceType.Remote => "\uEE77",
            DeviceType.Emulator => "\uE99A",
            DeviceType.Service => "\uEDE4",
            DeviceType.Sideload => "\uED10",
            _ => throw new NotImplementedException(),
        };
        public string StatusIcon => Device.Status switch
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

        public static implicit operator bool(UIDevice obj) => obj?.Device;
    }

    public class LogicalDevice : Device, INotifyPropertyChanged
    {
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
                NotifyPropertyChanged();
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

        public string BaseID => Type == DeviceType.Service ? ID.Split('.')[0] : ID;

        public LogicalDevice(string name, string id)
        {
            Name = name;
            ID = id;
        }

        public LogicalDevice(string name, string id, string status) : this(name, id)
        {
            Type = GetType(id, status);
            Status = GetStatus(status);
        }

        private static DeviceStatus GetStatus(string status)
        {
            return status switch
            {
                "device" or "recovery" => DeviceStatus.Online,
                "offline" => DeviceStatus.Offline,
                "unauthorized" => DeviceStatus.Unauthorized,
                "authorizing" => DeviceStatus.Unauthorized,
                _ => throw new NotImplementedException(),
            };
        }

        private static DeviceType GetType(string id, string status)
        {
            if (status == "recovery")
                return DeviceType.Sideload;
            else if (id.Contains("._adb-tls-connect."))
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

        public string Name => ((LogicalDevice)Device).Name;

        public UILogicalDevice(LogicalDevice device)
        {
            Device = device;
            IsMdns = false;
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
            Device = service;
            IsMdns = true;
        }

    }

    public class LogicalDeviceEqualityComparer : IEqualityComparer<LogicalDevice>
    {
        public bool Equals(LogicalDevice x, LogicalDevice y)
        {
            return x.ID == y.ID && x.Status == y.Status;
        }

        public int GetHashCode([DisallowNull] LogicalDevice obj)
        {
            throw new NotImplementedException();
        }
    }

    public class ServiceDeviceEqualityComparer : IEqualityComparer<ServiceDevice>
    {
        public bool Equals(ServiceDevice x, ServiceDevice y)
        {
            return x.ID == y.ID;
        }

        public int GetHashCode([DisallowNull] ServiceDevice obj)
        {
            throw new NotImplementedException();
        }
    }
}
