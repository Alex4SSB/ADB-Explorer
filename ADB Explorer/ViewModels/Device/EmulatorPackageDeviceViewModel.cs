namespace ADB_Explorer.ViewModels;

public class EmulatorPackageDeviceViewModel : DeviceViewModel
{
    private EmulatorPackageDevice _device = null!;
    protected new EmulatorPackageDevice Device
    {
        get => _device;
        set => SetProperty(ref _device, value);
    }

    public string AvdName => Device.AvdName;

    public DateTime LastLaunch => Device.LastLaunch;

    public DeviceAction LaunchCommand { get; }

    public override string Tooltip => Strings.Resources.S_EMULATOR_PKG_TIP;

    public override bool DeviceExists => false;

    public EmulatorPackageDeviceViewModel(EmulatorPackageDevice device, Devices? devicesObject = null) : base(device, devicesObject)
    {
        Device = device;
        LaunchCommand = DeviceHelper.LaunchEmulator(this);
    }

    public void SetLastLaunch(DateTime? newDate = null)
    {
        Device.LastLaunch = newDate is null ? DateTime.Now : newDate.Value;
    }
}
