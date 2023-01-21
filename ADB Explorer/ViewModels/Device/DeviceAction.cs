using ADB_Explorer.Helpers;
using ADB_Explorer.Services;

namespace ADB_Explorer.ViewModels;

public class DeviceAction : BaseAction
{
    public string Description { get; }

    public DeviceAction(Func<bool> canExecute, Action action, string description = null)
        : base(canExecute, action)
    {
        Description = description;
    }

    public override string ToString()
    {
        return Description;
    }
}

public class RebootCommand : DeviceAction
{
    public enum RebootType
    {
        Regular,
        Bootloader,
        Recovery,
        Sideload,
        SideloadAuto,
    }

    public RebootCommand(LogicalDeviceViewModel device, RebootType type)
        : base(() => true,
            () => Task.Run(() => ADBService.AdbDevice.Reboot(device.ID, RebootParam(type))),
            RebootString(type))
    { }

    private static string RebootParam(RebootType type) => type switch
    {
        RebootType.Regular => "",
        RebootType.Bootloader => "bootloader",
        RebootType.Recovery => "recovery",
        RebootType.Sideload => "sideload",
        RebootType.SideloadAuto => "sideload-auto-reboot",
        _ => throw new NotSupportedException(),
    };

    private static string RebootString(RebootType type) => type switch
    {
        RebootType.Regular => "System Image",
        RebootType.Bootloader => "Bootloader",
        RebootType.Recovery => "Recovery",
        RebootType.Sideload => "Sideload",
        RebootType.SideloadAuto => "Sideload (Auto Reboot)",
        _ => throw new NotSupportedException(),
    };
}
