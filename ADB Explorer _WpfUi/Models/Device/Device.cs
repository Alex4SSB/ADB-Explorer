namespace ADB_Explorer.Models;

public enum DeviceType
{
    Service,
    Local,
    Remote,
    Recovery,
    Sideload,
    WSA,
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

public abstract class Device
{
    public DeviceType Type { get; set; }

    public DeviceStatus Status { get; set; }

    public string IpAddress { get; set; }

    public string ID { get; set; }

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
    public string PairingPort { get; set; }

    public string PairingCode { get; set; }
}
