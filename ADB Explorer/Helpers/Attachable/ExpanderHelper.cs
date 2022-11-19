namespace ADB_Explorer.Helpers;

public static class ExpanderHelper
{
    public enum ExpandArrow
    {
        None,
        CW,
        CCW
    }

    public static ExpandArrow GetExpanderArrow(Control control) =>
        (ExpandArrow)control.GetValue(ExpanderArrowProperty);

    public static void SetExpanderArrow(Control control, ExpandArrow value) =>
        control.SetValue(ExpanderArrowProperty, value);

    public static readonly DependencyProperty ExpanderArrowProperty =
        DependencyProperty.RegisterAttached(
            "ExpanderArrow",
            typeof(ExpandArrow),
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

    public static bool GetIsAnimationActive(UIElement control) =>
        (bool)control.GetValue(IsAnimationActiveProperty);

    public static void SetIsAnimationActive(UIElement control, bool value) =>
        control.SetValue(IsAnimationActiveProperty, value);

    public static readonly DependencyProperty IsAnimationActiveProperty =
        DependencyProperty.RegisterAttached(
            "IsAnimationActive",
            typeof(bool),
            typeof(ExpanderHelper),
            null);

    public static double GetExpansionProgress(UIElement control) =>
        (double)control.GetValue(ExpansionProgressProperty);

    public static void SetExpansionProgress(UIElement control, double value) =>
        control.SetValue(ExpansionProgressProperty, value);

    public static readonly DependencyProperty ExpansionProgressProperty =
        DependencyProperty.RegisterAttached(
            "ExpansionProgress",
            typeof(double),
            typeof(ExpanderHelper),
            null);

    public static bool GetIsExpandEnabled(UIElement control) =>
        (bool)control.GetValue(IsExpandEnabledProperty);

    public static void SetIsExpandEnabled(UIElement control, bool value) =>
        control.SetValue(IsExpandEnabledProperty, value);

    public static readonly DependencyProperty IsExpandEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsExpandEnabled",
            typeof(bool),
            typeof(ExpanderHelper),
            null);

    public static object GetHeaderBottomContent(UIElement control) =>
        control.GetValue(HeaderBottomContentProperty);

    public static void SetHeaderBottomContent(UIElement control, object value) =>
        control.SetValue(HeaderBottomContentProperty, value);

    public static readonly DependencyProperty HeaderBottomContentProperty =
        DependencyProperty.RegisterAttached(
            "HeaderBottomContent",
            typeof(object),
            typeof(ExpanderHelper),
            null);
}
