using ADB_Explorer.Helpers;
using ADB_Explorer.Models;

namespace ADB_Explorer.ViewModels;

public class HistoryDeviceViewModel : NewDeviceViewModel
{
    private HistoryDevice device;
    protected new HistoryDevice Device
    {
        get => device;
        set => Set(ref device, value);
    }

    #region Read only properties

    public string DeviceName => Device.DeviceName;

    public override string Tooltip => Strings.Resources.S_DEVICE_SAVED;

    public override bool DeviceExists => false;

    public bool IsDeviceNameValid => !string.IsNullOrEmpty(DeviceName);

    #endregion

    public DeviceAction RemoveCommand { get; }

    public HistoryDeviceViewModel(HistoryDevice device) : base(device)
    {
        Device = device;

        RemoveCommand = DeviceHelper.RemoveDeviceCommand(this);
    }

    public static HistoryDeviceViewModel FromNewDevice(NewDeviceViewModel device) => new(new()
    {
        IpAddress = device.IsIpAddressValid ? device.IpAddress : null,
        HostName = device.IsIpAddressValid ? null : device.HostName,
        ConnectPort = device.ConnectPort
    });

    public static HistoryDeviceViewModel FromStorage(StorageDevice device)
    {
        HistoryDeviceViewModel historyDevice = new(new HistoryDevice()
        {
            DeviceName = device.DeviceName,
            IpAddress = device.IpAddress,
            ConnectPort = device.ConnectPort
        });

        if (!historyDevice.IsIpAddressValid)
        {
            historyDevice.HostName = historyDevice.IpAddress;
        }

        return historyDevice;
    }

    public StorageDevice GetStorage() => StorageDevice.FromVM(this);

    public bool SetDeviceName(string name)
    {
        if (DeviceName != name)
        {
            Device.DeviceName = name;

            return true;
        }

        return false;
    }
}

public record struct StorageDevice(string DeviceName, string IpAddress, string ConnectPort)
{
    public static StorageDevice FromVM(HistoryDeviceViewModel device) => new()
    {
        DeviceName = device.DeviceName,
        IpAddress = device.IsIpAddressValid ? device.IpAddress : device.HostName,
        ConnectPort = device.ConnectPort
    };
}
