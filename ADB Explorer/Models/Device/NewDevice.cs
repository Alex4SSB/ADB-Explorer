namespace ADB_Explorer.Models;

public class NewDevice : PairingDevice
{
    public string ConnectPort { get; set; }

    public string HostName { get; set; }

    public NewDevice()
    {
        Type = DeviceType.New;
        Status = DeviceStatus.Ok;
    }
}
