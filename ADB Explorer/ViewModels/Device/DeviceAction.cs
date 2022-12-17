using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Resources;
using ADB_Explorer.Services;

namespace ADB_Explorer.ViewModels;

public abstract class DeviceAction : ViewModelBase
{
    protected DeviceViewModel device;

    public virtual bool IsEnabled { get; } = true;

    protected DeviceAction(DeviceViewModel device)
    {
        this.device = device;
    }
}

public abstract class LogicalDeviceAction : DeviceAction
{
    protected new LogicalDeviceViewModel device;

    protected LogicalDeviceAction(LogicalDeviceViewModel device) : base(device)
    {
        this.device = device;
    }
}

public abstract class ServiceDeviceAction : DeviceAction
{
    protected new ServiceDeviceViewModel device;

    protected ServiceDeviceAction(ServiceDeviceViewModel device) : base(device)
    {
        this.device = device;
    }
}

public abstract class NewDeviceAction : DeviceAction
{
    protected new NewDeviceViewModel device;

    protected NewDeviceAction(NewDeviceViewModel device) : base(device)
    {
        this.device = device;
    }
}

public class BrowseCommand : LogicalDeviceAction
{
    public override bool IsEnabled => !((LogicalDeviceViewModel)device).IsOpen
        && device.Status is AbstractDevice.DeviceStatus.Ok;

    public BrowseCommand(LogicalDeviceViewModel device) : base(device)
    { }

    public void Action()
    {
        Data.CurrentADBDevice = new(device);
        Data.RuntimeSettings.DeviceToOpen = device;
    }

    private ICommand command;
    public ICommand Command => command ??= new CommandHandler(() => Action(), () => IsEnabled);
}

public class RemoveCommand : DeviceAction
{
    public override bool IsEnabled
    {
        get
        {
            return device.Type is AbstractDevice.DeviceType.History
                    || !Data.RuntimeSettings.IsManualPairingInProgress
                        && device.Type is AbstractDevice.DeviceType.Remote
                                       or AbstractDevice.DeviceType.Emulator;
        }
    }

    public string RemoveAction => device.Type switch
    {
        AbstractDevice.DeviceType.Remote => Strings.S_REM_DEV,
        AbstractDevice.DeviceType.Emulator => Strings.S_REM_EMU,
        AbstractDevice.DeviceType.History => Strings.S_REM_HIST_DEV,
        _ => "",
    };

    public RemoveCommand(LogicalDeviceViewModel device) : base(device)
    { }

    public RemoveCommand(HistoryDeviceViewModel device) : base(device)
    { }

    public async void Action()
    {
        var dialogTask = await DialogService.ShowConfirmation(Strings.S_REM_DEVICE(device), Strings.S_REM_DEVICE_TITLE(device));
        if (dialogTask.Item1 is not ContentDialogResult.Primary)
            return;

        if (device.Type is AbstractDevice.DeviceType.Emulator)
        {
            try
            {
                ADBService.KillEmulator(device.ID);
            }
            catch (Exception ex)
            {
                DialogService.ShowMessage(ex.Message, Strings.S_DISCONN_FAILED_TITLE, DialogService.DialogIcon.Critical);
                return;
            }
        }
        else if (device.Type is AbstractDevice.DeviceType.Remote)
        {
            try
            {
                ADBService.DisconnectNetworkDevice(device.ID);
            }
            catch (Exception ex)
            {
                DialogService.ShowMessage(ex.Message, Strings.S_DISCONN_FAILED_TITLE, DialogService.DialogIcon.Critical);
                return;
            }
        }
        else if (device.Type is AbstractDevice.DeviceType.History)
        { } // No additional action is required
        else
        {
            throw new NotImplementedException();
        }

        Data.RuntimeSettings.DeviceToRemove = device;
    }

    private ICommand command;
    public ICommand Command => command ??= new CommandHandler(() => Action(), () => IsEnabled);
}

public class PairCommand : ServiceDeviceAction
{
    public override bool IsEnabled => device.IsPairingCodeValid
        && device.Status is AbstractDevice.DeviceStatus.Unauthorized;

    public PairCommand(ServiceDeviceViewModel device) : base(device)
    { }

    public void Action()
    {
        Data.RuntimeSettings.DeviceToPair = device;
    }

