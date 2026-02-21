namespace ADB_Explorer.Models;

public class HistoryDevice : NewDevice
{
    private string deviceName = null;
    public string DeviceName
    {
        get => deviceName;
        set => Set(ref deviceName, value);
    }

    public HistoryDevice()
    {
        Type = DeviceType.History;
        Status = DeviceStatus.Ok;
    }

    public HistoryDevice(NewDevice device) : this()
    {
        IpAddress = device.IpAddress;
        ConnectPort = device.ConnectPort;
    }

    [JsonConstructor]
    public HistoryDevice(string ipAddress, string connectPort, string deviceName = "") : this()
    {
        DeviceName = deviceName;
        IpAddress = ipAddress;
        ConnectPort = connectPort;
    }
}
