namespace ADB_Explorer.Services;

/// <summary>
/// An app page (Settings, Devices...) dragged by its navigation item, as a device is by its row. Dropped onto the
/// title bar, it opens in a new tab there (<see cref="Controls.ExplorerTabStrip.ShowNewTabTarget"/>).
/// </summary>
public static class PageDrag
{
    private const string DRAG_FORMAT = "ADBExplorer.Page";

    private const string TEMPLATE_KEY = "PageDragTemplate";

    // Held by its icon, as a device's row is
    private const double ICON_HOLD_X = 16;

    /// <summary>Drags the page until the button's released. Esc or a right click cancels.</summary>
    public static void Start(UIElement source, AdbLocation page)
    {
        var size = DrawnSize(page);
        var scale = VisualTreeHelper.GetDpi(source as Visual ?? Application.Current.MainWindow).DpiScaleX;
        var imageRect = new Rect(-ICON_HOLD_X * scale, -size.Height / 2 * scale, size.Width * scale, size.Height * scale);

        ContentDrag.Run(source, page, imageRect, new DataObject(DRAG_FORMAT, page.Location.ToString()), DragDropEffects.Link);
    }

    /// <summary>The size of the page as the drag window draws it.</summary>
    private static Size DrawnSize(AdbLocation page)
    {
        var drawn = new ContentPresenter
        {
            Content = page,
            ContentTemplate = (DataTemplate)Application.Current.FindResource(TEMPLATE_KEY),
        };
        drawn.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        return drawn.DesiredSize;
    }
}
