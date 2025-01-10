using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;

namespace ADB_Explorer;

/// <summary>
/// Interaction logic for DragWindow.xaml
/// </summary>
public partial class DragWindow
{
    private readonly DispatcherTimer DragTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };

    public bool MouseWithinApp;

    private HANDLE windowHandle;

    public DragWindow()
    {
        InitializeComponent();

        DragTimer.Tick += DragTimer_Tick;

#if !DEPLOY
        MainBorder.BorderThickness = new Thickness(1);
        MainBorder.BorderBrush = Brushes.OrangeRed;
#endif
    }

    private void DragTimer_Tick(object sender, EventArgs e)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            if (Data.CopyPaste.DragFiles.Length > 0 && Data.CopyPaste.CurrentDropEffect is not DragDropEffects.None)
            {
                var target = MouseWithinApp
                    ? " to " + FileHelper.GetFullName(Data.CopyPaste.DropTarget)
                    : "";

                DragTooltip.Text = $" {Data.CopyPaste.DragFiles.Length} item(s){target}";
            }
            else
                DragTooltip.Text = "";
        });
    }

    private string processUnderMouse = "";

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        windowHandle = new WindowInteropHelper(this).Handle;

#if DEBUG
                MouseWithinApp = true;
#else
        NativeMethods.InterceptMouse.Init(NativeMethods.MouseMessages.WM_MOUSEMOVE,
            point =>
            {
                if (Data.RuntimeSettings.DragBitmap is null)
                    return;

                var actualPoint = NativeMethods.MonitorInfo.MousePositionToDpi(point, windowHandle);

                Top = actualPoint.Y - DragImage.ActualHeight - 4;
                Left = actualPoint.X - DragImage.ActualWidth / 2;

                MouseWithinApp = NativeMethods.MonitorInfo.IsPointInMainWin(point);
                if (!MouseWithinApp)
                {
                    if (Data.CopyPaste.DragStatus is CopyPasteService.DragState.None)
                        Data.RuntimeSettings.DragBitmap = null;

                    if (NativeMethods.InterceptMouse.GetPidFromPoint() is int pid
                        && Process.GetProcessById(pid) is Process proc)
                    {
                        processUnderMouse = proc.ProcessName;
                        Data.RuntimeSettings.DragWithinSlave = processUnderMouse == Properties.Resources.AppDisplayName;
                    }
                    else
                        processUnderMouse = "";
                }
                else
                    Data.RuntimeSettings.DragWithinSlave = false;
            });

#endif

        DragTimer.Start();
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
#if !DEBUG
        NativeMethods.InterceptMouse.Close();
#endif
    }

    private void Border_MouseUp(object sender, MouseButtonEventArgs e)
    {
        Data.RuntimeSettings.DragBitmap = null;
    }
}

