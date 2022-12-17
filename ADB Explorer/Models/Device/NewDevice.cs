namespace ADB_Explorer.Models;

public class NewDevice : PairingDevice
{
    private string connectPort;
    public string ConnectPort
    {
        get => connectPort;
        set => Set(ref connectPort, value);
    }

    public NewDevice()
    {
        Type = DeviceType.New;
        Status = DeviceStatus.Ok;
    }
}
