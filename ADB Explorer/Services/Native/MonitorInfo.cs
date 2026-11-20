namespace ADB_Explorer.Services;

using Vanara.PInvoke;
using static Vanara.PInvoke.SHCore;
using static Vanara.PInvoke.User32;

public static partial class NativeMethods
{
    public static class MonitorInfo
    {
        public static HMONITOR PrimaryMonitor() => MonitorFromWindow(IntPtr.Zero, MonitorFlags.MONITOR_DEFAULTTOPRIMARY);

        /// <summary>
        /// Converts a mouse position from device-independent pixels (DIPs) to device pixels using the specified scaling
        /// factor.
        /// </summary>
        /// <remarks>Use this method when translating coordinates from logical (DIP) space to physical
        /// device pixels, such as when handling high-DPI displays.</remarks>
        /// <param name="mousePosition">The mouse position in device-independent pixels (DIPs) to convert.</param>
        /// <param name="scalingFactor">The scaling factor, where 1 = 100%, 0.8 = 125%, etc.</param>
        /// <returns>A POINT structure representing the mouse position in device pixels after applying the scaling factor.</returns>
        public static POINT MousePositionToDpi(POINT mousePosition, float scalingFactor) => new(
            (int)(mousePosition.X * scalingFactor),
            (int)(mousePosition.Y * scalingFactor));

        /// <summary>
        /// Calculates the scaling factor for a specified window based on its DPI setting.
        /// </summary>
        /// <remarks>If the window handle is invalid or the DPI cannot be determined, the method assumes a
        /// default DPI of 96.</remarks>
        /// <param name="hWnd">A handle to the window for which to retrieve the scaling factor.</param>
        /// <returns>The scaling factor as a floating-point value. A value of 1.0 indicates 100% scaling; values less than 1.0
        /// indicate higher DPI (scaled down), and values greater than 1.0 indicate lower DPI (scaled up).</returns>
        public static float GetScalingFromWindow(HWND hWnd)
            => DpiToScalingFactor(GetDpiForWindow(hWnd));

        public static uint PrimaryMonitorDpi()
        {
            var result = GetDpiForMonitor(PrimaryMonitor(), MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out var dpiX, out _);
            return result == HRESULT.S_OK ? dpiX : 96u;
        }

        public static float DpiToScalingFactor(uint dpi) =>
            dpi == 0
            ? 1
            : 96f / dpi;
    }
}
