using AdvancedSharpAdbClient.Models;

namespace ADB_Explorer.Models;

/// <summary>
/// One adb transport (USB, mDNS or IP) to a physical device, as listed by <c>adb devices</c>.
/// </summary>
public class DeviceConnection
{
    public required string ID { get; init; }

    public required DeviceType Type { get; init; }

    public required DeviceStatus Status { get; init; }

    public required DeviceData DeviceData { get; init; }

    public string IpAddress { get; init; } = "";

    /// <summary><c>ro.serialno</c> of the device behind this transport, or empty when unknown.</summary>
    public string Serial { get; init; } = "";

    public bool IsUsb => Type is DeviceType.Local or DeviceType.Recovery or DeviceType.Sideload;

    public bool IsWifi => Type is DeviceType.Service or DeviceType.Remote;

    /// <summary>Lower is preferred: online first, then USB, mDNS and plain IP.</summary>
    public int Rank
    {
        get
        {
            var statusRank = Status switch
            {
                DeviceStatus.Ok => 0,
                DeviceStatus.Unauthorized => 1,
                _ => 2,
            };

            var typeRank = Type switch
            {
                _ when IsUsb => 0,
                DeviceType.Service => 1,
                DeviceType.Remote => 2,
                _ => 3,
            };

            return statusRank * 10 + typeRank;
        }
    }

    public static DeviceConnection From(DeviceSnapshot snapshot) => new()
    {
        ID = snapshot.ID,
        Type = snapshot.Type,
        Status = snapshot.Status,
        DeviceData = snapshot.DeviceData,
        IpAddress = snapshot.IpAddress,
        Serial = ResolveSerial(snapshot),
    };

    private static string ResolveSerial(DeviceSnapshot snapshot) => snapshot.Type switch
    {
        DeviceType.Local or DeviceType.Recovery or DeviceType.Sideload => snapshot.ID,
        DeviceType.Service => DeviceHelper.ParseMdnsSerial(snapshot.ID),
        DeviceType.Remote => AdbService.GetCachedRemoteSerial(snapshot.ID),
        _ => "",
    };
}
