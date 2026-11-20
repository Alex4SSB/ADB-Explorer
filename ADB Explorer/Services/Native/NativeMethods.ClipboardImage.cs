namespace ADB_Explorer.Services;

using static Vanara.PInvoke.Gdi32;
using static Vanara.PInvoke.Kernel32;
using static Vanara.PInvoke.User32;

public static partial class NativeMethods
{
    /// <summary>
    /// Puts a BGRA image on the clipboard as DeviceIndependentBitmap then Format17, in the same
    /// order Windows uses - minus CF_BITMAP, since a hand-built handle for it made some readers
    /// give up on the whole clipboard instead of falling back to DIB.
    /// </summary>
    public static bool SetClipboardImage(BitmapSource image)
    {
        var converted = image.Format == PixelFormats.Bgra32
            ? image
            : new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);

        int width = converted.PixelWidth;
        int height = converted.PixelHeight;
        int stride = width * 4;
        var topDownBgra = new byte[stride * height];
        converted.CopyPixels(topDownBgra, stride, 0);

        var dib = PackBottomUpDib(BuildDibHeaderBytes(width, height, topDownBgra.Length), topDownBgra, width, height);
        var dibV5 = PackBottomUpDib(BuildDibV5HeaderBytes(width, height, topDownBgra.Length), topDownBgra, width, height);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (OpenClipboard(0))
            {
                try
                {
                    EmptyClipboard();

                    var okDib = SetClipboardData(AdbDataFormats.DeviceIndependentBitmap, (IntPtr)CopyToHGlobal(dib)) != 0;
                    var okDibV5 = SetClipboardData(AdbDataFormats.Format17, (IntPtr)CopyToHGlobal(dibV5)) != 0;

                    return okDib || okDibV5;
                }
                finally
                {
                    CloseClipboard();
                }
            }

            Thread.Sleep(50);
        }

        return false;
    }

    private static byte[] BuildDibHeaderBytes(int width, int height, int imageSize)
    {
        var header = new BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = width,
            biHeight = height,
            biPlanes = 1,
            biBitCount = 32,
            biSizeImage = (uint)imageSize,
        };

        return MemoryMarshal.AsBytes(new ReadOnlySpan<BITMAPINFOHEADER>(in header)).ToArray();
    }

    private static byte[] BuildDibV5HeaderBytes(int width, int height, int imageSize)
    {
        var header = new BITMAPV5HEADER
        {
            bV5Size = (uint)Marshal.SizeOf<BITMAPV5HEADER>(),
            bV5Width = width,
            bV5Height = height,
            bV5Planes = 1,
            bV5BitCount = 32,
            bV5Compression = 3, // BI_BITFIELDS
            bV5SizeImage = (uint)imageSize,
            bV5RedMask = 0x00FF0000,
            bV5GreenMask = 0x0000FF00,
            bV5BlueMask = 0x000000FF,
            bV5AlphaMask = 0xFF000000,
            bV5CSType = 0x73524742, // 'sRGB'
        };

        return MemoryMarshal.AsBytes(new ReadOnlySpan<BITMAPV5HEADER>(in header)).ToArray();
    }

    // CF_DIB/CF_DIBV5 store rows bottom-up; WPF hands out pixels top-down.
    private static byte[] PackBottomUpDib(byte[] headerBytes, byte[] topDownBgra, int width, int height)
    {
        int stride = width * 4;
        var result = new byte[headerBytes.Length + topDownBgra.Length];
        headerBytes.CopyTo(result, 0);

        for (int y = 0; y < height; y++)
            Buffer.BlockCopy(topDownBgra, y * stride, result, headerBytes.Length + (height - 1 - y) * stride, stride);

        return result;
    }

    private static HGLOBAL CopyToHGlobal(byte[] bytes)
    {
        var hMem = GlobalAlloc(GMEM.GMEM_MOVEABLE, bytes.Length);
        var ptr = GlobalLock(hMem);
        Marshal.Copy(bytes, 0, ptr, bytes.Length);
        GlobalUnlock(hMem);

        return hMem;
    }
}
