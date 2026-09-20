namespace ADB_Explorer.Controls;

/// <summary>
/// Lays tabs out horizontally, every one the same width: <see cref="MaxTabWidth"/> when they all
/// fit, else the available width split evenly (never below <see cref="MinTabWidth"/>).
/// </summary>
public class TabStripPanel : Panel
{
    public static readonly DependencyProperty MaxTabWidthProperty =
        DependencyProperty.Register(nameof(MaxTabWidth), typeof(double), typeof(TabStripPanel),
            new FrameworkPropertyMetadata(240d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty MinTabWidthProperty =
        DependencyProperty.Register(nameof(MinTabWidth), typeof(double), typeof(TabStripPanel),
            new FrameworkPropertyMetadata(48d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Widest a tab's visible body may get, excluding its container's own margin.</summary>
    public double MaxTabWidth
    {
        get => (double)GetValue(MaxTabWidthProperty);
        set => SetValue(MaxTabWidthProperty, value);
    }

    public double MinTabWidth
    {
        get => (double)GetValue(MinTabWidthProperty);
        set => SetValue(MinTabWidthProperty, value);
    }

    public static readonly DependencyProperty SeparatorBrushProperty =
        DependencyProperty.Register(nameof(SeparatorBrush), typeof(Brush), typeof(TabStripPanel),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SeparatorHeightProperty =
        DependencyProperty.Register(nameof(SeparatorHeight), typeof(double), typeof(TabStripPanel),
            new FrameworkPropertyMetadata(16d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SeparatorBottomInsetProperty =
        DependencyProperty.Register(nameof(SeparatorBottomInset), typeof(double), typeof(TabStripPanel),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Drawn between every two neighboring tabs that are both unselected.</summary>
    public Brush? SeparatorBrush
    {
        get => (Brush?)GetValue(SeparatorBrushProperty);
        set => SetValue(SeparatorBrushProperty, value);
    }

    public double SeparatorHeight
    {
        get => (double)GetValue(SeparatorHeightProperty);
        set => SetValue(SeparatorHeightProperty, value);
    }

    /// <summary>Space at the bottom of the panel that the tabs' bodies don't use, so separators center on the bodies.</summary>
    public double SeparatorBottomInset
    {
        get => (double)GetValue(SeparatorBottomInsetProperty);
        set => SetValue(SeparatorBottomInsetProperty, value);
    }

    private double slotWidth;

    public TabStripPanel()
    {
        SnapsToDevicePixels = true;

        // Which neighbors get a separator depends on the selection.
        AddHandler(Selector.SelectedEvent, new RoutedEventHandler((_, _) => InvalidateVisual()));
        AddHandler(Selector.UnselectedEvent, new RoutedEventHandler((_, _) => InvalidateVisual()));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var count = InternalChildren.Count;
        if (count == 0)
        {
            slotWidth = 0;
            return default;
        }

        var margin = InternalChildren[0] is FrameworkElement { Margin: var m } ? m.Left + m.Right : 0;
        var max = MaxTabWidth + margin;
        var min = Math.Min(MinTabWidth + margin, max);

        slotWidth = double.IsInfinity(availableSize.Width)
            ? max
            : Math.Clamp(Math.Floor(availableSize.Width / count), min, max);

        var height = 0d;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(slotWidth, availableSize.Height));
            height = Math.Max(height, child.DesiredSize.Height);
        }

        return new Size(slotWidth * count, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = 0d;
        foreach (UIElement child in InternalChildren)
        {
            child.Arrange(new Rect(x, 0, slotWidth, finalSize.Height));
            x += slotWidth;
        }

        InvalidateVisual();

        return finalSize;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (SeparatorBrush is not { } brush || InternalChildren.Count < 2)
            return;

        var pen = new Pen(brush, 1);
        var top = (ActualHeight - SeparatorBottomInset - SeparatorHeight) / 2;

        for (var i = 1; i < InternalChildren.Count; i++)
        {
            if (InternalChildren[i - 1] is ListBoxItem { IsSelected: true } || InternalChildren[i] is ListBoxItem { IsSelected: true })
                continue;

            var x = Math.Round(i * slotWidth);
            drawingContext.DrawLine(pen, new Point(x, top), new Point(x, top + SeparatorHeight));
        }
    }
}
