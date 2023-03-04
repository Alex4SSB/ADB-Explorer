namespace ADB_Explorer.Converters;

public class MarginConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        // arg 0 - ExpansionProgress
        // arg 1 - ExpandDirection
        // arg 2 - ContentHeight
        // arg 3 - ContentWidth

        if (values.Length < 4)
            throw new ArgumentException("This function requires 4 arguments.");
        else if (values[0] is not double || values[1] is not ExpandDirection || values[2] is not double || values[3] is not double)
            throw new ArgumentException("Provided arguments are not of correct format.");

        var expansionProgress = (double)values[0];
        var expandDirection = (ExpandDirection)values[1];
        var contentHeight = (double)values[2];
        var contentWidth = (double)values[3];

        var size = expandDirection switch
        {
            ExpandDirection.Down or ExpandDirection.Up => contentHeight,
            ExpandDirection.Left or ExpandDirection.Right => contentWidth,
            _ => throw new NotSupportedException(),
        };

        var result = -size * (1 - expansionProgress);

        return expandDirection switch
        {
            ExpandDirection.Down => new Thickness(0, result, 0, 0),
            ExpandDirection.Up => new Thickness(0, 0, 0, result),
            ExpandDirection.Left => new Thickness(0, 0, result, 0),
            ExpandDirection.Right => new Thickness(result, 0, 0, 0),
            _ => throw new NotSupportedException(),
        };
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
