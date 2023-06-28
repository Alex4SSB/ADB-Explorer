using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Resources;

namespace ADB_Explorer.ViewModels;

public class NewDeviceViewModel : PairingDeviceViewModel
{
    #region Full properties

    private NewDevice device;
    protected new NewDevice Device
    {
        get => device;
        set => Set(ref device, value);
    }

    private bool isPairingEnabled = false;
    public bool IsPairingEnabled
    {
        get => isPairingEnabled;
        set => Set(ref isPairingEnabled, value);
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

    public string ConnectPort
    {
        get => Device.ConnectPort;
        set => SetConnectPort(value);
    }

    public string HostName
    {
        get => Device.HostName;
        set => SetHostName(value);
    }

    public bool IsHostNameValid => !string.IsNullOrWhiteSpace(HostName);

    public string ConnectAddress => $"{(string.IsNullOrEmpty(HostName) ? IpAddress : HostName)}:{ConnectPort}";

    public override string PairingAddress => $"{(string.IsNullOrEmpty(HostName) ? IpAddress : HostName)}:{PairingPort}";

    public bool IsConnectPortValid => !string.IsNullOrWhiteSpace(ConnectPort)
                                      && ushort.TryParse(ConnectPort, out ushort res)
                                      && res > 0;

    public override string Tooltip => Strings.S_NEW_DEVICE_TIP;

    public override bool DeviceExists => false;

    #endregion

    public DeviceAction ConnectCommand { get; }

    public DeviceAction ClearCommand { get; }

    public NewDeviceViewModel(NewDevice device) : base(device)
    {
        Device = device;
        
        ConnectCommand = DeviceHelper.ConnectDeviceCommand(this);

        ClearCommand = new(() =>
        {
            return !(string.IsNullOrEmpty(device.IpAddress)
                    && string.IsNullOrEmpty(device.ConnectPort)
                    && string.IsNullOrEmpty(device.PairingPort)
                    && string.IsNullOrEmpty(device.PairingCode)
                    && string.IsNullOrEmpty(device.HostName));
        },
        ClearDevice);

        PropertyChanged += NewDeviceViewModel_PropertyChanged;
    }

    private void NewDeviceViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IpAddress))
            HostName = IsIpAddressValid ? null : IpAddress;
    }

    public void ClearDevice()
    {
        SetIpAddress();
        SetConnectPort();
        SetPairingPort();
        SetHostName();

        UIPairingCode = "";
        IsPairingEnabled = false;
    }

    public void EnablePairing()
    {
        SetPairingPort();
        UIPairingCode = "";
        IsPairingEnabled = true;
    }

    public bool SetConnectPort(string port = "")
    {
        if (ConnectPort != port)
        {
            Device.ConnectPort = port;
            OnPropertyChanged(nameof(ConnectPort));
            OnPropertyChanged(nameof(IsConnectPortValid));

            return true;
        }

        return false;
    }

    public bool SetHostName(string name = "")
    {
        if (HostName != name)
        {
            Device.HostName = name; 
            OnPropertyChanged(nameof(HostName));
            OnPropertyChanged(nameof(IsHostNameValid));
        }

        return false;
    }
}
