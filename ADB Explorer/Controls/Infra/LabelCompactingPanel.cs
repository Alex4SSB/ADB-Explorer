namespace ADB_Explorer.Controls;

/// <summary>
/// Lays its children out in a row. When they don't fit, it hides the labels of <see cref="LabelHosts"/>
/// (see <see cref="StyleHelper.IsLabelHiddenProperty"/>) one host at a time in list order, then whole menu items from the end.
/// </summary>
public class LabelCompactingPanel : Panel
{
    private IReadOnlyList<UIElement> _labelHosts = [];
    private double[] _savedWidths = [];
    private int _hiddenCount;

    public IReadOnlyList<UIElement> LabelHosts
    {
        get => _labelHosts;
        set
        {
            _labelHosts = value;
            _savedWidths = new double[value.Count];
            SetHiddenCount(0);
            InvalidateMeasure();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var size = MeasureRow(availableSize.Height);

        while (size.Width > availableSize.Width && _hiddenCount < _labelHosts.Count)
        {
            SetHiddenCount(_hiddenCount + 1);
            var smaller = MeasureRow(availableSize.Height);
            _savedWidths[_hiddenCount - 1] = size.Width - smaller.Width;
            size = smaller;
        }

        while (_hiddenCount > 0 && size.Width + _savedWidths[_hiddenCount - 1] <= availableSize.Width)
        {
            SetHiddenCount(_hiddenCount - 1);
            var larger = MeasureRow(availableSize.Height);

            // The label is wider than when it was last hidden (or its host was collapsed then)
            if (larger.Width > availableSize.Width)
            {
                _savedWidths[_hiddenCount] = larger.Width - size.Width;
                SetHiddenCount(_hiddenCount + 1);
                size = MeasureRow(availableSize.Height);
                break;
            }

            size = larger;
        }

        return new(Math.Min(size.Width, availableSize.Width), size.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Arrange(new Rect(x, 0, child.DesiredSize.Width, finalSize.Height));
            x += child.DesiredSize.Width;
        }

        // Still too wide with every label hidden - cut the row after the last item that fits whole
        Clip = x > finalSize.Width
            ? new RectangleGeometry(new Rect(0, 0, WholeItemsEnd(finalSize.Width), finalSize.Height))
            : null;

        return finalSize;
    }

    private double WholeItemsEnd(double width)
    {
        double end = 0;
        foreach (var item in ToolbarItems(this))
        {
            var right = item.TransformToAncestor(this).TransformBounds(new Rect(item.RenderSize)).Right;
            if (right <= width)
                end = Math.Max(end, right);
        }

        return end;
    }

    /// <summary>The top-level items of the menus in the row, the units it hides whole.</summary>
    private static IEnumerable<MenuItem> ToolbarItems(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);

            if (child is MenuItem item && ItemsControl.ItemsControlFromItemContainer(item) is Menu)
            {
                if (item.IsVisible)
                    yield return item;

                continue;
            }

            foreach (var nested in ToolbarItems(child))
                yield return nested;
        }
    }

    private Size MeasureRow(double height)
    {
        double width = 0, maxHeight = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(double.PositiveInfinity, height));
            width += child.DesiredSize.Width;
            maxHeight = Math.Max(maxHeight, child.DesiredSize.Height);
        }

        return new(width, maxHeight);
    }

    private void SetHiddenCount(int count)
    {
        _hiddenCount = count;

        for (int i = 0; i < _labelHosts.Count; i++)
        {
            var host = _labelHosts[i];
            var hide = i < count;
            if (StyleHelper.GetIsLabelHidden(host) == hide)
                continue;

            StyleHelper.SetIsLabelHidden(host, hide);

            // Measuring a child whose own measure is valid returns its cached size, even though a label deep
            // inside it just changed - so everything between this panel and the labels is measured afresh.
            InvalidateMeasureDown(host);

            for (var parent = VisualTreeHelper.GetParent(host); parent is not null && parent != this; parent = VisualTreeHelper.GetParent(parent))
                (parent as UIElement)?.InvalidateMeasure();
        }
    }

    private static void InvalidateMeasureDown(DependencyObject element)
    {
        (element as UIElement)?.InvalidateMeasure();

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            InvalidateMeasureDown(VisualTreeHelper.GetChild(element, i));
    }
}
