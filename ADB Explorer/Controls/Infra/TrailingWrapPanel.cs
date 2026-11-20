namespace ADB_Explorer.Controls;

/// <summary>
/// Lays its children out in a row, with the last one at the end. When the row can't hold them all
/// with <see cref="Spacing"/> before the last one, the last one moves to a row of its own below.
/// </summary>
public class TrailingWrapPanel : Panel
{
    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public static readonly DependencyProperty SpacingProperty =
        DependencyProperty.Register(
            nameof(Spacing),
            typeof(double),
            typeof(TrailingWrapPanel),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    private bool _isWrapped;

    protected override Size MeasureOverride(Size availableSize)
    {
        if (InternalChildren.Count == 0)
            return new(0, 0);

        var leading = MeasureLeading(availableSize);
        var trailing = InternalChildren[^1];
        trailing.Measure(availableSize);

        var trailingSize = trailing.DesiredSize;
        var gap = leading.Width > 0 && trailingSize.Width > 0 ? Spacing : 0;
        _isWrapped = leading.Width + gap + trailingSize.Width > availableSize.Width;

        if (_isWrapped)
            return new(Math.Max(leading.Width, trailingSize.Width), leading.Height + Spacing + trailingSize.Height);

        return new(leading.Width + gap + trailingSize.Width, Math.Max(leading.Height, trailingSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (InternalChildren.Count == 0)
            return finalSize;

        var trailing = InternalChildren[^1];
        double leadingHeight = 0;

        for (int i = 0; i < InternalChildren.Count - 1; i++)
            leadingHeight = Math.Max(leadingHeight, InternalChildren[i].DesiredSize.Height);

        double rowHeight = _isWrapped ? leadingHeight : finalSize.Height;
        double x = 0;

        for (int i = 0; i < InternalChildren.Count - 1; i++)
        {
            var child = InternalChildren[i];
            child.Arrange(new Rect(x, 0, child.DesiredSize.Width, rowHeight));
            x += child.DesiredSize.Width;
        }

        var trailingWidth = trailing.DesiredSize.Width;
        if (_isWrapped)
        {
            var top = leadingHeight + Spacing;
            trailing.Arrange(new Rect(finalSize.Width - trailingWidth, top, trailingWidth, Math.Max(0, finalSize.Height - top)));
        }
        else
            trailing.Arrange(new Rect(finalSize.Width - trailingWidth, 0, trailingWidth, finalSize.Height));

        return finalSize;
    }

    private Size MeasureLeading(Size availableSize)
    {
        double width = 0, height = 0;
        for (int i = 0; i < InternalChildren.Count - 1; i++)
        {
            var child = InternalChildren[i];
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            width += child.DesiredSize.Width;
            height = Math.Max(height, child.DesiredSize.Height);
        }

        return new(width, height);
    }
}
