using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Models;

public abstract class AbstractDevice : ViewModelBase
{
    public enum DeviceType
    {
        Service,
        Local,
        Remote,
        Sideload,
        Emulator,
        History,
        New,
    }

    public enum DeviceStatus
    {
        Ok, // online \ does not require attention
        Offline,
        Unauthorized,
    }

    public enum RootStatus
    {
        Unchecked,
        Forbidden,
        Disabled,
        Enabled,
    }
}

public abstract class Device : AbstractDevice
{
    #region Full properties

    private DeviceType type;
    public DeviceType Type
    {
        get => type;
        protected set => Set(ref type, value);
    }

    private DeviceStatus status;
    public DeviceStatus Status
    {
        get => status;
        set => Set(ref status, value);
    }

    private string ipAddress;
    public string IpAddress
    {
        get => ipAddress;
        set => Set(ref ipAddress, value);
    }

    public string ID { get; protected set; }

    #endregion

    public static implicit operator bool(Device obj)
    {
        return obj is not null && !string.IsNullOrEmpty(obj.ID);
    }
}

/// <summary>
/// Represents all device types that require pairing properties
/// </summary>
public abstract class PairingDevice : Device
{
    #region Full properties

    private string pairingPort;
    public virtual string PairingPort
    {
        get => pairingPort;
        set => Set(ref pairingPort, value);
    }

    private string pairingCode;
    public string PairingCode
    {
        get => pairingCode;
        set => Set(ref pairingCode, value);
    }

    #endregion
}
