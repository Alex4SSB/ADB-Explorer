using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows.Media;
using static ADB_Explorer.Models.AdbExplorerConst;

namespace ADB_Explorer.Services
{
    public class WiFiPairingService
    {
        public static DrawingImage GenerateQrCode(SolidColorBrush background, SolidColorBrush foreground)
        {
            var serviceName = PAIRING_SERVICE_PREFIX + RandomString.GetUniqueKey(10);
            var password = RandomString.GetUniqueKey(12);
            var pairingString = CreatePairingString(serviceName, password);
            var image = QrGenerator.GenerateQR(pairingString, background, foreground);
            return image;
        }

        /**
        * Format is "WIFI:T:ADB;S:service;P:password;;" (without the quotes)
        */
        private static string CreatePairingString(string service, string password) 
        {
            return $"WIFI:T:ADB;S:{service};P:{password};;";
        }

        public static List<MdnsService> GetServices()
        {
            ADBService.ExecuteAdbCommand("mdns", out string services, out _, "services");

            List<MdnsService> mdnsServices = new();
            var matches = AdbRegEx.MDNS_SERVICE.Matches(services);
            foreach (Match item in matches)
            {
                var id  = item.Groups["ID"].Value;
                var portType = item.Groups["PortType"].Value;
                var ipAddress = item.Groups["IpAddress"].Value;
                var port = item.Groups["Port"].Value;

                if (mdnsServices.Count > 0 && mdnsServices[^1].IpAddress == ipAddress)
                {
                    if (string.IsNullOrEmpty(mdnsServices[^1].PairingPort) && portType == "pairing")
                        mdnsServices[^1].PairingPort = port;
                    else if (string.IsNullOrEmpty(mdnsServices[^1].ConnectPort) && portType == "connect")
                        mdnsServices[^1].ConnectPort = port;
                }
                else
                {
                    MdnsService service = new() { ID = id, IpAddress = ipAddress };
                    if (portType == "pairing")
                        service.PairingPort = port;
                    else if (portType == "connect")
                        service.ConnectPort = port;

                    service.Type = id.Contains(PAIRING_SERVICE_PREFIX)
                        ? MdnsService.ServiceType.QrCode
                        : MdnsService.ServiceType.PairingCode;

                    mdnsServices.Add(service);
                }
            }

            return mdnsServices;
        }
    }
}
