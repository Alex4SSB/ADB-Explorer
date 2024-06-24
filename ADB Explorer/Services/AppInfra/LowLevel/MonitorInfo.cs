namespace ADB_Explorer.Services;

internal static class MonitorInfo
{
    private static HANDLE? handler = null;
    private static HANDLE primaryMonitor => NativeMethods.PrimaryMonitor();

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

        var current = NativeMethods.NearestMonitor(handler.Value);

        return current == primaryMonitor;
    }

    public static bool IsMouseWithinElement(FrameworkElement element)
    {
        var point = NativeMethods.GetCursorPos();

        var relativePoint = element.PointFromScreen(point);

        return new Rect(new(element.ActualWidth, element.ActualHeight)).Contains(relativePoint);
    }
}
