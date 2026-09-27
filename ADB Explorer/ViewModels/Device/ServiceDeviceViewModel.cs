namespace ADB_Explorer.ViewModels;

public class ServiceDeviceViewModel : PairingDeviceViewModel
{
    #region Full properties

    private ServiceDevice _device = null!;
    protected new ServiceDevice Device
    {
        get => _device;
        set => SetProperty(ref _device, value);
    }

    private string _uiPairingCode = "";
    public string UIPairingCode
    {
        get => _uiPairingCode;
        set
        {
            if (SetProperty(ref _uiPairingCode, value))
                SetPairingCode(_uiPairingCode?.Replace("-", ""));
        }
    }

    public bool IsPairingInProgress { get; private set; }

    private string _pairingError = "";
    public string PairingError
    {
        get => _pairingError;
        private set
        {
            if (SetProperty(ref _pairingError, value))
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

    public ServiceDeviceViewModel(ServiceDevice service, Devices? devicesObject = null) : base(service, devicesObject)
    {
        Device = service;

        UpdateServiceStatus();

        PairCommand = new(() => !IsPairingInProgress && IsPairingCodeValid && _device.Status is DeviceStatus.Unauthorized,
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
        PairingError = "";
        IsPairingInProgress = true;
        PairCommand.NotifyIsEnabledChanged();
        CommandManager.InvalidateRequerySuggested();
    }

    public void EndPairing(bool success, string? error = null)
    {
        IsPairingInProgress = false;

        if (!success && !string.IsNullOrEmpty(error))
            PairingError = error;

        PairCommand.NotifyIsEnabledChanged();
        CommandManager.InvalidateRequerySuggested();
    }
}
