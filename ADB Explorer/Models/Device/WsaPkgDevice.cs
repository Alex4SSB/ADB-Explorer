namespace ADB_Explorer.Models;

public class WsaPkgDevice : Device
{
    private DateTime lastLaunch = DateTime.MinValue;
    public DateTime LastLaunch
    {
        get => lastLaunch;
        set => Set(ref lastLaunch, value);
    }

    public WsaPkgDevice()
    {
        Type = DeviceType.WSA;
        Status = DeviceStatus.Offline;
    }
}
