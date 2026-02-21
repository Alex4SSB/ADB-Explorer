namespace ADB_Explorer.Services;

public static class WindowStyle
{
    public static void SetWindowHidden(HANDLE hwnd)
    {
        var style = GetWindowLong(hwnd, NativeMethods.WindowIndex.GWL_EXSTYLE)
            | NativeMethods.ExtendedWindowStyle.WS_EX_TOOLWINDOW
            | NativeMethods.ExtendedWindowStyle.WS_EX_NOACTIVATE;

        SetWindowLong(hwnd, NativeMethods.WindowIndex.GWL_EXSTYLE, style);
    }

    [DllImport("user32.dll")]
    private static extern NativeMethods.ExtendedWindowStyle GetWindowLong(HANDLE hWnd, NativeMethods.WindowIndex nIndex);

    
    [DllImport("user32.dll")]
    private static extern int SetWindowLong(HANDLE hWnd, NativeMethods.WindowIndex nIndex, NativeMethods.ExtendedWindowStyle dwNewLong);
}
