namespace ADB_Explorer.Services;

internal static class MonitorInfo
{
    private enum MonitorType
    {
        Primary = 0x00000001,
        Nearest = 0x00000002,
    }

    private static IntPtr? handler = null;
    private static IntPtr primaryMonitor => MonitorFromWindow(IntPtr.Zero, (Int32)MonitorType.Primary);


    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr handle, Int32 flags);

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
}
