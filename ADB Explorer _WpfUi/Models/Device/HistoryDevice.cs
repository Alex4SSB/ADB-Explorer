namespace ADB_Explorer.Models;

public class HistoryDevice : NewDevice
{
    public string DeviceName { get; set; }

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
