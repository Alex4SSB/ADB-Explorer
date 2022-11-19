using ADB_Explorer.Helpers;
using ADB_Explorer.Resources;
using ADB_Explorer.Services;

namespace ADB_Explorer.Models;

public abstract class DeviceAction : INotifyPropertyChanged
{
    protected UIDevice device;

    public virtual bool IsEnabled { get; } = true;

    protected DeviceAction(UIDevice device)
    {
        this.device = device;
    }

    public event PropertyChangedEventHandler PropertyChanged;
    protected bool Set<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
    {
        if (Equals(storage, value))
            return false;

        storage = value;
        OnPropertyChanged(propertyName);

        return true;
    }
    protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public class BrowseCommand : DeviceAction
{
    public override bool IsEnabled => !((UILogicalDevice)device).IsOpen
        && device.Device.Status
        is AbstractDevice.DeviceStatus.Ok;

    public BrowseCommand(UILogicalDevice device) : base(device)
    { }

    public void Action()
    {
        Data.CurrentADBDevice = new(device);
        Data.RuntimeSettings.UpdateCurrentDevice = true;
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
            return device.Device.Type is AbstractDevice.DeviceType.History
                    || (!Data.RuntimeSettings.IsManualPairingInProgress
                        && device.Device.Type is AbstractDevice.DeviceType.Remote
                                              or AbstractDevice.DeviceType.Emulator);
        }
    }

    public string RemoveAction => device.Device.Type switch
    {
        AbstractDevice.DeviceType.Remote => Strings.S_REM_DEV,
        AbstractDevice.DeviceType.Emulator => Strings.S_REM_EMU,
        AbstractDevice.DeviceType.History => Strings.S_REM_HIST_DEV,
        _ => "",
    };

    public RemoveCommand(UILogicalDevice device) : base(device)
    { }

    public RemoveCommand(UIHistoryDevice device) : base(device)
    { }

