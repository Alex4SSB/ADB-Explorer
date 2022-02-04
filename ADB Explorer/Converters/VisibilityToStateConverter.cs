using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Shell;

namespace ADB_Explorer.Converters
{
    internal class VisibilityToStateConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            TaskbarItemProgressState enabled = TaskbarItemProgressState.None;
            if (!string.IsNullOrEmpty(value.ToString()))
            {
                if (value.ToString() == "Visible")
                {
                    enabled = TaskbarItemProgressState.Normal;
                }
            }
            return enabled;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
