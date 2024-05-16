namespace ADB_Explorer.Services;

using HANDLE = IntPtr;

internal static class MonitorInfo
{
    private enum MonitorType
    {
        Primary = 0x00000001,
        Nearest = 0x00000002,
    }

    private static HANDLE? handler = null;
    private static HANDLE primaryMonitor => MonitorFromWindow(IntPtr.Zero, (Int32)MonitorType.Primary);


    [DllImport("user32.dll")]
    private static extern HANDLE MonitorFromWindow(HANDLE handle, Int32 flags);

    public static void Init(Window window)
    {
        if (handler is null)
            handler = new WindowInteropHelper(window).EnsureHandle();
    }

    public static bool? IsPrimaryMonitor(Window window)
    {
        Init(window);
        return IsPrimaryMonitor();
    }

    public static bool? IsPrimaryMonitor()
    {
        if (handler is null)
            return null;

        var current = MonitorFromWindow(handler.Value, (Int32)MonitorType.Nearest);

        return current == primaryMonitor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public Int32 X;
        public Int32 Y;

        public POINT(int x, int y)
        {
            X = x;
            Y = y;
        }

        public static implicit operator Point(POINT self)
            => new(self.X, self.Y);
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    public static bool IsMouseWithinElement(FrameworkElement element)
    {
        GetCursorPos(out var point);

        var relativePoint = element.PointFromScreen(point);

        return new Rect(new(element.ActualWidth, element.ActualHeight)).Contains(relativePoint);
    }
}
