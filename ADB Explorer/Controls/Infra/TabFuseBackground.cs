namespace ADB_Explorer.Controls;

/// <summary>
/// The active tab's body: rounded top corners, and concave flares at the bottom corners that run
/// into the surface below. The flares reach <see cref="FlareRadius"/> past the element's own width.
/// </summary>
public class TabFuseBackground : FrameworkElement
{
    /// <summary>Anti-aliasing sliver, in device pixels, left uncovered so the shape doesn't spill onto the surface and double its tint.</summary>
    private const double UNCOVERED_SLIVER_PX = 0.15;

    public static readonly DependencyProperty CoveredBorderThicknessProperty =
        DependencyProperty.Register(nameof(CoveredBorderThickness), typeof(double), typeof(TabFuseBackground),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty =
        DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(TabFuseBackground),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty =
        DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(TabFuseBackground),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TopRadiusProperty =
        DependencyProperty.Register(nameof(TopRadius), typeof(double), typeof(TabFuseBackground),
            new FrameworkPropertyMetadata(10d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FlareRadiusProperty =
        DependencyProperty.Register(nameof(FlareRadius), typeof(double), typeof(TabFuseBackground),
            new FrameworkPropertyMetadata(8d, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Thickness (DIPs) of a border on the surface below that this shape reaches down over; the overlap is derived from the DPI.</summary>
    public double CoveredBorderThickness
    {
        get => (double)GetValue(CoveredBorderThicknessProperty);
        set => SetValue(CoveredBorderThicknessProperty, value);
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

    public double TopRadius
    {
        get => (double)GetValue(TopRadiusProperty);
        set => SetValue(TopRadiusProperty, value);
    }

    public double FlareRadius
    {
        get => (double)GetValue(FlareRadiusProperty);
        set => SetValue(FlareRadiusProperty, value);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);

        InvalidateVisual();
    }

    /// <summary>The device-pixel rows the covered border occupies (1.25px at 125% is 1 row, 1.5px at 150% is 2),
    /// in DIPs, less a sliver where the edge isn't pixel-aligned (only at fractional scales).</summary>
    private double BorderOverlap()
    {
        if (CoveredBorderThickness <= 0)
            return 0;

        var scale = VisualTreeHelper.GetDpi(this).DpiScaleY;
        var rows = Math.Max(1, Math.Round(CoveredBorderThickness * scale, MidpointRounding.AwayFromZero));
        var sliver = scale % 1 == 0 ? 0 : UNCOVERED_SLIVER_PX;

        return (rows - sliver) / scale;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        double width = ActualWidth, height = ActualHeight;
        if (width <= 0 || height <= 0)
            return;

        var bottom = height + BorderOverlap();

        var top = Math.Min(TopRadius, Math.Min(width, height) / 2);
        var flare = Math.Min(FlareRadius, bottom - top);

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            // Left flare up the left wall, across the rounded top, then down the right wall.
            // The figure is left open so the stroke skips the bottom edge, which joins the surface.
            context.BeginFigure(new Point(-flare, bottom), true, false);
            context.ArcTo(new Point(0, bottom - flare), new Size(flare, flare), 0, false, SweepDirection.Counterclockwise, true, true);
            context.LineTo(new Point(0, top), true, true);
            context.ArcTo(new Point(top, 0), new Size(top, top), 0, false, SweepDirection.Clockwise, true, true);
            context.LineTo(new Point(width - top, 0), true, true);
            context.ArcTo(new Point(width, top), new Size(top, top), 0, false, SweepDirection.Clockwise, true, true);
            context.LineTo(new Point(width, bottom - flare), true, true);
            context.ArcTo(new Point(width + flare, bottom), new Size(flare, flare), 0, false, SweepDirection.Counterclockwise, true, true);
        }

        geometry.Freeze();

        var pen = Stroke is null ? null : new Pen(Stroke, 1);
        drawingContext.DrawGeometry(Fill, pen, geometry);
    }
}
