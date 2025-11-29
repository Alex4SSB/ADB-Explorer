using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using static ADB_Explorer.Models.AdbExplorerConst;

namespace ADB_Explorer.Services;

public class PairingQrClass
{
    public string ServiceName { get; }
    public string Password { get; }
    public SolidColorBrush Background { get; }
    public SolidColorBrush Foreground { get; }

    public DrawingImage Image => string.IsNullOrEmpty(PairingString) ? null : QrGenerator.GenerateQR(PairingString, Background, Foreground);
    public string PairingString => WiFiPairingService.CreatePairingString(ServiceName, Password);

    public PairingQrClass()
    {
        ServiceName = PAIRING_SERVICE_PREFIX + RandomString.GetUniqueKey(10);
        Password = RandomString.GetUniqueKey(12);

        Background = (SolidColorBrush)App.Current.FindResource("QrBackgroundBrush");
        Foreground = (SolidColorBrush)App.Current.FindResource("QrForegroundBrush");
    }
}

public class WiFiPairingService
{
    /**
    * Format is "WIFI:T:ADB;S:service;P:password;;" (without the quotes)
    */
    public static string CreatePairingString(string service, string password)
    {
        return $"WIFI:T:ADB;S:{service};P:{password};;";
    }

    public static IEnumerable<ServiceDevice> GetServices()
    {
        ADBService.ExecuteAdbCommand("mdns", out string services, out _, new(), "services");

        List<ServiceDevice> mdnsServices = [];
        var matches = AdbRegEx.RE_MDNS_SERVICE().Matches(services);
        foreach (Match item in matches)
        {
            var id = item.Groups["ID"].Value;
            var portType = item.Groups["PortType"].Value;
            var ipAddress = item.Groups["IpAddress"].Value;
            var port = item.Groups["Port"].Value;

            if (LOOPBACK_ADDRESSES.Contains(ipAddress))
                continue;

            ServiceDevice service = portType == "pairing"
                ? new PairingService(id, ipAddress, port)
                : new ConnectService(id, ipAddress, port);

            service.MdnsType = id.Contains(PAIRING_SERVICE_PREFIX)
                ? ServiceDevice.ServiceType.QrCode
                : ServiceDevice.ServiceType.PairingCode;

            mdnsServices.Add(service);
        }

        return mdnsServices.Distinct();
    }
}
