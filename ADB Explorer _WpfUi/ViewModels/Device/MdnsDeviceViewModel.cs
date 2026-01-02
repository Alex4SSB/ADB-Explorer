using ADB_Explorer.Helpers;

namespace ADB_Explorer.ViewModels;

public class MdnsDeviceViewModel : DeviceViewModel
{
    public MdnsDeviceViewModel() : base(null)
    {
        AdbHelper.EnableMdns();
    }
}
