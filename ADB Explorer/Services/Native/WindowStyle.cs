namespace ADB_Explorer.Services;

public static class WindowStyle
{
    public static void SetWindowHidden(HANDLE hwnd)
    {
        var style = GetWindowLong(hwnd, NativeMethods.WindowIndex.GWL_EXSTYLE)
            | NativeMethods.ExtendedWindowStyle.WS_EX_TOOLWINDOW
            | NativeMethods.ExtendedWindowStyle.WS_EX_NOACTIVATE
            | NativeMethods.ExtendedWindowStyle.WS_EX_TRANSPARENT;

        SetWindowLong(hwnd, NativeMethods.WindowIndex.GWL_EXSTYLE, style);
    }

    /// <summary>Puts the window above every non-topmost one, which the main window may have risen over since it was shown.</summary>
    public static void BringToTop(HANDLE hwnd)
    {
        const uint SWP_NOSIZE = 0x0001;
        const uint SWP_NOMOVE = 0x0002;
        const uint SWP_NOACTIVATE = 0x0010;

        SetWindowPos(hwnd, new HANDLE(-1), 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(HANDLE hWnd, HANDLE hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern NativeMethods.ExtendedWindowStyle GetWindowLong(HANDLE hWnd, NativeMethods.WindowIndex nIndex);

    
    [DllImport("user32.dll")]
    private static extern int SetWindowLong(HANDLE hWnd, NativeMethods.WindowIndex nIndex, NativeMethods.ExtendedWindowStyle dwNewLong);
}
