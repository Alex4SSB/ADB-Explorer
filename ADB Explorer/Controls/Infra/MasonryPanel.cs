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

    public double ColumnSpacing
    {
        get => (double)GetValue(ColumnSpacingProperty);
        set => SetValue(ColumnSpacingProperty, value);
    }

    public static readonly DependencyProperty ColumnSpacingProperty =
        DependencyProperty.Register(
            nameof(ColumnSpacing),
            typeof(double),
            typeof(MasonryPanel),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count == 0 || Columns <= 0)
            return new Size(0, 0);

        double spacing = ColumnSpacing;
        int gutterCount = Math.Max(0, Columns - 1);
        double columnWidth = GetColumnWidth(availableSize.Width, spacing, gutterCount);
        var columnHeights = new double[Columns];

        foreach (UIElement child in Children)
        {
            child.Measure(new Size(columnWidth, double.PositiveInfinity));

            int column = GetShortestColumn(columnHeights);
            columnHeights[column] += child.DesiredSize.Height;
            columnWidth = child.DesiredSize.Width;
        }

        double desiredHeight = columnHeights.Max();
        double desiredWidth = columnWidth * Columns + gutterCount * spacing;

        return new Size(desiredWidth, desiredHeight);
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

        double spacing = ColumnSpacing;
        int gutterCount = Math.Max(0, Columns - 1);
        double columnWidth = GetColumnWidth(finalSize.Width, spacing, gutterCount);
        var columnHeights = new double[Columns];

        foreach (UIElement child in Children)
        {
            int column = GetShortestColumn(columnHeights);
            double height = child.DesiredSize.Height;

            child.Arrange(new Rect(
                x: column * (columnWidth + spacing),
                y: columnHeights[column],
                width: columnWidth,
                height: height));

            columnHeights[column] += height;
        }

        return finalSize;
    }

    private double GetColumnWidth(double availableWidth, double spacing, int gutterCount)
    {
        if (double.IsInfinity(availableWidth))
            return double.PositiveInfinity;

        return (availableWidth - gutterCount * spacing) / Columns;
    }
}
