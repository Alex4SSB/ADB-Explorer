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
        Title,
        Regular,
        Bootloader,
        Recovery,
        Sideload,
        SideloadAuto,
    }

    public RebootCommand(LogicalDeviceViewModel device, RebootType type)
        : base(() => type is not RebootType.Title,
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
        RebootType.Title => Strings.Resources.S_DEVICE_REBOOT_TITLE,
        RebootType.Regular => Strings.Resources.S_REBOOT_SYSTEM,
        RebootType.Bootloader => Strings.Resources.S_REBOOT_BOOTLOADER,
        RebootType.Recovery => Strings.Resources.S_RECOVERY_MODE,
        RebootType.Sideload => Strings.Resources.S_REBOOT_SIDELOAD,
        RebootType.SideloadAuto => Strings.Resources.S_REBOOT_SIDELOAD_AUTO,
        _ => throw new NotSupportedException(),
    };
}
