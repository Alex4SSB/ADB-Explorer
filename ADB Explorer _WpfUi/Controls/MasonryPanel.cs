namespace ADB_Explorer.Controls;

public class MasonryPanel : Panel
{
    public int Columns
    {
        get => (int)GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    public static readonly DependencyProperty ColumnsProperty =
        DependencyProperty.Register(
            nameof(Columns),
            typeof(int),
            typeof(MasonryPanel),
            new FrameworkPropertyMetadata(2, FrameworkPropertyMetadataOptions.AffectsMeasure));

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count == 0 || Columns <= 0)
            return new Size(0, 0);

        double availableWidth = availableSize.Width;

        double columnWidth = availableWidth / Columns;
        var columnHeights = new double[Columns];

        foreach (UIElement child in Children)
        {
            // Children get finite width, unbounded height
            child.Measure(new Size(columnWidth, double.PositiveInfinity));

            int column = GetShortestColumn(columnHeights);
            columnHeights[column] += child.DesiredSize.Height;
            availableWidth = child.DesiredSize.Width;
        }

        double desiredHeight = columnHeights.Max();

        return new Size(availableWidth * Columns, desiredHeight);
    }

    private static int GetShortestColumn(double[] heights)
    {
        int index = 0;
        double min = heights[0];

        for (int i = 1; i < heights.Length; i++)
        {
            if (heights[i] < min)
            {
                min = heights[i];
                index = i;
            }
        }

        return index;
    }


    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count == 0 || Columns <= 0)
            return finalSize;

        double columnWidth = finalSize.Width / Columns;
        var columnHeights = new double[Columns];

        foreach (UIElement child in Children)
        {
            int column = GetShortestColumn(columnHeights);

            double height = child.DesiredSize.Height;

            child.Arrange(new Rect(
                x: column * columnWidth,
                y: columnHeights[column],
                width: columnWidth,
                height: height));

            columnHeights[column] += height;
        }

        return finalSize;
    }
}
