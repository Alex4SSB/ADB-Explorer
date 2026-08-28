using ADB_Explorer.Helpers;

namespace ADB_Explorer.Converters;

/// <summary>
/// Shows a tooltip when the placement target is not fully inside its ancestor <see cref="ScrollViewer"/>.
/// Tree item headers are not width-constrained, so <see cref="TrimmedTooltipConverter"/> never sees ellipsis.
/// </summary>
public class OverflowsTreeTooltipConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not FrameworkElement element)
            return Visibility.Collapsed;

        var viewer = HitTestHelper.FindAncestor<ScrollViewer>(element);
        if (viewer is null)
            return Visibility.Collapsed;

        if (element.ActualWidth <= 0 || element.ActualHeight <= 0
            || viewer.ActualWidth <= 0 || viewer.ActualHeight <= 0)
            return Visibility.Collapsed;

        Rect elementRect;
        try
        {
            elementRect = element.TransformToAncestor(viewer)
                .TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        }
        catch (InvalidOperationException)
        {
            return Visibility.Collapsed;
        }
        
        var viewerRect = new Rect(0, 0, viewer.ActualWidth, viewer.ActualHeight);
        if (Rect.Intersect(viewerRect, elementRect) == elementRect)
            return Visibility.Collapsed;

        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
