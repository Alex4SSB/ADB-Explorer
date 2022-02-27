using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace ADB_Explorer.Converters
{
    public class MarginConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var isUp = values.Length == 3 && values[2] is ExpandDirection and ExpandDirection.Up;

            if (values[0] is double size && values[1] is double factor)
            {
                var result = -size * (1 - factor);
                var marginAbove = isUp ? 0 : result;
                var marginBelow = isUp ? result : 0;

                return new Thickness(0, marginAbove, 0, marginBelow);
            }

            return new Thickness(0, 0, 0, 0);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