    public async void Action()
    {
        var dialogTask = await DialogService.ShowConfirmation(Strings.S_REM_DEVICE(device.Device), Strings.S_REM_DEVICE_TITLE(device.Device));
        if (dialogTask.Item1 is not ModernWpf.Controls.ContentDialogResult.Primary)
            return;

        if (device.Device.Type is AbstractDevice.DeviceType.Emulator)
        {
            try
            {
                ADBService.KillEmulator(device.Device.ID);
            }
            catch (Exception ex)
            {
                DialogService.ShowMessage(ex.Message, Strings.S_DISCONN_FAILED_TITLE, DialogService.DialogIcon.Critical);
                return;
            }
        }
        else if (device.Device.Type is AbstractDevice.DeviceType.Remote)
        {
            try
            {
                ADBService.DisconnectNetworkDevice(device.Device.ID);
            }
            catch (Exception ex)
            {
                DialogService.ShowMessage(ex.Message, Strings.S_DISCONN_FAILED_TITLE, DialogService.DialogIcon.Critical);
                return;
            }
        }
        else if (device.Device.Type is AbstractDevice.DeviceType.History)
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

public class PairCommand : DeviceAction
{
    public override bool IsEnabled => ((NetworkDevice)device.Device).IsPairingCodeValid
        && device.Device.Status is AbstractDevice.DeviceStatus.Unauthorized;

    public PairCommand(UIServiceDevice device) : base(device)
    { }

    public void Action()
    {
        Data.RuntimeSettings.DeviceToPair = (ServiceDevice)device.Device;
    }

    private ICommand command;
    public ICommand Command => command ??= new CommandHandler(() => Action(), () => IsEnabled);
}

public class ToggleRootCommand : DeviceAction
{
    public override bool IsEnabled => ((LogicalDevice)device.Device).Root is not AbstractDevice.RootStatus.Forbidden
        && device.Device.Status is AbstractDevice.DeviceStatus.Ok
        && device.Device.Type is not AbstractDevice.DeviceType.Sideload;

    public ToggleRootCommand(UILogicalDevice device) : base(device)
    { }

    public void Action()
    {
        var logical = (LogicalDevice)device.Device;
        bool rootEnabled = logical.Root is AbstractDevice.RootStatus.Enabled;

        var rootTask = Task.Run(() =>
        {
            logical.EnableRoot(!rootEnabled);
        });
        rootTask.ContinueWith((t) =>
        {
            if (logical.Root is AbstractDevice.RootStatus.Forbidden)
                Data.RuntimeSettings.RootAttemptForbidden = true;
        });
    }

    private ICommand command;
    public ICommand Command => command ??= new CommandHandler(() => Action(), () => IsEnabled);
}

public class RebootCommand : DeviceAction
{
    public RebootCommand(UILogicalDevice device) : base(device)
    { }

    public void Action() => Task.Run(() => ADBService.AdbDevice.Reboot((LogicalDevice)device.Device, ""));

    private ICommand command;
    public ICommand Command => command ??= new CommandHandler(() => Action(), () => IsEnabled);
}

public class BootloaderCommand : DeviceAction
{
    public BootloaderCommand(UILogicalDevice device) : base(device)
    { }

    public void Action() => Task.Run(() => ADBService.AdbDevice.Reboot((LogicalDevice)device.Device, "bootloader"));

    private ICommand command;
    public ICommand Command => command ??= new CommandHandler(() => Action(), () => IsEnabled);
}

public class RecoveryCommand : DeviceAction
{
    public RecoveryCommand(UILogicalDevice device) : base(device)
    { }

    public void Action() => Task.Run(() => ADBService.AdbDevice.Reboot((LogicalDevice)device.Device, "recovery"));

    private ICommand command;
    public ICommand Command => command ??= new CommandHandler(() => Action(), () => IsEnabled);
}

public class SideloadCommand : DeviceAction
{
    public SideloadCommand(UILogicalDevice device) : base(device)
    { }

    public void Action() => Task.Run(() => ADBService.AdbDevice.Reboot((LogicalDevice)device.Device, "sideload"));

    private ICommand command;
    public ICommand Command => command ??= new CommandHandler(() => Action(), () => IsEnabled);
}

public class SideloadAutoCommand : DeviceAction
{
    public SideloadAutoCommand(UILogicalDevice device) : base(device)
    { }

    public void Action() => Task.Run(() => ADBService.AdbDevice.Reboot((LogicalDevice)device.Device, "sideload-auto-reboot"));

    private ICommand command;
    public ICommand Command => command ??= new CommandHandler(() => Action(), () => IsEnabled);
}

public class NewDevice : NetworkDevice
{
    private string connectPort;
    public string ConnectPort
    {
        get => connectPort;
        set
        {
            if (Set(ref connectPort, value))
                OnPropertyChanged(nameof(IsConnectPortValid));
        }
    }

    public bool IsConnectPortValid => !string.IsNullOrWhiteSpace(ConnectPort)
                                      && ushort.TryParse(ConnectPort, out ushort res)
                                      && res > 0;

    public NewDevice()
    {
        Type = DeviceType.New;
        Status = DeviceStatus.Ok;
    }

    public string ConnectAddress => $"{IpAddress}:{ConnectPort}";
}

public class ConnectCommand : DeviceAction
{
    private NewDevice dev => device.Device as NewDevice;

    public override bool IsEnabled
    {
        get
        {
            if (!dev.IsIpAddressValid || !dev.IsConnectPortValid)
                return false;

            if (device is UINewDevice
                && ((UINewDevice)device).IsPairingEnabled
                && (!dev.IsPairingCodeValid || !dev.IsPairingPortValid))
                return false;

            return true;
        }
    }

    public ConnectCommand(UINewDevice device) : base(device)
    { }

    public ConnectCommand(UIHistoryDevice device) : base(device)
    { }

    public void Action()
    {
        Data.RuntimeSettings.ConnectNewDevice = device;
    }

    private ICommand command;
    public ICommand Command => command ??= new CommandHandler(() => Action(), () => IsEnabled);
}

public class ClearCommand : DeviceAction
{
    private NewDevice dev => device.Device as NewDevice;

    public override bool IsEnabled
    {
        get
        {
            return !(string.IsNullOrEmpty(dev.IpAddress)
                    && string.IsNullOrEmpty(dev.ConnectPort)
                    && string.IsNullOrEmpty(dev.PairingPort)
                    && string.IsNullOrEmpty(dev.PairingCode));
        }
    }

    public ClearCommand(UINewDevice device) : base(device)
    { }

    public void Action()
    {
        ((UINewDevice)device).ClearDevice();
    }

    private ICommand command;
    public ICommand Command => command ??= new CommandHandler(() => Action(), () => IsEnabled);
}
