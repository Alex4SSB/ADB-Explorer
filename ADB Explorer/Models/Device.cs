﻿using ADB_Explorer.Helpers;
using ADB_Explorer.Resources;
using ADB_Explorer.Services;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;

namespace ADB_Explorer.Models
{
    public abstract class AbstractDevice : INotifyPropertyChanged
    {
        public enum DeviceType
        {
            Service,
            Local,
            Remote,
            Sideload,
            Emulator,
            History,
            New,
        }

        public enum DeviceStatus
        {
            Ok, // online \ does not require attention
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
            private set
            {
                if (Set(ref uiDevices, value))
                    OnPropertyChanged(nameof(Count));
            }
        }

        public IEnumerable<UILogicalDevice> UILogicalDevices => UIList?.OfType<UILogicalDevice>();
        public IEnumerable<UIServiceDevice> UIServiceDevices => UIList?.OfType<UIServiceDevice>();
        public IEnumerable<UIHistoryDevice> UIHistoryDevices => UIList?.OfType<UIHistoryDevice>();

        public IEnumerable<LogicalDevice> LogicalDevices => UILogicalDevices.Select(d => d.Device as LogicalDevice);
        public IEnumerable<ServiceDevice> ServiceDevices => UIServiceDevices.Select(d => d.Device as ServiceDevice);
        public IEnumerable<HistoryDevice> HistoryDevices => UIHistoryDevices.Select(d => d.Device as HistoryDevice);

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

        public UINewDevice NewDevice => UIList.OfType<UINewDevice>().First();

        public DateTime LastUpdate { get; set; }

        public int Count => UIList.Count(d => d.Device.Type is not DeviceType.New and not DeviceType.History
            && d.Device.Status is not DeviceStatus.Offline);

        public string AppTitle
        {
            get
            {
                if (Count < 1)
                    return $"{Properties.Resources.AppDisplayName}{Strings.S_NO_DEVICES_TITLE}";
                else
                {
                    if (CurrentDevice)
                        return $"{Properties.Resources.AppDisplayName} - {CurrentDevice.Name}";
                    else
                        return Properties.Resources.AppDisplayName;
                }
            }
        }

        public void RetrieveHistoryDevices() => RetrieveHistoryDevices(UIList);

        public static void RetrieveHistoryDevices(ObservableList<UIDevice> uiList)
        {
            List<HistoryDevice> historyDevices = new();

            if (Storage.RetrieveValue(Strings.S_SAVED_DEVICES) is var value and not null
                        && JsonConvert.DeserializeObject(value.ToString(), typeof(List<HistoryDevice>)) is var devices and not null)
            {
                historyDevices.AddRange((List<HistoryDevice>)devices);
            }

            uiList.AddRange(historyDevices?.Select(s => new UIHistoryDevice(s)));
        }

        public void StoreHistoryDevices() => StoreHistoryDevices(HistoryDevices);

        public static void StoreHistoryDevices(IEnumerable<HistoryDevice> devices)
        {
            Storage.StoreValue(Strings.S_SAVED_DEVICES, devices.ToList());
        }

        public void AddHistoryDevice(HistoryDevice device)
        {
            UIList.Add(new UIHistoryDevice(device));
            StoreHistoryDevices();
        }

        public void RemoveHistoryDevice(UIHistoryDevice device)
        {
            UIList.Remove(device);
            StoreHistoryDevices();
        }

        public Devices()
        {
            UIList.Add(new UINewDevice());
            if (Data.Settings.SaveDevices)
                RetrieveHistoryDevices();

            UIList.CollectionChanged += UIList_CollectionChanged;
        }

