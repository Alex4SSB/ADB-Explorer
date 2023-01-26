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

    public override string Tooltip => "Saved Device";

    #endregion

    public DeviceAction RemoveCommand { get; }

    public HistoryDeviceViewModel(HistoryDevice device) : base(device)
    {
        Device = device;

        RemoveCommand = DeviceHelper.RemoveDeviceCommand(this);
    }

    public static HistoryDeviceViewModel New(StorageDevice device) => new(new HistoryDevice()
    {
        DeviceName = device.DeviceName,
        IpAddress = device.IpAddress,
        ConnectPort = device.ConnectPort
    });

    public StorageDevice GetStorage() => new(this);

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

public class StorageDevice
{
    public string DeviceName { get; private set; }
    public string IpAddress { get; private set; }
    public string ConnectPort { get; private set; }

    public StorageDevice(HistoryDeviceViewModel device)
    {
        DeviceName = device.DeviceName;
        IpAddress = device.IpAddress;
        ConnectPort = device.ConnectPort;
    }
}
