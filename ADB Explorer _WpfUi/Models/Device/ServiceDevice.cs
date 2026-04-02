using ADB_Explorer.Services;

namespace ADB_Explorer.Models;

public enum ServiceConnectionKind { Pairing, Connect }

/// <summary>
/// Represents all services acquired by <code>mdns services</code>
/// </summary>
public class ServiceDevice : PairingDevice
{
    public enum PairingMode
    {
        QrCode,
        PairingCode
    }

    public PairingMode MdnsType { get; set; }

    public ServiceConnectionKind ConnectionKind { get; set; }

    public ServiceDevice()
    {
        Type = DeviceType.Service;
    }

    public ServiceDevice(string id, string ipAddress, string port, ServiceConnectionKind kind) : this()
    {
        ID = id;
        IpAddress = ipAddress;
        PairingPort = port;
        ConnectionKind = kind;
    }

    public static ServiceDevice From(ServiceSnapshot snapshot) => new(snapshot.ID, snapshot.IpAddress, snapshot.Port, snapshot.ConnectionKind)
    {
        MdnsType = snapshot.MdnsType
    };
}
