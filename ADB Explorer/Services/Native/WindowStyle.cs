namespace ADB_Explorer.Services;

using Vanara.PInvoke;
using static Vanara.PInvoke.User32;

public static class WindowStyle
{
    public static void SetWindowHidden(HWND hwnd)
    {
        var style = (WindowStylesEx)GetWindowLong(hwnd, WindowLongFlags.GWL_EXSTYLE)
            | WindowStylesEx.WS_EX_TOOLWINDOW
            | WindowStylesEx.WS_EX_NOACTIVATE
            | WindowStylesEx.WS_EX_TRANSPARENT;

        SetWindowLong(hwnd, WindowLongFlags.GWL_EXSTYLE, (int)style);
    }

    /// <summary>Puts the window above every non-topmost one, which the main window may have risen over since it was shown.</summary>
    public static void BringToTop(HWND hwnd)
        => SetWindowPos(hwnd, HWND.HWND_TOPMOST, 0, 0, 0, 0,
            SetWindowPosFlags.SWP_NOMOVE | SetWindowPosFlags.SWP_NOSIZE | SetWindowPosFlags.SWP_NOACTIVATE);
}
