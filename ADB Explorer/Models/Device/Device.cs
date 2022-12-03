using ADB_Explorer.Helpers;
using ADB_Explorer.Resources;
using ADB_Explorer.Services;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Models;

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

public class LogicalDevice : Device
{
    private string name;
    public string Name
    {
        get => Type == DeviceType.Emulator ? ID : name;
        private set => Set(ref name, value);
    }

    private ObservableList<DriveViewModel> drives = new();
    public ObservableList<DriveViewModel> Drives
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

        Drives.Add(new LogicalDriveViewModel(new(path: AdbExplorerConst.DRIVE_TYPES.First(d => d.Value is AbstractDrive.DriveType.Root).Key)));
        Drives.Add(new LogicalDriveViewModel(new(path: AdbExplorerConst.DRIVE_TYPES.First(d => d.Value is AbstractDrive.DriveType.Internal).Key)));

        Drives.Add(new VirtualDriveViewModel(new(path: AdbExplorerConst.RECYCLE_PATH)));
        Drives.Add(new VirtualDriveViewModel(new(path: AdbExplorerConst.TEMP_PATH)));
        Drives.Add(new VirtualDriveViewModel(new(path: AdbExplorerConst.PACKAGE_PATH)));
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
    /// Update <see cref="Device"/> with new drives
    /// </summary>
    /// <param name="drives">The new drives to be assigned</param>
    /// <param name="asyncClasify"><see langword="true"/> to update only after fully acquiring all information</param>
    public async Task<bool> UpdateDrives(IEnumerable<Drive> drives, Dispatcher dispatcher, bool asyncClasify = false)
    {
        var collectionChanged = false;

        // MMC and OTG drives are searched for and only then UI is updated with all changes
        if (asyncClasify)
        {
            collectionChanged = await UpdateExtensionDrivesAsync(drives, dispatcher);
        }
        // All drives are first updated in UI, and only then MMC and OTG drives are searched for
        else
        {
            collectionChanged = SetDrives(drives);
            UpdateExtensionDrives(drives, dispatcher);
        }

        return collectionChanged;
    }

    private void UpdateExtensionDrives(IEnumerable<Drive> drives, Dispatcher dispatcher)
    {
        var mmcTask = Task.Run(() => GetMmcDrive(drives.OfType<LogicalDrive>(), ID));
        mmcTask.ContinueWith((t) =>
        {
            if (t.IsCanceled)
                return;

            dispatcher.BeginInvoke(() =>
            {
                SetMmcDrive(t.Result);
                SetExternalDrives();
            });
        });
    }

    private async Task<bool> UpdateExtensionDrivesAsync(IEnumerable<Drive> drives, Dispatcher dispatcher)
    {
        await Task.Run(() =>
        {
            if (GetMmcDrive(drives.OfType<LogicalDrive>(), ID) is LogicalDrive mmc)
                mmc.Type = AbstractDrive.DriveType.Expansion;

            SetExternalDrives(drives.OfType<LogicalDrive>());
        });

        var result = false;
        await dispatcher.BeginInvoke(() => result = SetDrives(drives));

        return result;
    }

    /// <summary>
    /// Update drive parameters, add new drives, remove non-existent drives
    /// </summary>
    /// <param name="drives"></param>
    /// <returns><see langword="true"/> if drives have been added or removed</returns>
    private bool SetDrives(IEnumerable<Drive> drives)
    {
        if (drives is null)
            return false;

        bool added = false;

        foreach (var other in drives)
        {
            // Accommodate for changing the path to /sdcard
            var selfQ = Drives.Where(d => d.Path == other.Path || (other.Type is AbstractDrive.DriveType.Internal && d.Type is AbstractDrive.DriveType.Internal));
            if (selfQ.Any())
            {
                // Update the drive if it exists
                var self = selfQ.First();

                switch (self)
                {
                    case LogicalDriveViewModel logical:
                        logical.SetParams((LogicalDrive)other);
                        if (other.Type is not AbstractDrive.DriveType.Unknown)
                            logical.SetType(other.Type);
                        break;
                    case VirtualDriveViewModel virt:
                        virt.SetItemsCount(((VirtualDrive)other).ItemsCount);
                        break;
                    default:
                        throw new NotImplementedException();
                }
            }
            // Create a new drive if it doesn't exist
            else if (other is LogicalDrive logical)
            {
                Drives.Add(new LogicalDriveViewModel(logical));
                added = true;
            }
            else
                throw new NotImplementedException();
        }

        // Remove all drives that were not discovered in the last update
        var removed = Drives.RemoveAll(self => self is LogicalDriveViewModel
                                               && !drives.Any(other => other.Path == self.Path
                                                    || (other.Type is AbstractDrive.DriveType.Internal && self.Type is AbstractDrive.DriveType.Internal)));

        return added || removed;
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

    public void SetMmcDrive(LogicalDrive mmcDrive) => ((LogicalDriveViewModel)Drives.Where(d => d.Path == mmcDrive.Path).FirstOrDefault())?.SetExtension();

    /// <summary>
    /// Sets type of all <see cref="DriveViewModel"/> with unknown type as external. Changes the local property.
    /// </summary>
    public void SetExternalDrives()
    {
        if (drives is null)
            return;

        foreach (var item in Drives.Where(d => d.Type == AbstractDrive.DriveType.Unknown))
        {
            ((LogicalDriveViewModel)item).SetExtension(false);
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
