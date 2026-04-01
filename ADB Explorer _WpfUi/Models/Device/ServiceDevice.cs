namespace ADB_Explorer.Models;

/// <summary>
/// Represents all services acquired by <code>mdns services</code>
/// </summary>
public abstract class ServiceDevice : PairingDevice
{
    public enum ServiceType
    {
        QrCode,
        PairingCode
    }

    public ServiceType MdnsType { get; set; }

    public ServiceDevice()
    {
        Type = DeviceType.Service;
    }

    public ServiceDevice(string id, string ipAddress, string port = "") : this()
    {
        ID = id;
        IpAddress = ipAddress;
        PairingPort = port;
    }
}

public class PairingService : ServiceDevice
{
    public PairingService(string id, string ipAddress, string port) : base(id, ipAddress, port)
    { }
}

public class ConnectService : ServiceDevice
{
    public ConnectService(string id, string ipAddress, string port) : base(id, ipAddress, port)
    { }
}
