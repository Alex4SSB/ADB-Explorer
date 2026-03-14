namespace ADB_Explorer.Services;

public static partial class NativeMethods
{
    public sealed partial class InterceptClipboard : IDisposable
    {
        private static Action _externalClipAction;
        private static Action<string> _externalIpcAction;
        private static Action<float> _externalScalingAction;
        private static HwndSource _hwndSource;

        public static HANDLE MainWindowHandle { get; private set; } = IntPtr.Zero;

        public static void Init(Window window, Action clipboardAction, Action<string> ipcAction, Action<float> scalingAction)
        {
            _externalClipAction = clipboardAction;
            _externalIpcAction = ipcAction;
            _externalScalingAction = scalingAction;
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
            else if ((WindowMessages)msg is WindowMessages.WM_COPYDATA)
            {
                var cds = Marshal.PtrToStructure<COPYDATASTRUCT>(lParam);
                _externalIpcAction(cds.lpData);
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
