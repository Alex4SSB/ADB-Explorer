using QRCoder;
using QRCoder.Xaml;

namespace ADB_Explorer.Helpers;

public static class QrGenerator
{
    public static DrawingImage GenerateQR(string val, SolidColorBrush background, SolidColorBrush foreground)
    {
        QRCodeGenerator qrGenerator = new();
        QRCodeData qrCodeData = qrGenerator.CreateQrCode(val, QRCodeGenerator.ECCLevel.Q);
        var qrCode = new XamlQRCode(qrCodeData);
        var image = qrCode.GetGraphic(new Size(256, 256), foreground, background, false);
        
        return image;
    }
}
