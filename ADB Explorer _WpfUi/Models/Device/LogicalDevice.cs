using AdvancedSharpAdbClient.Models;
using ADB_Explorer.Services;

namespace ADB_Explorer.Models;

/// <summary>
/// Represents all devices acquired by <code>adb devices</code>
/// </summary>
public class LogicalDevice : Device
{
    public string Name { get; set; }

    public RootStatus Root { get; set; } = RootStatus.Unchecked;

    public Battery Battery { get; set; } = new();

    public DeviceData DeviceData { get; set; }

    private LogicalDevice(string name, string id)
    {
        Name = name;
        ID = id;
    }

    public static LogicalDevice From(DeviceSnapshot snapshot) => new LogicalDevice(snapshot.Name, snapshot.ID)
    {
        Type = snapshot.Type,
        Status = snapshot.Status,
        Root = snapshot.Root,
        IpAddress = snapshot.IpAddress,
        DeviceData = snapshot.DeviceData
    };

    public override string ToString() => Name;
}
