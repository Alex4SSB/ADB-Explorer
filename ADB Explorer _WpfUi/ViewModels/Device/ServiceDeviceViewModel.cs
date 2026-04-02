using ADB_Explorer.Helpers;
using ADB_Explorer.Models;

namespace ADB_Explorer.ViewModels;

public class ServiceDeviceViewModel : PairingDeviceViewModel
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

    public ServiceDevice.PairingMode MdnsType => Device.MdnsType;

    public ServiceConnectionKind ConnectionKind => Device.ConnectionKind;

    public override string Tooltip
    {
        get
        {
            var type = MdnsType is ServiceDevice.PairingMode.QrCode
                ? Strings.Resources.S_DEVICE_QR
                : Strings.Resources.S_DEVICE_READY_PAIR;

            return $"{Strings.Resources.S_TYPE_SERVICE} - {type}";
        }
    }

    #endregion

    public DeviceAction PairCommand { get; }

    public ServiceDeviceViewModel(ServiceDevice service) : base(service)
    {
        Device = service;

        UpdateServiceStatus();

        PairCommand = new(() => IsPairingCodeValid && device.Status is DeviceStatus.Unauthorized,
                          () => _ = DeviceHelper.PairService(this));
    }

    private void UpdateServiceStatus()
    {
        Device.Status = Device.MdnsType is ServiceDevice.PairingMode.QrCode ? DeviceStatus.Ok : DeviceStatus.Unauthorized;
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusIcon));
    }

    /// <summary>
    /// Updates the pairing port with the pairing port of the given service.
    /// </summary>
    /// <param name="other">The service with the new port.</param>
    public bool UpdateService(ServiceDeviceViewModel other)
    {
        bool changed = SetPairingPort(other.PairingPort);

        if (Device.MdnsType != other.Device.MdnsType)
        {
            Device.MdnsType = other.Device.MdnsType;
            OnPropertyChanged(nameof(MdnsType));
            UpdateServiceStatus();
            changed = true;
        }

        return changed;
    }
}
