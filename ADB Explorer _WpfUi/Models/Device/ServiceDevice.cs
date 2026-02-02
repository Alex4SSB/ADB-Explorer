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

    #region Full properties

    private ServiceType mdnsType;
    public ServiceType MdnsType
    {
        get => mdnsType;
        set => Set(ref mdnsType, value);
    }

    #endregion

    public ServiceDevice()
    {
        Type = DeviceType.Service;
    }

    public ServiceDevice(string id, string ipAddress, string port = "") : this()
    {
        PropertyChanged += ServiceDevice_PropertyChanged;

        ID = id;
        IpAddress = ipAddress;
        PairingPort = port;
    }

    private void ServiceDevice_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PairingPort) or nameof(MdnsType))
        {
            UpdateStatus();
        }
    }

    private void UpdateStatus()
    {
        Status = MdnsType is ServiceType.QrCode ? DeviceStatus.Ok : DeviceStatus.Unauthorized;
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
