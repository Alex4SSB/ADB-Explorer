namespace ADB_Explorer.Services;

#pragma warning disable SYSLIB1054 // Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time

public static partial class NativeMethods
{
    private static Action _externalClipAction;
    private static HANDLE _clipWindowHandle = IntPtr.Zero;

    public static void InitInterceptCB(Window window, Action action)
    {
        _externalClipAction = action;
        RoutedEventHandler handler = null;

        handler = (object sender, RoutedEventArgs e) =>
        {
            _clipWindowHandle = new WindowInteropHelper(window).Handle;

            var hwndSource = HwndSource.FromHwnd(_clipWindowHandle);
            hwndSource.AddHook(WndProc);

            AddClipboardFormatListener(_clipWindowHandle);

            window.Loaded -= handler;
        };

        window.Loaded += handler;
    }

    public static void CloseInterceptCB()
    {
        RemoveClipboardFormatListener(_clipWindowHandle);
    }

    private static HANDLE WndProc(HANDLE hwnd, int msg, HANDLE wParam, HANDLE lParam, ref bool handled)
    {
        if ((ClipboardNotificationMessage)msg is ClipboardNotificationMessage.WM_CLIPBOARDUPDATE)
        {
            _externalClipAction();
            handled = true;
        }

        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(HANDLE hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RemoveClipboardFormatListener(HANDLE hwnd);
}
