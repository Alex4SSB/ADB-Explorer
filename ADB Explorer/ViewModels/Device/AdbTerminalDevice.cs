namespace ADB_Explorer.ViewModels;

public class AdbTerminalDevice : DeviceViewModel
{
    public string Name { get; }
    public AdbTerminalDevice() : base(null)
    {
        Name = "ADB";
    }
}
