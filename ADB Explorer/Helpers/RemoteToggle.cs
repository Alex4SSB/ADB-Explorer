using System.Windows;
using System.Windows.Controls;

namespace ADB_Explorer.Helpers
{
    public static class RemoteToggle
    {
        public static bool GetIsTargetVisible(Control control) =>
            (bool)control.GetValue(IsTargetVisibleProperty);

        public static void SetIsTargetVisible(Control control, bool value) =>
            control.SetValue(IsTargetVisibleProperty, value);

        public static readonly DependencyProperty IsTargetVisibleProperty =
            DependencyProperty.RegisterAttached(
                "IsTargetVisible",
                typeof(bool),
                typeof(RemoteToggle),
                null);
    }
}
