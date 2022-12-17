using ADB_Explorer.Models;

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

    public string ConnectAddress => $"{IpAddress}:{ConnectPort}";

    public bool IsConnectPortValid => !string.IsNullOrWhiteSpace(ConnectPort)
                                      && ushort.TryParse(ConnectPort, out ushort res)
                                      && res > 0;

    #endregion

    public ConnectCommand ConnectCommand { get; private set; }

    public ClearCommand ClearCommand { get; private set; }

    public NewDeviceViewModel(NewDevice device) : base(device)
    {
        Device = device;

        ConnectCommand = new(this);
        ClearCommand = new(this);
    }

    public void ClearDevice()
    {
        SetIpAddress();
        SetConnectPort();
        SetPairingPort();
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
            OnPropertyChanged(nameof(IsConnectPortValid));

            return true;
        }

        return false;
    }
}
