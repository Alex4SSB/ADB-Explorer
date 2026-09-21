namespace ADB_Explorer.Services;

public static partial class NativeMethods
{
    public sealed partial class InterceptClipboard : IDisposable
    {
        private static Action _externalClipAction = null!;
        private static Action<float> _externalScalingAction = null!;
        private static Action _externalCaptionClickAction = null!;
        private static HwndSource _hwndSource = null!;

        public static HANDLE MainWindowHandle { get; private set; } = IntPtr.Zero;

        private const int HTCAPTION = 2;

        public static void Init(Window window, Action clipboardAction, Action<float> scalingAction, Action captionClickAction)
        {
            _externalClipAction = clipboardAction;
            _externalScalingAction = scalingAction;
            _externalCaptionClickAction = captionClickAction;
            RoutedEventHandler windowLoadedHandler = null;

            if (window.IsLoaded)
            {
                GetMainWindowHandle(window);
            }
            else
            {
                windowLoadedHandler = (sender, e) =>
                {
                    GetMainWindowHandle(window);

                    window.Loaded -= windowLoadedHandler;
                };

                window.Loaded += windowLoadedHandler;
            }

            static void GetMainWindowHandle(Window window)
            {
                MainWindowHandle = new WindowInteropHelper(window).Handle;

                _hwndSource = HwndSource.FromHwnd(MainWindowHandle);
                _hwndSource.AddHook(WndProc);

                AddClipboardFormatListener(MainWindowHandle);

                _externalScalingAction(MonitorInfo.GetScalingFromWindow(MainWindowHandle));
            }
        }

        public static void Close()
        {
            RemoveClipboardFormatListener(MainWindowHandle);
            _hwndSource?.RemoveHook(WndProc);
            _hwndSource?.Dispose();
        }

        private static HANDLE WndProc(HANDLE hwnd, int msg, HANDLE wParam, HANDLE lParam, ref bool handled)
        {
            if ((ClipboardNotificationMessage)msg is ClipboardNotificationMessage.WM_CLIPBOARDUPDATE)
            {
                _externalClipAction();
                handled = true;
            }
            // The HIWORD of the wParam contains the Y-axis value of the new dpi of the window.
            // The LOWORD of the wParam contains the X-axis value of the new DPI of the window.
            // For example, 96, 120, 144, or 192.
            // The values of the X-axis and the Y-axis are identical for Windows apps.
            else if ((WindowMessages)msg is WindowMessages.WM_DPICHANGED)
            {
                var point = (UInt16)wParam;
                _externalScalingAction(MonitorInfo.DpiToScalingFactor(point));
            }
            // Empty title bar space is the window's caption, so WPF never sees a mouse-down there.
            else if ((WindowMessages)msg is WindowMessages.WM_NCLBUTTONDOWN && wParam == HTCAPTION)
            {
                _externalCaptionClickAction();
            }

            return IntPtr.Zero;
        }

        [LibraryImport("User32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool AddClipboardFormatListener(HANDLE hwnd);

        [LibraryImport("User32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool RemoveClipboardFormatListener(HANDLE hwnd);

        public void Dispose() => Close();
    }
}
