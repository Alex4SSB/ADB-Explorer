namespace ADB_Explorer.Controls;

/// <summary>
/// A handle growing out of the edge of a pane: rounded corners on its free side, and concave flares
/// where it meets the edge, like a tab's. The flares reach <see cref="FlareRadius"/> past the element's
/// own extent along the edge, and the edge itself is left unstroked.
/// </summary>
public class PaneHandleBackground : FrameworkElement
{
    public static readonly DependencyProperty AttachedEdgeProperty =
        DependencyProperty.Register(nameof(AttachedEdge), typeof(Dock), typeof(PaneHandleBackground),
            new FrameworkPropertyMetadata(Dock.Left, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty =
        DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(PaneHandleBackground),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty =
        DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(PaneHandleBackground),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.Register(nameof(CornerRadius), typeof(double), typeof(PaneHandleBackground),
            new FrameworkPropertyMetadata(6d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FlareRadiusProperty =
        DependencyProperty.Register(nameof(FlareRadius), typeof(double), typeof(PaneHandleBackground),
            new FrameworkPropertyMetadata(6d, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The side of the element that joins the pane; the handle grows away from it.</summary>
    public Dock AttachedEdge
    {
        get => (Dock)GetValue(AttachedEdgeProperty);
        set => SetValue(AttachedEdgeProperty, value);
    }

    public Brush? Fill
    {
        get => (Brush?)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public Brush? Stroke
    {
        get => (Brush?)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    /// <summary>Radius of the two corners on the side away from the pane.</summary>
    public double CornerRadius
    {
        get => (double)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public double FlareRadius
    {
        get => (double)GetValue(FlareRadiusProperty);
        set => SetValue(FlareRadiusProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        double width = ActualWidth, height = ActualHeight;
        if (width <= 0 || height <= 0)
            return;

        var horizontalEdge = AttachedEdge is Dock.Top or Dock.Bottom;

        // Drawn as if attached to a left edge - depth across the handle, length along it - then turned into place.
        var depth = horizontalEdge ? height : width;
        var length = horizontalEdge ? width : height;
        var corner = Math.Min(CornerRadius, Math.Min(depth, length) / 2);
        var flare = FlareRadius;

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(0, -flare), true, false);
            context.ArcTo(new Point(flare, 0), new Size(flare, flare), 0, false, SweepDirection.Counterclockwise, true, true);
            context.LineTo(new Point(depth - corner, 0), true, true);
            context.ArcTo(new Point(depth, corner), new Size(corner, corner), 0, false, SweepDirection.Clockwise, true, true);
            context.LineTo(new Point(depth, length - corner), true, true);
            context.ArcTo(new Point(depth - corner, length), new Size(corner, corner), 0, false, SweepDirection.Clockwise, true, true);
            context.LineTo(new Point(flare, length), true, true);
            context.ArcTo(new Point(0, length + flare), new Size(flare, flare), 0, false, SweepDirection.Counterclockwise, true, true);
        }

        geometry.Freeze();

        var placement = AttachedEdge switch
        {
            Dock.Right => new Matrix(-1, 0, 0, 1, width, 0),
            Dock.Top => new Matrix(0, 1, 1, 0, 0, 0),
            Dock.Bottom => new Matrix(0, -1, 1, 0, 0, height),
            _ => Matrix.Identity,
        };

        var pen = Stroke is null ? null : new Pen(Stroke, 1);

        drawingContext.PushTransform(new MatrixTransform(placement));
        drawingContext.DrawGeometry(Fill, pen, geometry);
        drawingContext.Pop();
    }
}
