using ADB_Explorer.Helpers;
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

        public enum RootStatus
        {
            Unchecked,
            Forbidden,
            Disabled,
            Enabled,
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void Set<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(storage, value))
            {
                return;
            }

            storage = value;
            OnPropertyChanged(propertyName);
        }

        protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public class Devices : AbstractDevice
    {
        private ObservableList<UIDevice> uiDevices = new();
        public ObservableList<UIDevice> UIList
        {
            get => uiDevices;
            private set => Set(ref uiDevices, value);
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

        public UIDevice SelectedDevice => UIList?.Find(device => device.DeviceSelected);

        public DateTime LastUpdate { get; set; }

        public void SetOpen(UILogicalDevice device, bool openState = true) => device.SetOpen(UILogicalDevices.ToList(), openState);

        public void CloseAll() => UILogicalDevice.SetOpen(UILogicalDevices.ToList());

        public void SetSelected(UIDevice device, bool selectState = true) => device.SetSelected(UIList, selectState);

        public void UnselectAll() => UIDevice.SetSelected(UIList);

        public void UpdateServices(IEnumerable<ServiceDevice> other) => UpdateServices(UIList, other);

        public static void UpdateServices(ObservableList<UIDevice> self, IEnumerable<ServiceDevice> other)
        {
            self.RemoveAll(thisDevice => thisDevice is UIServiceDevice && !other.Any(otherDevice => otherDevice.ID == thisDevice.Device.ID));

            foreach (var item in other)
            {
                if (self?.Find(thisDevice => thisDevice.Device.ID == item.ID) is UIServiceDevice service)
                {
                    ((ServiceDevice)service.Device).UpdateService(item);
                }
                else
                {
                    // Services should always be displayed first in the list
                    self.Insert(0, new UIServiceDevice(item));
                }
            }

            ConsolidateDevices(self);
        }

        public bool UpdateDevices(IEnumerable<LogicalDevice> other) => UpdateDevices(UIList, other, Current);

        public static bool UpdateDevices(ObservableList<UIDevice> self, IEnumerable<LogicalDevice> other, UILogicalDevice current)
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

            if (self.OfType<UIServiceDevice>().Any())
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
            // if the list is null, were probably not ready to update
            if (other is null)
                return false;

            // if the list is empty, we need to update (and remove all items)
            if (!other.Any())
                return ServiceDevices.Any();

            // if there's any service whose ID is not found in any logical device,
            // AND an ordering of both new and old lists doesn't match up all IDs
            return other.Any(service => !LogicalDevices.Any(device => device.Status == DeviceStatus.Online && device.BaseID == service.ID))
                   && !ServiceDevices.OrderBy(thisDevice => thisDevice.ID).SequenceEqual(other.OrderBy(otherDevice => otherDevice.ID), new ServiceDeviceEqualityComparer());
        }

        public bool DevicesChanged(IEnumerable<LogicalDevice> other)
        {
            return other is not null
                && !LogicalDevices.OrderBy(thisDevice => thisDevice.ID).SequenceEqual(
                    other.OrderBy(otherDevice => otherDevice.ID), new LogicalDeviceEqualityComparer());
        }

        public void ConsolidateDevices() => ConsolidateDevices(UIList);

        public static void ConsolidateDevices(ObservableList<UIDevice> devices)
        {
            foreach (var device in devices.OfType<UILogicalDevice>().ToList())
            {
                var services = devices.Where(s => s is UIServiceDevice && s.Device.ID == ((LogicalDevice)device.Device).BaseID).ToList();
                if (!services.Any())
                    continue;

                var qrServices = devices.Where(q => q is UIServiceDevice
                    && q.Device is ServiceDevice service
                    && service.MdnsType == ServiceDevice.ServiceType.QrCode
                    && service.IpAddress == ((ServiceDevice)services.First().Device).IpAddress
                ).ToList();

                switch (device.Device.Status)
                {
                    // if logical device is online - remove all related services
                    case DeviceStatus.Online:
                    {
                        services.ForEach(s => devices.Remove(s));
                        qrServices.ForEach(s => devices.Remove(s));
                        break;
                    }
                    // if logical device is offline, and we have one of its services - remove the logical device
                    case DeviceStatus.Offline:
                    {
                        devices.Remove(device);
                        break;
                    }
                    default:
                        break;
                }
            }

            // if there's a QR service and a regular service, but no logical - the regular service is probably outdated so it is removed here
            devices.RemoveAll(s => s is UIServiceDevice
                && s.Device is ServiceDevice serv
                && serv.MdnsType == ServiceDevice.ServiceType.PairingCode
                && !devices.OfType<UILogicalDevice>().Any(l => ((LogicalDevice)l.Device).BaseID == serv.ID)
                && devices.Any(q => q is UIServiceDevice && q.Device is ServiceDevice qr
                    && qr.IpAddress == serv.IpAddress
                    && qr.MdnsType == ServiceDevice.ServiceType.QrCode));
        }
    }

    public abstract class Device : AbstractDevice
    {
        private DeviceType type;
        public DeviceType Type
        {
            get => type;
            protected set => Set(ref type, value);
        }

        private DeviceStatus status;
        public DeviceStatus Status
        {
            get => status;
            protected set => Set(ref status, value);
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
        public bool DeviceSelected
        {
            get => isSelected;
            set => Set(ref isSelected, value);
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

        public void SetSelected(ObservableList<UIDevice> devices, bool selectedState = true)
        {
            devices.ForEach(device => device.DeviceSelected =
                device.Equals(this) && selectedState);
        }

        public static void SetSelected(ObservableList<UIDevice> devices)
        {
            devices.ForEach(device => device.DeviceSelected = false);
        }

        public static implicit operator bool(UIDevice obj) => obj?.Device;
    }

    public class LogicalDevice : Device, INotifyPropertyChanged
    {
        private string name;
        public string Name
        {
            get => Type == DeviceType.Emulator ? ID : name;
            private set => Set(ref name, value);
        }

        private List<Drive> drives;
        public List<Drive> Drives
        {
            get => drives;
            set
            {
                Set(ref drives, value);

                if (value is not null)
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
            }
        }

        public string BaseID => Type == DeviceType.Service ? ID.Split('.')[0] : ID;

        private RootStatus root = RootStatus.Unchecked;
        public RootStatus Root
        {
            get => root;
            set
            {
                Set(ref root, value);

                if (Data.DevicesRoot.ContainsKey(ID))
                    Data.DevicesRoot[ID] = value;
                else
                    Data.DevicesRoot.Add(ID, value);
            }
        }

        private Battery battery;
        public Battery Battery
        {
            get => battery;
            set => Set(ref battery, value);
        }

        private LogicalDevice(string name, string id)
        {
            Name = name;
            ID = id;

            if (Data.DevicesRoot.ContainsKey(ID))
                root = Data.DevicesRoot[ID];
        }

        public static LogicalDevice New(string name, string id, string status)
        {
            var deviceType = GetType(id, status);
            var deviceStatus = GetStatus(status);

            if (!AdbExplorerConst.DISPLAY_OFFLINE_SERVICES && deviceType is DeviceType.Service && deviceStatus is DeviceStatus.Offline)
                return null;

            return new LogicalDevice(name, id) { Type = deviceType, Status = deviceStatus };
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
            else if (id.Contains("._adb-tls-"))
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
            Set(ref drives, val, nameof(Drives));
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

        public void EnableRoot(bool enable)
        {
            Root = enable
                ? ADBService.Root(this) ? RootStatus.Enabled : RootStatus.Forbidden
                : ADBService.Unroot(this) ? RootStatus.Disabled : RootStatus.Unchecked;
        }

        public void UpdateBattery()
        {
            Battery = new(ADBService.AdbDevice.GetBatteryInfo(this));
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

        public byte? AndroidVersion
        {
            get
            {
                if (IsOpen)
                {
                    var version = Data.CurrentADBDevice.GetAndroidVersion();
                    if (byte.TryParse(version.Split('.')[0], out byte ver))
                        return ver;
                }

                return null;
            }
        }

        public bool AndroidVersionIncompatible => AndroidVersion is not null && AndroidVersion < AdbExplorerConst.MIN_SUPPORTED_ANDROID_VER;

        public string Tooltip
        {
            get
            {
                string result = "";

                result += Device.Type switch
                {
                    DeviceType.Local => "USB",
                    DeviceType.Remote => "WiFi",
                    DeviceType.Emulator => "Emulator",
                    DeviceType.Service => "mDNS Service",
                    DeviceType.Sideload => "USB (Sideload)",
                    _ => throw new NotImplementedException(),
                };

                result += Device.Status switch
                {
                    DeviceStatus.Online => "",
                    DeviceStatus.Offline => " - Offline",
                    DeviceStatus.Unauthorized => " - Unauthorized",
                    _ => throw new NotImplementedException(),
                };

                return result;
            }
        }

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
            UpdateStatus();
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

        public void UpdateService(ServiceDevice other)
        {
            PairingPort = other.PairingPort;
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            Status = string.IsNullOrEmpty(PairingPort) ? DeviceStatus.Offline : DeviceStatus.Unauthorized;
        }

        public string IpAddress { get; set; }
        public string PairingPort { get; set; }
        public ServiceType MdnsType { get; set; }
        public string PairingCode { get; set; }

        public string PairingAddress => $"{IpAddress}:{PairingPort}";
    }

    public class UIServiceDevice : UIDevice
    {
        public UIServiceDevice(ServiceDevice service)
        {
            Device = service;
            IsMdns = true;
        }

        private string uiPairingCode;
        public string UIPairingCode
        {
            get => uiPairingCode;
            set
            {
                uiPairingCode = value;
                ((ServiceDevice)Device).PairingCode = uiPairingCode.Replace("-", "");
            }
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
            // IDs are equal and either both ports have a value, or they're both null
            // We do not update the port since it can change too frequently, and we do not use it anyway
            return x.ID == y.ID && !(string.IsNullOrEmpty(x.PairingPort) ^ string.IsNullOrEmpty(y.PairingPort));
        }

        public int GetHashCode([DisallowNull] ServiceDevice obj)
        {
            throw new NotImplementedException();
        }
    }
}
