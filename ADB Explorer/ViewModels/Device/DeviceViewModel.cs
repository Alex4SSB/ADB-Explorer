using ADB_Explorer.Models;
using ADB_Explorer.Services;

namespace ADB_Explorer.ViewModels;

public abstract class DeviceViewModel : AbstractDevice
{
    #region Full properties

    private Device device;
    protected Device Device
    {
        get => device;
        set => Set(ref device, value);
    }

    private bool deviceSelected;
    public bool DeviceSelected
    {
        get => deviceSelected;
        set
        {
            if (Set(ref deviceSelected, value) && this is LogicalDeviceViewModel)
                Data.RuntimeSettings.SelectedDevicesCount += value ? 1 : -1;
        }
    }

    public string IpAddress
    {
        get => Device.IpAddress;
        set => SetIpAddress(value);
    }

    #endregion

    #region Read only properties

    public string ID => Device.ID;

    public DeviceType Type => Device.Type;

    public DeviceStatus Status => Device.Status;

    public string TypeIcon => Type switch
    {
        DeviceType.Local => "\uE839",
        DeviceType.Remote => "\uEE77",
        DeviceType.Emulator => "\uE99A",
        DeviceType.Service when Device is ServiceDevice service && service.MdnsType is ServiceDevice.ServiceType.QrCode => "\uED14",
        DeviceType.Service => "\uEDE4",
        DeviceType.Recovery => "\uED10",
        DeviceType.Sideload => "\uE67A",
        DeviceType.New => "\uE710",
        DeviceType.History => "\uE823",
        _ => throw new NotImplementedException(),
    };

    public string StatusIcon => Status switch
    {
        DeviceStatus.Ok => "",
        DeviceStatus.Offline => "\uEBFF",
        DeviceStatus.Unauthorized => "\uEC00",
        _ => throw new NotImplementedException(),
    };

    /// <summary>
    /// Specifies whether the <see cref="Device"/> is (still) a real device - not offline, and not New or History
    /// </summary>
    public bool DeviceExists => Type is not DeviceType.New and not DeviceType.History && Status is not DeviceStatus.Offline;

    public bool IsIpAddressValid => !string.IsNullOrWhiteSpace(IpAddress)
                                    && IpAddress.Count(c => c == '.') == 3
                                    && IpAddress.Split('.').Count(i => byte.TryParse(i, out _)) == 4;

    public bool IsDeviceConnectionInProgress => Data.RuntimeSettings.ConnectNewDevice?.Equals(this) is true;

    #endregion

    public virtual string Tooltip { get; }

    private DeviceViewModel()
    {
        Data.RuntimeSettings.PropertyChanged += RuntimeSettings_PropertyChanged;
    }

    protected DeviceViewModel(Device device) : this()
    {
        Device = device;
    }

    private void RuntimeSettings_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppRuntimeSettings.CollapseDevices) && Data.RuntimeSettings.CollapseDevices)
        {
            DeviceSelected = false;
        }
        else if (e.PropertyName == nameof(AppRuntimeSettings.ConnectNewDevice))
        {
            OnPropertyChanged(nameof(IsDeviceConnectionInProgress));
        }
    }

    #region Public setters

    public bool SetIpAddress(string ipAddress = "")
    {
        if (IpAddress != ipAddress)
        {
            Device.IpAddress = ipAddress;
            OnPropertyChanged(nameof(IpAddress));
            OnPropertyChanged(nameof(IsIpAddressValid));

            return true;
        }

        return false;
    }

    public bool SetStatus(DeviceStatus status)
    {
        if (Status != status)
        {
            Device.Status = status;

            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(StatusIcon));

            return true;
        }

        return false;
    }

    #endregion

    public static implicit operator bool(DeviceViewModel obj) => obj?.Device;
}

public abstract class PairingDeviceViewModel : DeviceViewModel
{
    #region Full properties

    private PairingDevice device;
    protected new PairingDevice Device
    {
        get => device;
        set => Set(ref device, value);
    }

    public string PairingPort
    {
        get => Device.PairingPort;
        set => SetPairingPort(value);
    }

    public string PairingCode
    {
        get => Device.PairingCode;
        set => SetPairingCode(value);
    }

    #endregion

    #region Read only properties

    public bool IsPairingPortValid => !string.IsNullOrWhiteSpace(PairingPort)
                                  && ushort.TryParse(PairingPort, out ushort res)
                                  && res > 0;

    public bool IsPairingCodeValid => !string.IsNullOrWhiteSpace(PairingCode) && PairingCode.Length == 6;

    public virtual string PairingAddress => $"{IpAddress}:{PairingPort}";

    #endregion

    protected PairingDeviceViewModel(PairingDevice device) : base(device)
    {
        Device = device;
    }

    #region Public setters

    public bool SetPairingPort(string port = "")
    {
        if (Device.PairingPort != port)
        {
            Device.PairingPort = port;
            OnPropertyChanged(nameof(PairingPort));
            OnPropertyChanged(nameof(IsPairingPortValid));

            return true;
        }

        return false;
    }

    public bool SetPairingCode(string code = "")
    {
        if (Device.PairingCode != code)
        {
            Device.PairingCode = code;
            OnPropertyChanged(nameof(IsPairingCodeValid));

            return true;
        }

        return false;
    }

    #endregion
}

public class DeviceViewModelEqualityComparer : IEqualityComparer<DeviceViewModel>
{
    public bool Equals(DeviceViewModel x, DeviceViewModel y)
    {
        return x.ID == y.ID && x.Status == y.Status;
    }

    public int GetHashCode([DisallowNull] DeviceViewModel obj)
    {
        throw new NotImplementedException();
    }
}
