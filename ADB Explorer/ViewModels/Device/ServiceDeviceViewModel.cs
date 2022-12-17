using ADB_Explorer.Models;

namespace ADB_Explorer.ViewModels;

public abstract class ServiceDeviceViewModel : PairingDeviceViewModel
{
    #region Full properties

    private ServiceDevice device;
    protected new ServiceDevice Device
    {
        get => device;
        set => Set(ref device, value);
    }

    private string uiPairingCode;
    public string UIPairingCode
    {
        get => uiPairingCode;
        set
        {
            if (Set(ref uiPairingCode, value))
                SetPairingCode(uiPairingCode?.Replace("-", ""));
        }
    }

    #endregion

    #region Read only properties

    public ServiceDevice.ServiceType MdnsType => Device.MdnsType;

    public override string Tooltip => $"mDNS Service - {(MdnsType is ServiceDevice.ServiceType.QrCode ? "QR Pairing" : "Ready To Pair")}";

    #endregion

    public PairCommand PairCommand { get; private set; }

    public ServiceDeviceViewModel(ServiceDevice service) : base(service)
    {
        Device = service;

        PairCommand = new(this);
    }

    /// <summary>
    /// Updates the pairing port with the pairing port of the given service.
    /// </summary>
    /// <param name="other">The service with the new port.</param>
    public bool UpdateService(ServiceDeviceViewModel other)
    {
        return SetPairingPort(other.PairingPort);
    }

    public static ServiceDeviceViewModel New(ServiceDevice device)
    {
        return device switch
        {
            PairingService => new PairingServiceViewModel(device),
            ConnectService => new ConnectServiceViewModel(device),
            _ => throw new NotImplementedException(),
        };
    }
}

public class PairingServiceViewModel : ServiceDeviceViewModel
{
    public PairingServiceViewModel(ServiceDevice service) : base(service)
    { }
}

public class ConnectServiceViewModel : ServiceDeviceViewModel
{
    public ConnectServiceViewModel(ServiceDevice service) : base(service)
    { }
}

public class ServiceDeviceViewModelEqualityComparer : IEqualityComparer<ServiceDeviceViewModel>
{
    public bool Equals(ServiceDeviceViewModel x, ServiceDeviceViewModel y)
    {
        // IDs are equal and either both ports have a value, or they're both null
        // We do not update the port since it can change too frequently, and we do not use it anyway
        return x.ID == y.ID && !(string.IsNullOrEmpty(x.PairingPort) ^ string.IsNullOrEmpty(y.PairingPort));
    }

    public int GetHashCode([DisallowNull] ServiceDeviceViewModel obj)
    {
        throw new NotImplementedException();
    }
}
