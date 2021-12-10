using System.Windows;

namespace ADB_Explorer.Helpers
{
    public static class VisibilityHelper
    {
        public static Visibility Visible(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

        public static void Visible(this FrameworkElement control, bool value) => control.Visibility = Visible(value);

        public static bool Visible(this FrameworkElement control) => control.Visibility == Visibility.Visible;

        public static void ToggleVisibility(this FrameworkElement control) => control.Visible(!control.Visible());
    }
}
