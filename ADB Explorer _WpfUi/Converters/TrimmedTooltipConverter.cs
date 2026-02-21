namespace ADB_Explorer.Converters;

public class TrimmedTooltipConverter : IValueConverter
{

    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is null)
            return Visibility.Collapsed;

        var text = value switch
        {
            TextBlock textBlock => textBlock.Text,
            TextBox textBox => textBox.Text,
            _ => "",
        };

        return text.Length > 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        //// FrameworkElement to include both TextBlock and TextBox
        //var textBlock = value as FrameworkElement;

        //textBlock.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
        //var margin = textBlock.Margin.Left + textBlock.Margin.Right;

        //return textBlock.ActualWidth + margin < textBlock.DesiredSize.Width
        //    ? Visibility.Visible
        //    : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
