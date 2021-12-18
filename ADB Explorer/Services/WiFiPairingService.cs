using ADB_Explorer.Helpers;
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
    }
}