    private ICommand command;
    public ICommand Command => command ??= new CommandHandler(() => Action(), () => IsEnabled);
}

public class ToggleRootCommand : LogicalDeviceAction
{
    public override bool IsEnabled => device.Root is not AbstractDevice.RootStatus.Forbidden
        && device.Status is AbstractDevice.DeviceStatus.Ok
        && device.Type is not AbstractDevice.DeviceType.Sideload;

    public ToggleRootCommand(LogicalDeviceViewModel device) : base(device)
    { }

    public void Action()
    {
        bool rootEnabled = device.Root is AbstractDevice.RootStatus.Enabled;

        var rootTask = Task.Run(() =>
        {
            device.EnableRoot(!rootEnabled);
        });
        rootTask.ContinueWith((t) =>
        {
            if (device.Root is AbstractDevice.RootStatus.Forbidden)
                Data.RuntimeSettings.RootAttemptForbidden = true;
        });
    }

    private ICommand command;
    public ICommand Command => command ??= new CommandHandler(() => Action(), () => IsEnabled);
}

public class RebootCommand : LogicalDeviceAction
{
    public RebootCommand(LogicalDeviceViewModel device) : base(device)
    { }

    public void Action() => Task.Run(() => ADBService.AdbDevice.Reboot(device.ID, ""));

    private ICommand command;
    public ICommand Command => command ??= new CommandHandler(() => Action(), () => IsEnabled);
}

public class BootloaderCommand : LogicalDeviceAction
{
    public BootloaderCommand(LogicalDeviceViewModel device) : base(device)
    { }

    public void Action() => Task.Run(() => ADBService.AdbDevice.Reboot(device.ID, "bootloader"));

    private ICommand command;
    public ICommand Command => command ??= new CommandHandler(() => Action(), () => IsEnabled);
}

public class RecoveryCommand : LogicalDeviceAction
{
    public RecoveryCommand(LogicalDeviceViewModel device) : base(device)
    { }

    public void Action() => Task.Run(() => ADBService.AdbDevice.Reboot(device.ID, "recovery"));

    private ICommand command;
    public ICommand Command => command ??= new CommandHandler(() => Action(), () => IsEnabled);
}

public class SideloadCommand : LogicalDeviceAction
{
    public SideloadCommand(LogicalDeviceViewModel device) : base(device)
    { }

    public void Action() => Task.Run(() => ADBService.AdbDevice.Reboot(device.ID, "sideload"));

    private ICommand command;
    public ICommand Command => command ??= new CommandHandler(() => Action(), () => IsEnabled);
}

public class SideloadAutoCommand : LogicalDeviceAction
{
    public SideloadAutoCommand(LogicalDeviceViewModel device) : base(device)
    { }

    public void Action() => Task.Run(() => ADBService.AdbDevice.Reboot(device.ID, "sideload-auto-reboot"));

    private ICommand command;
    public ICommand Command => command ??= new CommandHandler(() => Action(), () => IsEnabled);
}

public class ConnectCommand : NewDeviceAction
{
    public override bool IsEnabled
    {
        get
        {
            if (!device.IsIpAddressValid || !device.IsConnectPortValid)
                return false;

            if (device is NewDeviceViewModel
                && device.IsPairingEnabled
                && (!device.IsPairingCodeValid || !device.IsPairingPortValid))
                return false;

            return true;
        }
    }

    public ConnectCommand(NewDeviceViewModel device) : base(device)
    { }

    public ConnectCommand(HistoryDeviceViewModel device) : base(device)
    { }

    public void Action()
    {
        Data.RuntimeSettings.ConnectNewDevice = device;
    }

    private ICommand command;
    public ICommand Command => command ??= new CommandHandler(() => Action(), () => IsEnabled);
}

public class ClearCommand : NewDeviceAction
{
    public override bool IsEnabled
    {
        get
        {
            return !(string.IsNullOrEmpty(device.IpAddress)
                    && string.IsNullOrEmpty(device.ConnectPort)
                    && string.IsNullOrEmpty(device.PairingPort)
                    && string.IsNullOrEmpty(device.PairingCode));
        }
    }

    public ClearCommand(NewDeviceViewModel device) : base(device)
    { }

    public void Action()
    {
        device.ClearDevice();
    }

    private ICommand command;
    public ICommand Command => command ??= new CommandHandler(() => Action(), () => IsEnabled);
}
