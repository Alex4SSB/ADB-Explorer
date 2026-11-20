namespace ADB_Explorer.Services;

using Vanara.PInvoke;
using static Vanara.PInvoke.User32;

public static partial class NativeMethods
{
    public sealed class ClipboardHook : IDisposable
    {
        private static Action _externalClipAction = null!;
        private static HwndSource _hwndSource = null!;
        private static bool _clipUpdatePending;

        public static HWND MainWindowHandle { get; private set; } = HWND.NULL;

        public static void Init(Window window, Action clipboardAction)
        {
            _externalClipAction = clipboardAction;
            RoutedEventHandler? windowLoadedHandler = null;

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

                _hwndSource = HwndSource.FromHwnd((IntPtr)MainWindowHandle);
                _hwndSource.AddHook(WndProc);

                AddClipboardFormatListener(MainWindowHandle);
            }
        }

        public static void Close()
        {
            RemoveClipboardFormatListener(MainWindowHandle);
            _hwndSource?.RemoveHook(WndProc);
            _hwndSource?.Dispose();
        }

        private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if ((WindowMessage)msg is not WindowMessage.WM_CLIPBOARDUPDATE)
                return IntPtr.Zero;

            // Owners often set the clipboard several times in a row, so a burst is read once,
            // after the owner is done, instead of inside the message (where opening it can fail).
            if (!_clipUpdatePending)
            {
                _clipUpdatePending = true;
                _hwndSource.Dispatcher.BeginInvoke(DispatcherPriority.Background, ReadClipboard);
            }

            handled = true;
            return IntPtr.Zero;
        }

        private static void ReadClipboard()
        {
            _clipUpdatePending = false;
            _externalClipAction();
        }

        public void Dispose() => Close();
    }
}
