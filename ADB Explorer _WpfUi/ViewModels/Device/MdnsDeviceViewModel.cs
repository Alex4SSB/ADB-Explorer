using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
namespace ADB_Explorer.ViewModels;

public class MdnsDeviceViewModel : DeviceViewModel
{
    protected new Device Device { get; set; }

    public MdnsDeviceViewModel(MdnsDevice device) : base(device)
    {
        Device = device;
    }
}

public class MdnsDevice : Device
{
    
}
