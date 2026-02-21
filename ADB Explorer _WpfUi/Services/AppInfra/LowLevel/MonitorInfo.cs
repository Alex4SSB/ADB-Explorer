using ADB_Explorer.Models;

namespace ADB_Explorer.Services;

public static partial class NativeMethods
{
    public static class MonitorInfo
    {
        private enum MonitorType
        {
            Primary = 0x00000001,
            Nearest = 0x00000002,
        }

        private static HANDLE? mainWinHandle;
        private static HANDLE primaryMonitor => PrimaryMonitor();

        public static void Init(Window window)
        {
            mainWinHandle ??= new WindowInteropHelper(window).EnsureHandle();
        }

        public static bool? IsPrimaryMonitor(Window window)
        {
            Init(window);
            return IsPrimaryMonitor();
        }

        public static bool? IsPrimaryMonitor()
        {
            if (mainWinHandle is null)
                return null;

            var current = NearestMonitor(mainWinHandle.Value);

            return current == primaryMonitor;
        }

        public static HANDLE NearestMonitor(HANDLE handle) => MonitorFromWindow(handle, MonitorType.Nearest);

        public static HANDLE PrimaryMonitor() => MonitorFromWindow(IntPtr.Zero, MonitorType.Primary);

        public static POINT MousePositionToDpi(POINT mousePosition, HANDLE hWnd)
        {
            // Get the DPI for this window
            var dpi = GetDpiForWindow(hWnd);
            if (dpi == 0) // fallback to default DPI if window not found
                dpi = 96;

            // Calculate the scaling factor (96 is the default DPI)
            Data.RuntimeSettings.DpiScalingFactor = 96f / dpi;

            // Convert the coordinates
            return new(
                (int)(mousePosition.X * Data.RuntimeSettings.DpiScalingFactor),
                (int)(mousePosition.Y * Data.RuntimeSettings.DpiScalingFactor));
        }

        [DllImport("User32.dll")]
        private static extern HANDLE MonitorFromWindow(HANDLE hwnd, MonitorType dwFlags);

        [DllImport("User32.dll")]
        static extern uint GetDpiForWindow(HANDLE hwnd);
    }
}
