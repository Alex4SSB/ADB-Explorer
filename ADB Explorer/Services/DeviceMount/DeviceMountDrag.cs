using Vanara.PInvoke;
using static Vanara.PInvoke.User32;

namespace ADB_Explorer.Services;

/// <summary>
/// A device card or tree node dragged out of the app, to open the device's mount where it's dropped: in a new tab of a
/// File Explorer window, or a window of its own over the desktop. Nothing outside the app takes the drag's data, so the
/// drop is told apart by the window under the cursor once the drag ends, as Windows has no drop that opens a folder.
/// </summary>
public static class DeviceMountDrag
{
    public enum DropTarget
    {
        None,
        Desktop,
        ExplorerWindow,
    }

    private const string DRAG_FORMAT = "ADBExplorerDevice";

    // The desktop's window, and the one it moves to while a wallpaper slideshow or animation runs
    private static readonly string[] DESKTOP_WINDOW_CLASSES = ["Progman", "WorkerW"];

    /// <summary>Whether the device can be opened in File Explorer, as the menu's own item allows.</summary>
    public static bool CanDrag(LogicalDeviceViewModel device) => device.OpenInExplorerCommand.IsEnabled;

    /// <summary>Where a drop onto the top-level window (the drag window follows it: <see cref="CopyPasteService.WindowUnderMouse"/>) would open the device.</summary>
    public static DropTarget TargetOf(HWND window)
    {
        if (window.IsNull)
            return DropTarget.None;

        if (DeviceMount.IsExplorerWindow(window))
            return DropTarget.ExplorerWindow;

        var name = new StringBuilder(256);
        if (GetClassName(window, name, name.Capacity) > 0 && DESKTOP_WINDOW_CLASSES.Contains(name.ToString()))
            return DropTarget.Desktop;

        return DropTarget.None;
    }

    /// <summary>
    /// Drags the device until the button's released, showing <paramref name="image"/> where it was grabbed, then opens
    /// the device where it was dropped. Esc or a right click cancels, as does a drop anywhere else.
    /// </summary>
    /// <param name="grabPoint">Where its tree row was grabbed, to hold the dragged row there; null to hold it by the icon.</param>
    public static void Start(UIElement source, LogicalDeviceViewModel device, Point? grabPoint = null)
    {
        if (!CanDrag(device))
            return;

        // The drag window draws the row from its template; its size places it, before it's laid out
        var row = RowSize(device);
        var hold = new Point(ICON_HOLD_X, row.Height / 2);
        if (grabPoint is { } grab)
            hold = new Point(Math.Clamp(grab.X - CHEVRON_WIDTH, ICON_HOLD_X, row.Width - ICON_HOLD_X), Math.Clamp(grab.Y, 0, row.Height));

        var scale = VisualTreeHelper.GetDpi(source as Visual ?? Application.Current.MainWindow).DpiScaleX;
        var imageRect = new Rect(-hold.X * scale, -hold.Y * scale, row.Width * scale, row.Height * scale);

        if (!ContentDrag.Run(source, device, imageRect, new DataObject(DRAG_FORMAT, device.ID), DragDropEffects.Link))
            return;

        // Where the drag window last saw the cursor, as the button came up
        var window = Data.CopyPaste.WindowUnderMouse;
        switch (TargetOf(window))
        {
            case DropTarget.ExplorerWindow:
                DeviceMount.OpenInFileExplorer(device, "", window);
                break;
            case DropTarget.Desktop:
                DeviceMount.OpenInFileExplorer(device, "");
                break;
        }
    }

    private const string ROW_TEMPLATE_KEY = "DeviceDragRowTemplate";

    // The tree row's chevron column, which the dragged row leaves out, and where a row is held by its icon
    private const double CHEVRON_WIDTH = 19;

    private const double ICON_HOLD_X = 16;

    /// <summary>The size of the device's row as the drag window draws it.</summary>
    private static Size RowSize(LogicalDeviceViewModel device)
    {
        var row = new ContentPresenter
        {
            Content = device,
            ContentTemplate = (DataTemplate)Application.Current.FindResource(ROW_TEMPLATE_KEY),
        };
        row.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        return row.DesiredSize;
    }
}
