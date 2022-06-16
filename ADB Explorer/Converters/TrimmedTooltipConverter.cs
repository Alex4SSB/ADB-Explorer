using System;
using System.Windows;
using System.Windows.Data;

namespace ADB_Explorer.Converters
{
    public class TrimmedTooltipConverter : IValueConverter
    {

        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is null)
                return Visibility.Collapsed;

            // FrameworkElement to include both TextBlock and TextBox
            var textBlock = value as FrameworkElement;

            textBlock.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
            var margin = textBlock.Margin.Left + textBlock.Margin.Right;

            return textBlock.ActualWidth + margin < textBlock.DesiredSize.Width
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
