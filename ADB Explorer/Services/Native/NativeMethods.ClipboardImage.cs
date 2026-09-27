namespace ADB_Explorer.Services;

public static partial class NativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPV5HEADER
    {
        public uint bV5Size;
        public int bV5Width;
        public int bV5Height;
        public ushort bV5Planes;
        public ushort bV5BitCount;
        public uint bV5Compression;
        public uint bV5SizeImage;
        public int bV5XPelsPerMeter;
        public int bV5YPelsPerMeter;
        public uint bV5ClrUsed;
        public uint bV5ClrImportant;
        public uint bV5RedMask;
        public uint bV5GreenMask;
        public uint bV5BlueMask;
        public uint bV5AlphaMask;
        public uint bV5CSType;
        private readonly int _cieE1, _cieE2, _cieE3, _cieE4, _cieE5, _cieE6, _cieE7, _cieE8, _cieE9; // CIEXYZTRIPLE
        public uint bV5GammaRed;
        public uint bV5GammaGreen;
        public uint bV5GammaBlue;
        public uint bV5Intent;
        public uint bV5ProfileData;
        public uint bV5ProfileSize;
        public uint bV5Reserved;
    }

    private const uint GMEM_MOVEABLE = 0x0002;

    [LibraryImport("User32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenClipboard(HANDLE hWndNewOwner);

    [LibraryImport("User32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseClipboard();

    [LibraryImport("User32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EmptyClipboard();

    [LibraryImport("User32.dll")]
    private static partial HANDLE SetClipboardData(uint uFormat, HANDLE hMem);

    [LibraryImport("Kernel32.dll")]
    private static partial HANDLE GlobalAlloc(uint uFlags, nuint dwBytes);

    [LibraryImport("User32.dll")]
    private static partial uint GetClipboardSequenceNumber();

    /// <summary>
    /// Increments on every OS clipboard content change, regardless of format - a reliable way
    /// to tell "still the same write, formats still settling" apart from "something else changed
    /// the clipboard since", which per-format presence checks alone cannot distinguish.
    /// </summary>
    public static uint MGetClipboardSequenceNumber() => GetClipboardSequenceNumber();

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

                    var okDib = SetClipboardData(AdbDataFormats.DeviceIndependentBitmap, CopyToHGlobal(dib)) != 0;
                    var okDibV5 = SetClipboardData(AdbDataFormats.Format17, CopyToHGlobal(dibV5)) != 0;

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

    private static HANDLE CopyToHGlobal(byte[] bytes)
    {
        var hMem = GlobalAlloc(GMEM_MOVEABLE, (nuint)bytes.Length);
        var ptr = MGlobalLock(hMem);
        Marshal.Copy(bytes, 0, ptr, bytes.Length);
        MGlobalUnlock(hMem);

        return hMem;
    }
}
