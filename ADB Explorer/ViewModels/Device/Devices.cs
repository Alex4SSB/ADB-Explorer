using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Resources;
using ADB_Explorer.Services;

namespace ADB_Explorer.ViewModels;

public class Devices : AbstractDevice
{
    #region Full properties

    private ObservableList<DeviceViewModel> uiDevices = new();
    public ObservableList<DeviceViewModel> UIList
    {
        get => uiDevices;
        private set => Set(ref uiDevices, value);
    }

    public DateTime LastUpdate { get; set; }

    public List<string> RootDevices { get; protected set; } = new();

    public NewDeviceViewModel CurrentNewDevice { get; set; }

    public string WsaPort { get; set; }

    #endregion

    #region Read only lists

    public IEnumerable<LogicalDeviceViewModel> LogicalDeviceViewModels => UIList?.OfType<LogicalDeviceViewModel>();
    public IEnumerable<ServiceDeviceViewModel> ServiceDeviceViewModels => UIList?.OfType<ServiceDeviceViewModel>();
    public IEnumerable<HistoryDeviceViewModel> HistoryDeviceViewModels => UIList?.OfType<HistoryDeviceViewModel>();

    #endregion

    #region Read only properties

    public LogicalDeviceViewModel Current => LogicalDeviceViewModels?.FirstOrDefault(device => device.IsOpen)
        ?? Data.RuntimeSettings.DeviceToOpen;

    public int Count => UIList.Count(d => d.DeviceExists);

    public ObservableProperty<string> ObservableCount = new();

    #endregion

    public Devices()
    {
        UIList.Add(new NewDeviceViewModel(new()));
        UIList.Add(new WsaPkgDeviceViewModel(new()));

        if (Data.Settings.SaveDevices)
            RetrieveHistoryDevices();

        UIList.CollectionChanged += UIList_CollectionChanged;
        PropertyChanged += Devices_PropertyChanged;

        ObservableCount.Value = "0";
    }

