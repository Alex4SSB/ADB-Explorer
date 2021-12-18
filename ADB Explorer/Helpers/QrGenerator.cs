using QRCoder;
using QRCoder.Xaml;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;

namespace ADB_Explorer.Helpers
{
    public static class QrGenerator
    {
        public static DrawingImage GenerateQR(string val, SolidColorBrush background, SolidColorBrush foreground)
        {
            QRCodeGenerator qrGenerator = new QRCodeGenerator();
            QRCodeData qrCodeData = qrGenerator.CreateQrCode(val, QRCodeGenerator.ECCLevel.Q);
            var qrCode = new XamlQRCode(qrCodeData);
            var image = qrCode.GetGraphic(new System.Windows.Size(256, 256), foreground, background, false);
            
            return image;
        }
    }
}
