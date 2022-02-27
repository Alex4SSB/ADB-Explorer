using System.Windows;
using System.Windows.Controls;

namespace ADB_Explorer.Helpers
{
    public static class ExpanderHelper
    {
        public static bool GetIsArrowVisible(Control control) =>
            (bool)control.GetValue(IsArrowVisibleProperty);

        public static void SetIsArrowVisible(Control control, bool value) =>
            control.SetValue(IsArrowVisibleProperty, value);

        public static readonly DependencyProperty IsArrowVisibleProperty =
            DependencyProperty.RegisterAttached(
                "IsArrowVisible",
                typeof(bool),
                typeof(ExpanderHelper),
                null);

        public static bool GetIsListItem(Control control) =>
            (bool)control.GetValue(IsListItemProperty);

        public static void SetIsListItem(Control control, bool value) =>
            control.SetValue(IsListItemProperty, value);

        public static readonly DependencyProperty IsListItemProperty =
            DependencyProperty.RegisterAttached(
                "IsListItem",
                typeof(bool),
                typeof(ExpanderHelper),
                null);

        public static bool GetIsHeaderVisible(Control control) =>
            (bool)control.GetValue(IsHeaderVisibleProperty);

        public static void SetIsHeaderVisible(Control control, bool value) =>
            control.SetValue(IsHeaderVisibleProperty, value);

        public static readonly DependencyProperty IsHeaderVisibleProperty =
            DependencyProperty.RegisterAttached(
                "IsHeaderVisible",
                typeof(bool),
                typeof(ExpanderHelper),
                null);

        public static bool GetIsContentCollapsed(Control control) =>
            (bool)control.GetValue(IsContentCollapsedProperty);

        public static void SetIsContentCollapsed(Control control, bool value) =>
            control.SetValue(IsContentCollapsedProperty, value);

        public static readonly DependencyProperty IsContentCollapsedProperty =
            DependencyProperty.RegisterAttached(
                "IsContentCollapsed",
                typeof(bool),
                typeof(ExpanderHelper),
                null);
    }
}