    private void Devices_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Count))
            ObservableCount.Value = Count.ToString();
    }

    private void UIList_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(Count));
    }

    #region History device handling

    public void RetrieveHistoryDevices() => RetrieveHistoryDevices(UIList);

    public static void RetrieveHistoryDevices(ObservableList<DeviceViewModel> uiList)
    {
        var value = Storage.RetrieveValue(Strings.S_SAVED_DEVICES);
        if (value is null)
            return;

        var jArray = value.ToString();
        bool legacy = jArray.Contains(typeof(HistoryDevice).FullName);
        var historyType = legacy ? typeof(List<HistoryDevice>) : typeof(List<StorageDevice>);

        var devices = JsonConvert.DeserializeObject(jArray, historyType);
        if (devices is null)
            return;

        var items = legacy ? ((List<HistoryDevice>)devices).Select(s => new HistoryDeviceViewModel(s)) : ((List<StorageDevice>)devices).Select(HistoryDeviceViewModel.New);
        uiList.AddRange(items);
    }

    public void StoreHistoryDevices() => StoreHistoryDevices(UIList.OfType<HistoryDeviceViewModel>());

    public static void StoreHistoryDevices(IEnumerable<HistoryDeviceViewModel> devices)
    {
        Storage.StoreValue(Strings.S_SAVED_DEVICES, devices.Select(h => h.GetStorage()));
    }

    public void AddHistoryDevice(HistoryDeviceViewModel device)
    {
        UIList.Add(device);
        StoreHistoryDevices();
    }

    public void RemoveHistoryDevice(HistoryDeviceViewModel device)
    {
        UIList.Remove(device);
        StoreHistoryDevices();
    }

    #endregion

    #region Service device handling

    public void UpdateServices(IEnumerable<ServiceDeviceViewModel> other) => UpdateServices(UIList, other);

    public static void UpdateServices(ObservableList<DeviceViewModel> self, IEnumerable<ServiceDeviceViewModel> other)
    {
        self.RemoveAll(thisDevice => thisDevice is ServiceDeviceViewModel && !other.Any(otherDevice => otherDevice.ID == thisDevice.ID));

        foreach (var item in other)
        {
            if (self?.Find(thisDevice => thisDevice.ID == item.ID) is ServiceDeviceViewModel service)
            {
                service.UpdateService(item);
            }
            else
            {
                self.Add(item);
            }
        }
    }

    public bool ServicesChanged(IEnumerable<ServiceDeviceViewModel> other)
    {
        // if the list is null, we're probably not ready to update
        if (other is null)
            return false;

        // if the list is empty, we need to update (and remove all items)
        if (!other.Any())
            return ServiceDeviceViewModels.Any();

        var pairing = other.OfType<PairingServiceViewModel>();
        // if there's any service whose ID is not found in any logical device,
        // AND an ordering of both new and old lists doesn't match up all IDs
        return pairing.Any(service => !LogicalDeviceViewModels.Any(device => device.Status == DeviceStatus.Ok && device.BaseID == service.ID || device.IpAddress == service.IpAddress))
               && !ServiceDeviceViewModels.OrderBy(thisDevice => thisDevice.ID).SequenceEqual(pairing.OrderBy(otherDevice => otherDevice.ID), new ServiceDeviceViewModelEqualityComparer());
    }

    #endregion

    #region Logical device handling

    public bool UpdateDevices(IEnumerable<LogicalDeviceViewModel> other)
    {
        var result = UpdateDevices(UIList, other);
        OnPropertyChanged(nameof(Count));

        UpdateLogicalIp();
        UpdateHistoryNames();

        return result;
    }

    private static bool UpdateDevices(ObservableList<DeviceViewModel> self, IEnumerable<LogicalDeviceViewModel> other)
    {
        bool isCurrentTypeUpdated = false;

        // First remove all devices that no longer exist
        var devicesToRemove = self.Where(thisDevice => thisDevice is LogicalDeviceViewModel && !other.Any(otherDevice => otherDevice.ID == thisDevice.ID));
        foreach (var item in devicesToRemove)
        {
            // Set status as offline for file op mechanism
            item.SetStatus(DeviceStatus.Offline);
        }
        self.RemoveAll(devicesToRemove);

        // Then update existing devices' statuses and names
        foreach (var item in other)
        {
            if (self?.Find(thisDevice => thisDevice.ID == item.ID) is LogicalDeviceViewModel device)
            {
                // Return (at the end of the function) true if current device status has changed
                if (device is not null && device.IsOpen && device.Status != item.Status)
                    isCurrentTypeUpdated = true;

                device.UpdateDevice(item);

                if (device.Drives.Count != item.Drives.Count)
                    device.UpdateDrives(item, App.Current.Dispatcher, true);
            }
            else
            {
                // And add the new devices
                self.Add(item);
                if (item.Status is DeviceStatus.Ok)
                    Task.Run(() => ShellCommands.FindCommands(item.ID));
            }
        }

        return isCurrentTypeUpdated;
    }

    public bool DevicesChanged(IEnumerable<LogicalDeviceViewModel> other)
    {
        return other is not null
            && !LogicalDeviceViewModels.OrderBy(thisDevice => thisDevice.ID).SequenceEqual(
                other.OrderBy(otherDevice => otherDevice.ID), new DeviceViewModelEqualityComparer());
    }

    #endregion

    #region General device handling

    public void UpdateDeviceRoot(string deviceID, bool isRootStateKnown)
    {
        if (isRootStateKnown)
        {
            if (!RootDevices.Contains(deviceID))
                RootDevices.Add(deviceID);
        }
        else
        {
            RootDevices.Remove(deviceID);
        }
    }

    public void UpdateHistoryNames()
    {
        if (UpdateHistoryNames(UIList))
            OnPropertyChanged(nameof(UIList));
    }

    public static bool UpdateHistoryNames(ObservableList<DeviceViewModel> devices)
    {
        var result = false;
        foreach (var item in devices.OfType<HistoryDeviceViewModel>().Where(d => string.IsNullOrEmpty(d.DeviceName)))
        {
            var logical = devices.OfType<LogicalDeviceViewModel>().Where(l => l.Type is DeviceType.Remote or DeviceType.Service
                                                                       && l.IpAddress == item.IpAddress);
            if (logical.Any())
            {
                item.SetDeviceName(logical.First().Name);
                StoreHistoryDevices(devices.OfType<HistoryDeviceViewModel>());

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

    public static async Task<bool> UpdateLogicalIp(ObservableList<DeviceViewModel> devices)
    {
        var result = false;
        var items = devices.OfType<LogicalDeviceViewModel>().Where(d => d.Type is DeviceType.Service or DeviceType.Local && !d.IsIpAddressValid).ToList();
        foreach (var item in items)
        {
            if (item.Type is DeviceType.Service)
            {
                var service = devices.Where(d => d is ServiceDeviceViewModel srv && (srv.ID == item.ID || srv.ID == item.BaseID) && srv.IsIpAddressValid);
                if (service.Any())
                {
                    item.SetIpAddress(service.First().IpAddress);
                    continue;
                }
            }

            await Task.Run(() => result |= ADBService.AdbDevice.GetDeviceIp(item));
        }

        return result;
    }

    public bool DevicesAvailable(bool current = false) => AvailableDevices(current).Any();

    private IEnumerable<LogicalDeviceViewModel> AvailableDevices(bool current = false)
    {
        return LogicalDeviceViewModels.Where(
                device => (!current || device.IsOpen)
                && device.Status is DeviceStatus.Ok);
    }

    public bool SetOpenDevice(string selectedId)
        => SetOpenDevice(AvailableDevices().FirstOrDefault(device => device.ID == selectedId));

    public bool SetOpenDevice(LogicalDeviceViewModel device)
    {
        if (Data.RuntimeSettings.DeviceToOpen is null && device is null)
            return false;

        if (Data.RuntimeSettings.DeviceToOpen?.Equals(device) is not true)
            Data.RuntimeSettings.DeviceToOpen = device;

        Data.RuntimeSettings.IsRootActive = device?.Root is RootStatus.Enabled;

        return device is not null;
    }

    #endregion
}
