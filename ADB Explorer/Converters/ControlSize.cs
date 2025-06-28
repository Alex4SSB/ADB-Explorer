namespace ADB_Explorer.Converters;

public static class ControlSize
{
    /// <summary>
    /// Returns the length of a path button.
    /// </summary>
    /// <param name="content"></param>
    /// <returns></returns>
    public static double GetWidth(UIElement item)
    {
        item.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return item.DesiredSize.Width;
    }

    public static double GetWidth(DataTemplate template, object dataContext)
    {
        var content = (FrameworkElement)template.LoadContent();
        content.DataContext = dataContext;
        return GetWidth(content);
    }
}
