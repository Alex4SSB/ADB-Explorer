using ADB_Explorer.Models;
using static ADB_Explorer.Models.AdbExplorerConst;
using static ADB_Explorer.Models.AdbRegEx;

namespace ADB_Explorer.Services;

public class WiFiPairingService
{
    /**
    * Format is "WIFI:T:ADB;S:service;P:password;;" (without the quotes)
    */
    public static string CreatePairingString(string service, string password)
    {
        return $"WIFI:T:ADB;S:{service};P:{password};;";
    }

    public static IEnumerable<ServiceSnapshot> GetServices()
    {
        ADBService.ExecuteAdbCommand("mdns", out string services, out _, new(), "services");

        return RE_MDNS_SERVICE().Matches(services).Select(ServiceSnapshot.Parse).Where(s => s).Distinct();
    }
}

/// <summary>
/// Lightweight, fully-parsed snapshot of a single mDNS service line.
/// Used for cheap change-detection during polling before any ViewModel is allocated.
/// </summary>
public readonly record struct ServiceSnapshot(
    string ID,
    string IpAddress,
    string Port,
    ServiceDevice.PairingMode MdnsType,
    ServiceConnectionKind ConnectionKind)
{
    public static ServiceSnapshot Parse(Match match)
    {
        var ipAddress = match.Groups["IpAddress"].Value;
        
        // Only WSA will have local IP, an that is treated separately
        if (LOOPBACK_ADDRESSES.Contains(ipAddress))
            return default;

        var id = match.Groups["ID"].Value;
        var portType = match.Groups["PortType"].Value;
        var port = match.Groups["Port"].Value;
        var kind = portType == "pairing" ? ServiceConnectionKind.Pairing : ServiceConnectionKind.Connect;
        var mdnsType = id.Contains(PAIRING_SERVICE_PREFIX) ? ServiceDevice.PairingMode.QrCode : ServiceDevice.PairingMode.PairingCode;

        return new(id, ipAddress, port, mdnsType, kind);
    }

    public static implicit operator bool(ServiceSnapshot s) => !string.IsNullOrEmpty(s.ID);
}
