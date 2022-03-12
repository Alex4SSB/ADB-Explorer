using System.Windows;
using System.Windows.Controls;

namespace ADB_Explorer.Helpers
{
    public static class TextHelper
    {
        public static string GetAltText(Control control) =>
            (string)control.GetValue(AltTextProperty);

        public static void SetAltText(Control control, string value) =>
            control.SetValue(AltTextProperty, value);

        public static readonly DependencyProperty AltTextProperty =
            DependencyProperty.RegisterAttached(
                "AltText",
                typeof(string),
                typeof(TextHelper),
                null);
    }
}
