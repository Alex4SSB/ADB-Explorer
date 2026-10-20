using AdvancedSharpAdbClient.Models;

namespace ADB_Explorer.Models;

/// <summary>
/// A physical device acquired by <code>adb devices</code>, with every transport it is currently listed on.
/// <see cref="Device.ID"/> is the first transport's serial and stays fixed; commands follow <see cref="Primary"/>.
/// </summary>
public class LogicalDevice : Device
{
    public string Name { get; set; } = "";

    public string? AvdName { get; set; }

    public RootStatus Root { get; set; } = RootStatus.Unchecked;

    public Battery Battery { get; set; } = new();

    /// <summary>Replaced as a whole so background readers never see a list mid-update.</summary>
    public IReadOnlyList<DeviceConnection> Connections { get; private set; } = [];

    public DeviceConnection Primary { get; private set; } = null!;

    public DeviceData DeviceData => Primary.DeviceData;

    private LogicalDevice(string name, string id)
    {
        Name = name;
        ID = id;
    }

    public static LogicalDevice From(DeviceSnapshot snapshot)
    {
        LogicalDevice device = new(snapshot.Name, snapshot.ID)
        {
            Status = snapshot.Status,
            Root = snapshot.Root,
            IpAddress = snapshot.IpAddress,
        };

        device.SetConnections([DeviceConnection.From(snapshot)]);
        return device;
    }

    /// <summary>Sets the transports and adopts the preferred one's type. Status is left to the view-model.</summary>
    public void SetConnections(IEnumerable<DeviceConnection> connections)
    {
        var ordered = connections.OrderBy(c => c.Rank).ToArray();
        if (ordered.Length == 0)
            return;

        Connections = ordered;
        Primary = ordered[0];
        Type = Primary.Type;

        if (ordered.FirstOrDefault(c => c.Type is DeviceType.Remote) is { } remote)
            IpAddress = remote.IpAddress;
    }

    public override string ToString() => Name;
}
