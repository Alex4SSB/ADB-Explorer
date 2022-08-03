using System.Windows;
using System.Windows.Controls.Primitives;

namespace ADB_Explorer.Helpers
{
    public static class DataGridHelper
    {
        public static bool GetIsFilteringEnabled(DataGridColumnHeader control) =>
            (bool)control.GetValue(IsFilteringEnabledProperty);

        public static void SetIsFilteringEnabled(DataGridColumnHeader control, bool value) =>
            control.SetValue(IsFilteringEnabledProperty, value);

        public static readonly DependencyProperty IsFilteringEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsFilteringEnabled",
                typeof(bool),
                typeof(DataGridHelper),
                null);
    }
}
