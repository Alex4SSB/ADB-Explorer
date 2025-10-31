using ADB_Explorer.Models;

namespace ADB_Explorer.Services;

#pragma warning disable SYSLIB1054 // Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time

public static partial class NativeMethods
{
    public sealed class InterceptClipboard : IDisposable
    {
        private static Action _externalClipAction;
        private static Action<string> _externalIpcAction;
        private static HwndSource _hwndSource;

        public static HANDLE MainWindowHandle { get; private set; } = IntPtr.Zero;

        public static void Init(Window window, Action clipboardAction, Action<string> ipcAction)
        {
            _externalClipAction = clipboardAction;
            _externalIpcAction = ipcAction;
            RoutedEventHandler windowLoadedHandler = null;
            PropertyChangedEventHandler driveViewHandler = null;

            driveViewHandler = (sender, e) =>
            {
                if (e.PropertyName == nameof(Data.RuntimeSettings.DriveViewNav) && Data.RuntimeSettings.DriveViewNav)
                {
                    Data.RuntimeSettings.PropertyChanged -= driveViewHandler;
                }
            };

            Data.RuntimeSettings.PropertyChanged += driveViewHandler;

            windowLoadedHandler = (sender, e) =>
            {
                MainWindowHandle = new WindowInteropHelper(window).Handle;

                _hwndSource = HwndSource.FromHwnd(MainWindowHandle);
                _hwndSource.AddHook(WndProc);

                AddClipboardFormatListener(MainWindowHandle);

                window.Loaded -= windowLoadedHandler;
            };

            window.Loaded += windowLoadedHandler;
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
            // Since we already have a hook for MainWindow, we'll use it for IPC as well
            {
                var cds = Marshal.PtrToStructure<COPYDATASTRUCT>(lParam);
                _externalIpcAction(cds.lpData);
            }

            return IntPtr.Zero;
        }

        [DllImport("User32.dll", SetLastError = true)]
        private static extern bool AddClipboardFormatListener(HANDLE hwnd);

        [DllImport("User32.dll", SetLastError = true)]
        private static extern bool RemoveClipboardFormatListener(HANDLE hwnd);

        public void Dispose() => Close();
    }
}
