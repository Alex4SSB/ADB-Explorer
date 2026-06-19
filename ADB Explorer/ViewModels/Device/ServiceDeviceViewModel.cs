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

    private bool isPairingInProgress;
    public bool IsPairingInProgress
    {
        get => isPairingInProgress;
        private set => Set(ref isPairingInProgress, value);
    }

    private string pairingError;
    public string PairingError
    {
        get => pairingError;
        private set
        {
            if (Set(ref pairingError, value))
                OnPropertyChanged(nameof(HasPairingError));
        }
    }

    #endregion

    #region Read only properties

    public bool HasPairingError => !string.IsNullOrEmpty(PairingError);

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

    public ServiceDeviceViewModel(ServiceDevice service, Devices devicesObject = null) : base(service, devicesObject)
    {
        Device = service;

        UpdateServiceStatus();

        PairCommand = new(() => !IsPairingInProgress && IsPairingCodeValid && device.Status is DeviceStatus.Unauthorized,
                          () => _ = DeviceHelper.PairService(this, CancellationToken.None));
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

    public void BeginPairing()
    {
        PairingError = null;
        IsPairingInProgress = true;
        PairCommand.NotifyIsEnabledChanged();
        CommandManager.InvalidateRequerySuggested();
    }

    public void EndPairing(bool success, string error = null)
    {
        IsPairingInProgress = false;

        if (!success && !string.IsNullOrEmpty(error))
            PairingError = error;

        PairCommand.NotifyIsEnabledChanged();
        CommandManager.InvalidateRequerySuggested();
    }
}
