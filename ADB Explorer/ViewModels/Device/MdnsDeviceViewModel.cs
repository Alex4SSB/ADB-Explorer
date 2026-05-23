using ADB_Explorer.Models;
namespace ADB_Explorer.ViewModels;

public class MdnsDeviceViewModel : DeviceViewModel
{
    protected new Device Device { get; set; }

    public MdnsDeviceViewModel(MdnsDevice device, Devices devicesObject = null) : base(device, devicesObject)
    {
        Device = device;
    }
}
