namespace ADB_Explorer.Models;

public class WsaPkgDevice : Device
{
    public DateTime LastLaunch { get; set; } = DateTime.MinValue;

    public WsaPkgDevice()
    {
        Type = DeviceType.WSA;
        Status = DeviceStatus.Offline;
    }
}
