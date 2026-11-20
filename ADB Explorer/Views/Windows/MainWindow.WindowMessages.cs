namespace ADB_Explorer.Views.Windows;

using Vanara.PInvoke;
using static Vanara.PInvoke.User32;

public partial class MainWindow
{
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var handle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(handle).AddHook(WndProc);

        Data.RuntimeSettings.MainWindowScalingFactor = NativeMethods.MonitorInfo.GetScalingFromWindow(handle);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);

        Data.RuntimeSettings.MainWindowScalingFactor = (float)(1 / newDpi.DpiScaleX);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Empty title bar space is the window's caption, so WPF never sees a mouse-down there.
        if ((WindowMessage)msg is WindowMessage.WM_NCLBUTTONDOWN && wParam == (nint)HitTestValues.HTCAPTION)
            UnfocusNavigationRow();

        return IntPtr.Zero;
    }
}
