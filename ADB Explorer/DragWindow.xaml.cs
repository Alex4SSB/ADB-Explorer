using ADB_Explorer.Models;
using ADB_Explorer.Services;

namespace ADB_Explorer;

/// <summary>
/// Interaction logic for DragWindow.xaml
/// </summary>
public partial class DragWindow : Window
{
    public DragWindow()
    {
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        NativeMethods.InitInterceptMouse(NativeMethods.MouseMessages.WM_MOUSEMOVE,
            (int x, int y) =>
            {
                if (Data.RuntimeSettings.DragBitmap is null)
                    return;
                
                Top = y - (ActualHeight / 2);
                Left = x - (ActualWidth / 2);
            });
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        NativeMethods.CloseInterceptMouse();
    }

    private void Border_MouseUp(object sender, MouseButtonEventArgs e)
    {
        Data.RuntimeSettings.DragBitmap = null;
    }
}

