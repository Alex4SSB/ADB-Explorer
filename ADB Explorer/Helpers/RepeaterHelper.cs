using System.Windows;
using System.Windows.Controls;

namespace ADB_Explorer.Helpers
{
    public static class RepeaterHelper
    {
        public static bool GetIsSelected(Control control) =>
            (bool)control.GetValue(IsSelectedProperty);

        public static void SetIsSelected(Control control, bool value) =>
            control.SetValue(IsSelectedProperty, value);

        public static readonly DependencyProperty IsSelectedProperty =
            DependencyProperty.RegisterAttached(
                "IsSelected",
                typeof(bool),
                typeof(RepeaterHelper),
                null);
    }
}
