namespace ADB_Explorer.Models;

public class EmulatorPackageDevice : Device
{
    public string AvdName { get; set; }

    public DateTime LastLaunch { get; set; } = DateTime.MinValue;

    public EmulatorPackageDevice(string avdName)
    {
        AvdName = avdName;
        ID = avdName;
        Type = DeviceType.EmulatorPackage;
        Status = DeviceStatus.Ok;
    }
}
