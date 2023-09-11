using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Resources;

namespace ADB_Explorer.ViewModels;

public class WsaPkgDeviceViewModel : DeviceViewModel
{
    private WsaPkgDevice device;
    protected new WsaPkgDevice Device
    {
        get => device;
        set => Set(ref device, value);
    }

    public DateTime LastLaunch => Device.LastLaunch;

    public DeviceAction LaunchWsaCommand { get; }

    public override string Tooltip => Strings.S_WSA_PKG_TIP;

    public override bool DeviceExists => false;

    public WsaPkgDeviceViewModel(WsaPkgDevice device) : base(device)
    {
        Device = device;

        LaunchWsaCommand = DeviceHelper.LaunchWsa(this);
    }

    public void SetLastLaunch(DateTime? newDate = null)
    {
        Device.LastLaunch = newDate is null ? DateTime.Now : newDate.Value;
        OnPropertyChanged(nameof(LastLaunch));
    }

}
