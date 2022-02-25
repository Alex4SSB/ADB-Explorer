using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ADB_Explorer.Converters
{
    public class MarginConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length == 3 && values[2] is bool and false)
                return new Thickness(0, 0, 0, 0);

            double result = 1.0;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] is double val)
                    result *= val;
            }

            return new Thickness(0, result - ((double)values[0]), 0, 0);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