        private void UIList_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(Count));
            OnPropertyChanged(nameof(AppTitle));
        }

        public void SetOpen(UILogicalDevice device, bool openState = true)
        {
            device.SetOpen(UILogicalDevices.ToList(), openState);
            OnPropertyChanged(nameof(AppTitle));
        }

        public void CloseAll()
        {
            UILogicalDevice.SetOpen(UILogicalDevices.ToList());
            OnPropertyChanged(nameof(AppTitle));
        }

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
                    self.Add(new UIServiceDevice(item));
                }
            }
        }

        public bool UpdateDevices(IEnumerable<LogicalDevice> other)
        {
            var result = UpdateDevices(UIList, other);
            OnPropertyChanged(nameof(Count));

            UpdateLogicalIp();
            UpdateHistoryNames();
            
            return result;
        }

        private static bool UpdateDevices(ObservableList<UIDevice> self, IEnumerable<LogicalDevice> other)
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

            return isCurrentTypeUpdated;
        }

        public void UpdateHistoryNames()
        {
            if (UpdateHistoryNames(UIList))
                OnPropertyChanged(nameof(UIList));
        }

        public static bool UpdateHistoryNames(ObservableList<UIDevice> devices)
        {
            var result = false;
            foreach (var item in devices.OfType<UIHistoryDevice>().Where(d => string.IsNullOrEmpty(((HistoryDevice)d.Device).DeviceName)))
            {
                var logical = devices.OfType<UILogicalDevice>().Where(l => ((LogicalDevice)l.Device).Type is DeviceType.Remote or DeviceType.Service
                                                                           && ((LogicalDevice)l.Device).IpAddress == ((HistoryDevice)item.Device).IpAddress);
                if (logical.Any())
                {
                    ((HistoryDevice)item.Device).DeviceName = logical.First().Name;
                    StoreHistoryDevices(devices.OfType<UIHistoryDevice>().Select(d => (HistoryDevice)d.Device));

                    result = true;
                }
            }

            return result;
        }

        public async void UpdateLogicalIp()
        {
            if (await UpdateLogicalIp(UIList))
                OnPropertyChanged(nameof(UIList));
        }

        public static async Task<bool> UpdateLogicalIp(ObservableList<UIDevice> devices)
        {
            var result = false;
            foreach (var item in devices.Where(d => d.Device.Type is DeviceType.Service or DeviceType.Local && string.IsNullOrEmpty(((LogicalDevice)d.Device).IpAddress)))
            {
                result = true;

                if (item.Device.Type is DeviceType.Service)
                {
                    var service = devices.Where(d => d.Device is ServiceDevice srv && (srv.ID == item.Device.ID || srv.ID == ((LogicalDevice)item.Device).BaseID) && !string.IsNullOrEmpty(srv.IpAddress));
                    if (service.Any())
                    {
                        ((LogicalDevice)item.Device).IpAddress = ((ServiceDevice)service.First().Device).IpAddress;
                        continue;
                    }
                }
                
                
                await Task.Run(() => ADBService.AdbDevice.GetDeviceIp((LogicalDevice)item.Device));
            }

            return result;
        }

        private IEnumerable<UILogicalDevice> AvailableDevices(bool current = false)
        {
            return UILogicalDevices.Where(
                    device => (!current || device.IsOpen)
                    && device.Device.Status is DeviceStatus.Ok);
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
                OnPropertyChanged(nameof(AppTitle));
                return true;
            }
            else
                return false;
        }

        public bool ServicesChanged(IEnumerable<ServiceDevice> other)
        {
            // if the list is null, we're probably not ready to update
            if (other is null)
                return false;

            // if the list is empty, we need to update (and remove all items)
            if (!other.Any())
                return ServiceDevices.Any();

            var pairing = other.OfType<PairingService>();
            // if there's any service whose ID is not found in any logical device,
            // AND an ordering of both new and old lists doesn't match up all IDs
            return pairing.Any(service => !LogicalDevices.Any(device => device.Status == DeviceStatus.Ok && device.BaseID == service.ID || device.IpAddress == service.IpAddress))
                   && !ServiceDevices.OrderBy(thisDevice => thisDevice.ID).SequenceEqual(pairing.OrderBy(otherDevice => otherDevice.ID), new ServiceDeviceEqualityComparer());
        }

        public bool DevicesChanged(IEnumerable<LogicalDevice> other)
        {
            return other is not null
                && !LogicalDevices.OrderBy(thisDevice => thisDevice.ID).SequenceEqual(
                    other.OrderBy(otherDevice => otherDevice.ID), new LogicalDeviceEqualityComparer());
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

    public abstract class NetworkDevice : Device
    {
        private string ipAddress;
        public string IpAddress
        {
            get => ipAddress;
            set
            {
                if (Set(ref ipAddress, value))
                    OnPropertyChanged(nameof(IsIpAddressValid));
            }
        }

        public bool IsIpAddressValid => !string.IsNullOrWhiteSpace(IpAddress)
                                        && IpAddress.Count(c => c == '.') == 3
                                        && IpAddress.Split('.').Count(i => byte.TryParse(i, out _)) == 4;

        private string pairingPort;
        public virtual string PairingPort
        {
            get => pairingPort;
            set
            {
                if (Set(ref pairingPort, value))
                    OnPropertyChanged(nameof(IsPairingPortValid));
            }
        }

        public bool IsPairingPortValid => !string.IsNullOrWhiteSpace(PairingPort)
                                          && ushort.TryParse(PairingPort, out ushort res)
                                          && res > 0;

        private string pairingCode;
        public string PairingCode
        {
            get => pairingCode;
            set
            {
                if (Set(ref pairingCode, value))
                    OnPropertyChanged(nameof(IsPairingCodeValid));
            }
        }

        public bool IsPairingCodeValid => !string.IsNullOrWhiteSpace(PairingCode) && PairingCode.Length == 6;

        public string PairingAddress => $"{IpAddress}:{PairingPort}";

    }

    public abstract class UIDevice : AbstractDevice
    {
        public Device Device { get; protected set; }

        private bool deviceSelected;
        public bool DeviceSelected
        {
            get => deviceSelected;
            set => Set(ref deviceSelected, value);
        }

        public virtual string Tooltip { get; }

        public string TypeIcon => Device.Type switch
        {
            DeviceType.Local => "\uE839",
            DeviceType.Remote => "\uEE77",
            DeviceType.Emulator => "\uE99A",
            DeviceType.Service when Device is ServiceDevice service && service.MdnsType is ServiceDevice.ServiceType.QrCode => "\uED14",
            DeviceType.Service => "\uEDE4",
            DeviceType.Sideload => "\uED10",
            DeviceType.New => "\uE710",
            DeviceType.History => "\uE823",
            _ => throw new NotImplementedException(),
        };

        public string StatusIcon => Device.Status switch
        {
            DeviceStatus.Ok => "",
            DeviceStatus.Offline => "\uEBFF",
            DeviceStatus.Unauthorized => "\uEC00",
            _ => throw new NotImplementedException(),
        };

        public static implicit operator bool(UIDevice obj) => obj?.Device;

        protected UIDevice()
        {
            Data.RuntimeSettings.PropertyChanged += RuntimeSettings_PropertyChanged;
        }

        private void RuntimeSettings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AppRuntimeSettings.CollapseDevices) && Data.RuntimeSettings.CollapseDevices)
            {
                DeviceSelected = false;
            }
        }
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

        public string LogicalID => Type == DeviceType.Service && ID.Count(c => c == '-') > 1 ? ID.Split('-')[1] : ID;

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

        private string ipAddress;
        public string IpAddress
        {
            get => ipAddress;
            set => Set(ref ipAddress, value);
        }

        private LogicalDevice(string name, string id)
        {
            Name = name;
            ID = id;
            Battery = new Battery();
        }

        public static LogicalDevice New(string name, string id, string status)
        {
            var deviceType = GetType(id, status);
            var deviceStatus = GetStatus(status);
            var ip = deviceType is DeviceType.Remote ? id.Split(':')[0] : "";
            var rootStatus = deviceType is DeviceType.Sideload
                ? RootStatus.Enabled
                : RootStatus.Unchecked;

            if (!AdbExplorerConst.DISPLAY_OFFLINE_SERVICES && deviceType is DeviceType.Service && deviceStatus is DeviceStatus.Offline)
                return null;

            return new LogicalDevice(name, id) { Type = deviceType, Status = deviceStatus, Root = rootStatus, IpAddress = ip };
        }

        private static DeviceStatus GetStatus(string status)
        {
            return status switch
            {
                "device" or "recovery" => DeviceStatus.Ok,
                "offline" => DeviceStatus.Offline,
                "unauthorized" or "authorizing" => DeviceStatus.Unauthorized,
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
                    if (t.IsCanceled)
                        return;

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
                    if (t.IsCanceled)
                        return;

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
            if (drives is null || !DrivesChanged(drives.Where(d => d.Type is not DriveType.Trash and not DriveType.Temp and not DriveType.Package)))
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
            if (drives is null)
                return null;

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
            if (drives is null)
                return;

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
            Battery.Update(ADBService.AdbDevice.GetBatteryInfo(this));
        }

        public override string ToString() => Name;
    }

    public class UILogicalDevice : UIDevice
    {
        private bool isOpen = false;
        public bool IsOpen
        {
            get => isOpen;
            private set => Set(ref isOpen, value);
        }

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
                    DeviceStatus.Ok => "",
                    DeviceStatus.Offline => " - Offline",
                    DeviceStatus.Unauthorized => " - Unauthorized",
                    _ => throw new NotImplementedException(),
                };

                return result;
            }
        }

        public BrowseCommand BrowseCommand { get; private set; }
        public RemoveCommand RemoveCommand { get; private set; }
        public ToggleRootCommand ToggleRootCommand { get; private set; }
        public RebootCommand RebootCommand { get; private set; }
        public BootloaderCommand BootloaderCommand { get; private set; }
        public RecoveryCommand RecoveryCommand { get; private set; }
        public SideloadCommand SideloadCommand { get; private set; }
        public SideloadAutoCommand SideloadAutoCommand { get; private set; }

        public UILogicalDevice(LogicalDevice device)
        {
            Device = device;

            BrowseCommand = new(this);
            RemoveCommand = new(this);
            ToggleRootCommand = new(this);
            RebootCommand = new(this);
            BootloaderCommand = new(this);
            RecoveryCommand = new(this);
            SideloadCommand = new(this);
            SideloadAutoCommand = new(this);

            Data.RuntimeSettings.PropertyChanged += RuntimeSettings_PropertyChanged;
        }

        private void RuntimeSettings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AppRuntimeSettings.UpdateCurrentDevice) && Data.RuntimeSettings.UpdateCurrentDevice)
            {
                if (Data.CurrentADBDevice is null || Data.CurrentADBDevice.ID != Device.ID)
                    IsOpen = false;
            }
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

    public abstract class ServiceDevice : NetworkDevice
    {
        public ServiceDevice()
        {
            Type = DeviceType.Service;
        }

        public ServiceDevice(string id, string ipAddress, string port = "") : this()
        {
            ID = id;
            IpAddress = ipAddress;
            PairingPort = port;
            UpdateStatus();
        }

        public enum ServiceType
        {
            QrCode,
            PairingCode
        }

        public override string PairingPort
        {
            get => base.PairingPort;
            set
            {
                if (base.PairingPort is not null && base.PairingPort.Equals(value))
                    return;

                base.PairingPort = value;
                UpdateStatus();
            }
        }

        public void UpdateService(ServiceDevice other)
        {
            PairingPort = other.PairingPort;
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            Status = MdnsType is ServiceType.QrCode ? DeviceStatus.Ok : DeviceStatus.Unauthorized;
        }

        private ServiceType mdnsType;
        public ServiceType MdnsType
        {
            get => mdnsType;
            set
            {
                if (Set(ref mdnsType, value))
                    UpdateStatus();
            }
        }
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

            PairCommand = new(this);
        }

        public PairCommand PairCommand { get; private set; }

        private string uiPairingCode;
        public string UIPairingCode
        {
            get => uiPairingCode;
            set
            {
                if (Set(ref uiPairingCode, value))
                    ((ServiceDevice)Device).PairingCode = uiPairingCode?.Replace("-", "");
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
            return x.ID == y.ID && !(string.IsNullOrEmpty(x.PairingPort) ^ string.IsNullOrEmpty(y.PairingPort));
        }

        public int GetHashCode([DisallowNull] ServiceDevice obj)
        {
            throw new NotImplementedException();
        }
    }

    public abstract class DeviceAction : INotifyPropertyChanged
    {
        protected UIDevice device;

        public virtual bool IsEnabled { get; } = true;

        protected DeviceAction(UIDevice device)
        {
            this.device = device;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected bool Set<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(storage, value))
                return false;

            storage = value;
            OnPropertyChanged(propertyName);

            return true;
        }
        protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public class BrowseCommand : DeviceAction
    {
        public override bool IsEnabled => !((UILogicalDevice)device).IsOpen
            && device.Device.Status
            is AbstractDevice.DeviceStatus.Ok;

        public BrowseCommand(UILogicalDevice device) : base(device)
        { }

        public void Action()
        {
            Data.CurrentADBDevice = new(device);
            Data.RuntimeSettings.UpdateCurrentDevice = true;
        }

        private ICommand command;
        public ICommand Command => command ??= new CommandHandler(() => Action(), () => IsEnabled);
    }

    public class RemoveCommand : DeviceAction
    {
        public override bool IsEnabled
        {
            get
            {
                return device.Device.Type is AbstractDevice.DeviceType.History
                        || (!Data.RuntimeSettings.IsManualPairingInProgress
                            && device.Device.Type is AbstractDevice.DeviceType.Remote
                                                  or AbstractDevice.DeviceType.Emulator);
            }
        }

        public string RemoveAction => device.Device.Type switch
        {
            AbstractDevice.DeviceType.Remote => Strings.S_REM_DEV,
            AbstractDevice.DeviceType.Emulator => Strings.S_REM_EMU,
            AbstractDevice.DeviceType.History => Strings.S_REM_HIST_DEV,
            _ => "",
        };

        public RemoveCommand(UILogicalDevice device) : base(device)
        { }

        public RemoveCommand(UIHistoryDevice device) : base(device)
        { }

        public async void Action()
        {
            var dialogTask = await DialogService.ShowConfirmation(Strings.S_REM_DEVICE(device.Device), Strings.S_REM_DEVICE_TITLE(device.Device));
            if (dialogTask.Item1 is not ModernWpf.Controls.ContentDialogResult.Primary)
                return;

            if (device.Device.Type is AbstractDevice.DeviceType.Emulator)
            {
                try
                {
                    ADBService.KillEmulator(device.Device.ID);
                }
                catch (Exception ex)
                {
                    DialogService.ShowMessage(ex.Message, Strings.S_DISCONN_FAILED_TITLE, DialogService.DialogIcon.Critical);
                    return;
                }
            }
            else if (device.Device.Type is AbstractDevice.DeviceType.Remote)
            {
                try
                {
                    ADBService.DisconnectNetworkDevice(device.Device.ID);
                }
                catch (Exception ex)
                {
                    DialogService.ShowMessage(ex.Message, Strings.S_DISCONN_FAILED_TITLE, DialogService.DialogIcon.Critical);
                    return;
                }
            }
            else if (device.Device.Type is AbstractDevice.DeviceType.History)
            { } // No additional action is required
            else
            {
                throw new NotImplementedException();
            }

            Data.RuntimeSettings.DeviceToRemove = device;
        }

        private ICommand command;
        public ICommand Command => command ??= new CommandHandler(() => Action(), () => IsEnabled);
    }

    public class PairCommand : DeviceAction
    {
        public override bool IsEnabled => ((NetworkDevice)device.Device).IsPairingCodeValid
            && device.Device.Status is AbstractDevice.DeviceStatus.Unauthorized;

        public PairCommand(UIServiceDevice device) : base(device)
        { }

        public void Action()
        {
            Data.RuntimeSettings.DeviceToPair = (ServiceDevice)device.Device;
        }

        private ICommand command;
        public ICommand Command => command ??= new CommandHandler(() => Action(), () => IsEnabled);
    }

    public class ToggleRootCommand : DeviceAction
    {
        public override bool IsEnabled => ((LogicalDevice)device.Device).Root is not AbstractDevice.RootStatus.Forbidden
            && device.Device.Status is AbstractDevice.DeviceStatus.Ok
            && device.Device.Type is not AbstractDevice.DeviceType.Sideload;

        public ToggleRootCommand(UILogicalDevice device) : base(device)
        { }

        public void Action()
        {
            var logical = (LogicalDevice)device.Device;
            bool rootEnabled = logical.Root is AbstractDevice.RootStatus.Enabled;

            var rootTask = Task.Run(() =>
            {
                logical.EnableRoot(!rootEnabled);
            });
            rootTask.ContinueWith((t) =>
            {
                if (logical.Root is AbstractDevice.RootStatus.Forbidden)
                    Data.RuntimeSettings.RootAttemptForbidden = true;
            });
        }

        private ICommand command;
        public ICommand Command => command ??= new CommandHandler(() => Action(), () => IsEnabled);
    }

    public class RebootCommand : DeviceAction
    {
        public RebootCommand(UILogicalDevice device) : base(device)
        { }

        public void Action() => Task.Run(() => ADBService.AdbDevice.Reboot((LogicalDevice)device.Device, ""));

        private ICommand command;
        public ICommand Command => command ??= new CommandHandler(() => Action(), () => IsEnabled);
    }

    public class BootloaderCommand : DeviceAction
    {
        public BootloaderCommand(UILogicalDevice device) : base(device)
        { }

        public void Action() => Task.Run(() => ADBService.AdbDevice.Reboot((LogicalDevice)device.Device, "bootloader"));

        private ICommand command;
        public ICommand Command => command ??= new CommandHandler(() => Action(), () => IsEnabled);
    }

    public class RecoveryCommand : DeviceAction
    {
        public RecoveryCommand(UILogicalDevice device) : base(device)
        { }

        public void Action() => Task.Run(() => ADBService.AdbDevice.Reboot((LogicalDevice)device.Device, "recovery"));

        private ICommand command;
        public ICommand Command => command ??= new CommandHandler(() => Action(), () => IsEnabled);
    }

    public class SideloadCommand : DeviceAction
    {
        public SideloadCommand(UILogicalDevice device) : base(device)
        { }

        public void Action() => Task.Run(() => ADBService.AdbDevice.Reboot((LogicalDevice)device.Device, "sideload"));

        private ICommand command;
        public ICommand Command => command ??= new CommandHandler(() => Action(), () => IsEnabled);
    }

    public class SideloadAutoCommand : DeviceAction
    {
        public SideloadAutoCommand(UILogicalDevice device) : base(device)
        { }

        public void Action() => Task.Run(() => ADBService.AdbDevice.Reboot((LogicalDevice)device.Device, "sideload-auto-reboot"));

        private ICommand command;
        public ICommand Command => command ??= new CommandHandler(() => Action(), () => IsEnabled);
    }

    public class NewDevice : NetworkDevice
    {
        private string connectPort;
        public string ConnectPort
        {
            get => connectPort;
            set
            {
                if (Set(ref connectPort, value))
                    OnPropertyChanged(nameof(IsConnectPortValid));
            }
        }

        public bool IsConnectPortValid => !string.IsNullOrWhiteSpace(ConnectPort)
                                          && ushort.TryParse(ConnectPort, out ushort res)
                                          && res > 0;

        public NewDevice()
        {
            Type = DeviceType.New;
            Status = DeviceStatus.Ok;
        }

        public string ConnectAddress => $"{IpAddress}:{ConnectPort}";
    }

    public class UINewDevice : UIDevice
    {
        public ConnectCommand ConnectCommand { get; private set; }

        public ClearCommand ClearCommand { get; private set; }

        private bool isPairingEnabled = false;
        public bool IsPairingEnabled
        {
            get => isPairingEnabled;
            set => Set(ref isPairingEnabled, value);
        }

        private string uiPairingCode;
        public string UIPairingCode
        {
            get => uiPairingCode;
            set
            {
                if (Set(ref uiPairingCode, value))
                    ((NewDevice)Device).PairingCode = uiPairingCode?.Replace("-", "");
            }
        }

        public UINewDevice()
        {
            Device = new NewDevice();

            ConnectCommand = new(this);
            ClearCommand = new(this);
        }

        public void ClearDevice()
        {
            var dev = Device as NewDevice;

            dev.IpAddress =
            dev.ConnectPort =
            dev.PairingPort =
            UIPairingCode = "";
            IsPairingEnabled = false;
        }

        public void EnablePairing()
        {
            var dev = Device as NewDevice;

            dev.PairingPort =
            UIPairingCode = "";
            IsPairingEnabled = true;
        }
    }

    public class ConnectCommand : DeviceAction
    {
        private NewDevice dev => device.Device as NewDevice;

        public override bool IsEnabled
        {
            get
            {
                if (!dev.IsIpAddressValid || !dev.IsConnectPortValid)
                    return false;

                if (device is UINewDevice
                    && ((UINewDevice)device).IsPairingEnabled
                    && (!dev.IsPairingCodeValid || !dev.IsPairingPortValid))
                    return false;

                return true;
            }
        }

        public ConnectCommand(UINewDevice device) : base(device)
        { }

        public ConnectCommand(UIHistoryDevice device) : base(device)
        { }

        public void Action()
        {
            Data.RuntimeSettings.ConnectNewDevice = device;
        }

        private ICommand command;
        public ICommand Command => command ??= new CommandHandler(() => Action(), () => IsEnabled);
    }

    public class ClearCommand : DeviceAction
    {
        private NewDevice dev => device.Device as NewDevice;

        public override bool IsEnabled
        {
            get
            {
                return !(string.IsNullOrEmpty(dev.IpAddress)
                        && string.IsNullOrEmpty(dev.ConnectPort)
                        && string.IsNullOrEmpty(dev.PairingPort)
                        && string.IsNullOrEmpty(dev.PairingCode));
            }
        }

        public ClearCommand(UINewDevice device) : base(device)
        { }

        public void Action()
        {
            ((UINewDevice)device).ClearDevice();
        }

        private ICommand command;
        public ICommand Command => command ??= new CommandHandler(() => Action(), () => IsEnabled);
    }

    public class HistoryDevice : NewDevice
    {
        private string deviceName = null;
        public string DeviceName
        {
            get => deviceName;
            set => Set(ref deviceName, value);
        }

        public HistoryDevice()
        {
            Type = DeviceType.History;
            Status = DeviceStatus.Ok;

        }

        public HistoryDevice(NewDevice device) : this()
        {
            IpAddress = device.IpAddress;
            ConnectPort = device.ConnectPort;
        }
    }

    public class UIHistoryDevice : UIDevice
    {
        public override string Tooltip => "Saved Device";

        public UIHistoryDevice(HistoryDevice device)
        {
            Device = device;

            ConnectCommand = new(this);
            RemoveCommand = new(this);
        }

        public ConnectCommand ConnectCommand { get; private set; }
        public RemoveCommand RemoveCommand { get; private set; }
    }
}
