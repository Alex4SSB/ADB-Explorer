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
            Sideload,
        }

        public enum DeviceStatus
        {
            Online,
            Offline,
            Unauthorized,
        }

        public enum RootStatus
        {
            Unchecked,
            Forbidden,
            Disabled,
            Enabled,
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual bool Set<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(storage, value))
            {
                return false;
            }

            storage = value;
            OnPropertyChanged(propertyName);

            return true;
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

        private ObservableList<ConnectService> connectServices = new();
        public ObservableList<ConnectService> ConnectServices
        {
            get => connectServices;
            private set
            {
                Set(ref connectServices, value);
                UpdateConnectServices();
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

        public UIDevice SelectedDevice => UIList?.Find(device => device.DeviceSelected);

        public DateTime LastUpdate { get; set; }

        public void UpdateConnectServices()
        {
            foreach (var item in LogicalDevices)
            {
                var service = ConnectServices.Where(srv => srv.ID == item.BaseID);
                if (service.Any() && (item.Service is null || item.Service.Port != service.First().Port))
                    item.UpdateConnectService(service.First());
            }

            ConsolidateDevices();
        }

        public void SetOpen(UILogicalDevice device, bool openState = true) => device.SetOpen(UILogicalDevices.ToList(), openState);

        public void CloseAll() => UILogicalDevice.SetOpen(UILogicalDevices.ToList());

        public void SetSelected(UIDevice device, bool selectState = true) => device.SetSelected(UIList, selectState);

        public void UnselectAll() => UIDevice.SetSelected(UIList);

        public void UpdateServices(IEnumerable<ServiceDevice> other)
        {
            AddConnectServices(other.OfType<ConnectService>());
            UpdateServices(UIList, other);
        }

        public void AddConnectServices(IEnumerable<ConnectService> services)
        {
            foreach (var item in services)
            {
                var prev = ConnectServices.Where(srv => srv.ID == item.ID);
                if (prev.Any())
                    prev.First().IpAddress = item.IpAddress;
                else
                    ConnectServices.Add(item);
            }
        }

        public static void UpdateServices(ObservableList<UIDevice> self, IEnumerable<ServiceDevice> other)
        {
            var pairing = other.OfType<PairingService>();

            self.RemoveAll(thisDevice => thisDevice is UIServiceDevice && !pairing.Any(otherDevice => otherDevice.ID == thisDevice.Device.ID));

            foreach (var item in pairing)
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

        public bool UpdateDevices(IEnumerable<LogicalDevice> other) => UpdateDevices(UIList, other, ConnectServices);

        public static bool UpdateDevices(ObservableList<UIDevice> self, IEnumerable<LogicalDevice> other, ObservableList<ConnectService> services)
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
            {
                if (self.OfType<UILogicalDevice>().Any(d => ((LogicalDevice)d.Device).Service is null))
                {
                    foreach (var item in self.OfType<UILogicalDevice>().Select(dev => dev.Device as LogicalDevice))
                    {
                        var service = services.Where(srv => srv.ID == item.ID);
                        if (service.Any() && (item.Service is null || item.Service.Port != service.First().Port))
                            item.UpdateConnectService(service.First());
                    }
                }
                ConsolidateDevices(self);
            }

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

            var pairing = other.OfType<PairingService>();
            AddConnectServices(other.OfType<ConnectService>());

            // if the list is empty, we need to update (and remove all items)
            if (!pairing.Any())
                return ServiceDevices.Any();

            // if there's any service whose ID is not found in any logical device,
            // AND an ordering of both new and old lists doesn't match up all IDs
            return pairing.Any(service => !LogicalDevices.Any(device => device.Status == DeviceStatus.Online && device.BaseID == service.ID || device.IpAddress == service.IpAddress))
                   && !ServiceDevices.OrderBy(thisDevice => thisDevice.ID).SequenceEqual(pairing.OrderBy(otherDevice => otherDevice.ID), new ServiceDeviceEqualityComparer());
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
                var logical = device.Device as LogicalDevice;
                var services = devices.Where(s => s.Device is ServiceDevice service && (service.ID == logical.BaseID || service.IpAddress == logical.IpAddress)).ToList();
                if (!services.Any())
                    continue;

                var qrServices = devices.Where(q => q is UIServiceDevice
                    && q.Device is ServiceDevice service
                    && service.MdnsType == ServiceDevice.ServiceType.QrCode
                    && service.IpAddress == ((ServiceDevice)services.First().Device).IpAddress
                ).ToList();

                switch (logical.Status)
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
                && !devices.OfType<UILogicalDevice>().Any(l => ((LogicalDevice)l.Device).BaseID == serv.ID || ((LogicalDevice)l.Device).IpAddress == serv.IpAddress)
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
                obj.GetType() == GetType() &&
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
        public abstract string Tooltip { get; }

        public string TypeIcon => Device.Type switch
        {
            DeviceType.Local => "\uE839",
            DeviceType.Remote => "\uEE77",
            DeviceType.Emulator => "\uE99A",
            DeviceType.Service when Device is ServiceDevice service && service.MdnsType is ServiceDevice.ServiceType.QrCode => "\uED14",
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

        private ObservableList<Drive> drives = new();
        public ObservableList<Drive> Drives
        {
            get => drives;
            set => Set(ref drives, value);
        }

        public string BaseID => Type == DeviceType.Service ? ID.Split('.')[0] : ID;

        private RootStatus root = RootStatus.Unchecked;
        public RootStatus Root
        {
            get => root;
            set => Set(ref root, value);
        }

        private Battery battery;
        public Battery Battery
        {
            get => battery;
            set => Set(ref battery, value);
        }

        private ConnectService service;
        public ConnectService Service
        {
            get => service;
            private set => Set(ref service, value);
        }

        public string IpAddress => service?.IpAddress;

        private LogicalDevice(string name, string id, ConnectService service = null)
        {
            Name = name;
            ID = id;
            this.service = service;
        }

        public static LogicalDevice New(string name, string id, string status, ObservableList<ConnectService> services = null)
        {
            ConnectService service = null;
            var deviceType = GetType(id, status);
            var deviceStatus = GetStatus(status);
            var rootStatus = deviceType is DeviceType.Sideload
                ? RootStatus.Enabled
                : RootStatus.Unchecked;

            if (!AdbExplorerConst.DISPLAY_OFFLINE_SERVICES && deviceType is DeviceType.Service && deviceStatus is DeviceStatus.Offline)
                return null;

            if (deviceType is DeviceType.Service && services is not null)
            {
                var srv = services.Where(s => s.ID == id);
                if (srv.Any())
                    service = srv.First();
            }

            return new LogicalDevice(name, id, service) { Type = deviceType, Status = deviceStatus, Root = rootStatus };
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

        public void UpdateConnectService(ConnectService service)
        {
            Service = service;
        }

        /// <summary>
        /// Set <see cref="Device"/> with new drives
        /// </summary>
        /// <param name="value">The new drives to be assigned</param>
        /// <param name="asyncClasify">true to update only after fully acquiring all information</param>
        internal void SetDrives(IEnumerable<Drive> value, Dispatcher dispatcher, bool asyncClasify = false)
        {
            if (asyncClasify)
            {
                var asyncTask = Task.Run(() =>
                { 
                    GetMmcDrive(value, ID)?.SetMmc();
                    SetExternalDrives(ref value);
                });
                asyncTask.ContinueWith((t) =>
                {
                    dispatcher.BeginInvoke(() =>
                    {
                        _setDrives(value);
                    });
                });
            }
            else
            {
                _setDrives(value);

                var mmcTask = Task.Run(() => { return GetMmcDrive(value, ID); });
                mmcTask.ContinueWith((t) =>
                {
                    dispatcher.BeginInvoke(() =>
                    {
                        t.Result?.SetMmc();
                        SetExternalDrives(ref value);
                    });
                });
            }
        }

        private void _setDrives(IEnumerable<Drive> drives)
        {
            if (!DrivesChanged(drives.Where(d => d.Type is not DriveType.Trash and not DriveType.Temp and not DriveType.Package)))
                return;

            var trash = Drives.Where(d => d.Type is DriveType.Trash);
            if (trash.Any())
                drives = drives.Append(trash.First());

            var temp = Drives.Where(d => d.Type is DriveType.Temp);
            if (temp.Any())
                drives = drives.Append(temp.First());

            var apk = Drives.Where(d => d.Type is DriveType.Package);
            if (apk.Any())
                drives = drives.Append(apk.First());

            Drives.Set(drives);
        }

        public bool DrivesChanged(IEnumerable<Drive> other)
        {
            var self = Drives.Where(d => d.Type is not DriveType.Trash and not DriveType.Temp and not DriveType.Package);

            return other is not null
                && !self.OrderBy(thisDrive => thisDrive.ID).SequenceEqual(
                    other.OrderBy(otherDrive => otherDrive.ID), new Drive.DriveEqualityComparer());
        }

        public static Drive GetMmcDrive(IEnumerable<Drive> drives, string deviceID)
        {
            if (Data.CurrentADBDevice.MmcProp is string mmcId)
            {
                var mmcDrive = drives.Where(d => d.ID == mmcId);
                if (mmcDrive.Any())
                {
                    var mmc = mmcDrive.First();
                    mmc.SetMmc();
                    return mmc;
                }
            }
            else if (Data.CurrentADBDevice.OtgProp is not null)
                return null;

            var externalDrives = drives.Where(d => d.Type == DriveType.Unknown);

            switch (externalDrives.Count())
            {
                case > 1:
                    var mmc = ADBService.GetMmcId(deviceID);
                    var drive = drives.Where(d => d.ID == mmc);
                    return drive.Any() ? drive.First() : null;
                case 1:
                    return ADBService.MmcExists(deviceID) ? externalDrives.First() : null;
                default:
                    return null;
            }
        }

        public static void SetExternalDrives(ref IEnumerable<Drive> drives)
        {
            foreach (var item in drives.Where(d => d.Type == DriveType.Unknown))
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
    }

    public class UILogicalDevice : UIDevice
    {
        public bool IsOpen { get; protected set; }

        public string Name => ((LogicalDevice)Device).Name;

        private byte? androidVersion = null;
        public byte? AndroidVersion
        {
            get => androidVersion;
            private set => Set(ref androidVersion, value);
        }

        public bool AndroidVersionIncompatible => AndroidVersion is not null && AndroidVersion < AdbExplorerConst.MIN_SUPPORTED_ANDROID_VER;

        public override string Tooltip
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
                    DeviceType.Sideload => "USB (Recovery)",
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

        public void SetAndroidVersion(string version)
        {
            if (!IsOpen)
                return;

            if (byte.TryParse(version.Split('.')[0], out byte ver))
                AndroidVersion = ver;
        }
    }

    public abstract class ServiceDevice : Device
    {
        public ServiceDevice()
        {
            Type = DeviceType.Service;
        }

        public ServiceDevice(string id, string ipAddress, string port = "") : this()
        {
            ID = id;
            IpAddress = ipAddress;
            Port = port;
            UpdateStatus();
        }

        public enum ServiceType
        {
            QrCode,
            PairingCode
        }

        public void UpdateService(ServiceDevice other)
        {
            Port = other.Port;
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            Status = MdnsType is ServiceType.QrCode ? DeviceStatus.Online : DeviceStatus.Unauthorized;
        }

        public string IpAddress { get; set; }

        private string port;
        public string Port
        {
            get => port;
            set
            {
                port = value;
                UpdateStatus();
            }
        }
        public ServiceType MdnsType { get; set; }
        public string PairingCode { get; set; }

        public string PairingAddress => $"{IpAddress}:{Port}";
    }

    public class PairingService : ServiceDevice
    {
        public PairingService(string id, string ipAddress, string port) : base(id, ipAddress, port)
        { }
    }

    public class ConnectService : ServiceDevice
    {
        public ConnectService(string id, string ipAddress, string port) : base(id, ipAddress, port)
        { }
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

        public override string Tooltip => $"mDNS Service - {(((ServiceDevice)Device).MdnsType is ServiceDevice.ServiceType.QrCode ? "QR Pairing" : "Ready To Pair")}";
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
            return x.ID == y.ID && !(string.IsNullOrEmpty(x.Port) ^ string.IsNullOrEmpty(y.Port));
        }

        public int GetHashCode([DisallowNull] ServiceDevice obj)
        {
            throw new NotImplementedException();
        }
    }
}
