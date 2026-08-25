using ADB_Explorer.Models;
using ADB_Explorer.Services;
using static ADB_Explorer.Models.AdbExplorerConst;

namespace ADB_Explorer.Helpers;

/// <summary>
/// Vertical auto-scroll for the navigation tree and explorer listing while an OLE drag is active.
/// </summary>
internal static class DragAutoScroll
{
    private static readonly HashSet<ScrollViewer> Viewers = [];
    private static DispatcherTimer? _timer;

    public static void Register(ScrollViewer? viewer)
    {
        if (viewer is not null)
            Viewers.Add(viewer);
    }

    public static void Unregister(ScrollViewer? viewer)
    {
        if (viewer is not null)
            Viewers.Remove(viewer);
    }

    public static void Begin()
    {
        _timer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        _timer.Tick -= OnTick;
        _timer.Tick += OnTick;
        _timer.Start();
    }

    public static void End()
    {
        _timer?.Stop();
    }

    public static void OnMouseWheel(int wheelDelta)
    {
        if (Data.CopyPaste.DragStatus is not CopyPasteService.DragState.Active)
            return;

        var viewer = ViewerUnderCursor();
        if (viewer is null || !CanScrollVertically(viewer))
            return;

        var step = Math.Max(48, viewer.ViewportHeight * 0.15);
        viewer.ScrollToVerticalOffset(viewer.VerticalOffset - Math.Sign(wheelDelta) * step);
    }

    private static void OnTick(object? sender, EventArgs e)
    {
        if (Data.CopyPaste.DragStatus is not CopyPasteService.DragState.Active)
        {
            End();
            return;
        }

        var viewer = ViewerUnderCursor();
        if (viewer is null || !CanScrollVertically(viewer))
            return;

        var pos = CursorInViewer(viewer);
        if (pos is null)
            return;

        ScrollFromEdge(viewer, pos.Value);
    }

    private static ScrollViewer? ViewerUnderCursor()
    {
        foreach (var viewer in Viewers)
        {
            if (viewer.IsVisible && CursorInViewer(viewer) is not null)
                return viewer;
        }

        return null;
    }

    private static Point? CursorInViewer(ScrollViewer viewer)
    {
        Point pos;
        try
        {
            pos = viewer.PointFromScreen(NativeMethods.InterceptMouse.GetCursorPosition());
        }
        catch (InvalidOperationException)
        {
            return null;
        }

        if (pos.X < 0 || pos.Y < 0 || pos.X > viewer.ActualWidth || pos.Y > viewer.ActualHeight)
            return null;

        return pos;
    }

    private static void ScrollFromEdge(ScrollViewer viewer, Point pos)
    {
        var height = viewer.ViewportHeight;
        if (height <= 0)
            return;

        double delta = 0;
        if (pos.Y < DRAG_AUTO_SCROLL_EDGE)
        {
            var t = 1 - Math.Clamp(pos.Y / DRAG_AUTO_SCROLL_EDGE, 0, 1);
            delta = -DRAG_AUTO_SCROLL_MAX_STEP * t;
        }
        else if (pos.Y > height - DRAG_AUTO_SCROLL_EDGE)
        {
            var t = 1 - Math.Clamp((height - pos.Y) / DRAG_AUTO_SCROLL_EDGE, 0, 1);
            delta = DRAG_AUTO_SCROLL_MAX_STEP * t;
        }

        if (delta == 0)
            return;

        viewer.ScrollToVerticalOffset(viewer.VerticalOffset + delta);
    }

    private static bool CanScrollVertically(ScrollViewer viewer)
        => viewer.ScrollableHeight > 0
        && viewer.ComputedVerticalScrollBarVisibility is Visibility.Visible;
}
