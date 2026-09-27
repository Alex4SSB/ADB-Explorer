namespace ADB_Explorer.Services;

/// <summary>
/// DWM helpers for window chrome: frame recomposite nudges and border color (the Win11 stand-in
/// for the soft window shadow, which high-contrast themes replace with a solid outline).
/// </summary>
internal static class WindowCompositionRefresh
{
    /// <summary>DWMWA_BORDER_COLOR — paints the window outline that replaces the soft shadow.</summary>
    private const int DWMWA_BORDER_COLOR = 34;

    /// <summary>DWMWA_COLOR_DEFAULT — restore the system default border color.</summary>
    private const uint DWMWA_COLOR_DEFAULT = 0xFFFFFFFF;

    public static void ApplyBorderColor(Window window, Color color)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        if (hwnd == IntPtr.Zero)
            return;

        // COLORREF is 0x00BBGGRR - always non-negative, so it fits the same uint the native call expects.
        uint colorRef = (uint)((color.B << 16) | (color.G << 8) | color.R);
        _ = DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref colorRef, sizeof(uint));
    }

    public static void ResetBorderColor(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        if (hwnd == IntPtr.Zero)
            return;

        uint color = DWMWA_COLOR_DEFAULT;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref color, sizeof(uint));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref uint attrValue, int attrSize);
}
