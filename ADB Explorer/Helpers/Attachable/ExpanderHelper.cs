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

    public static AlignmentX GetChevronPlacement(Control control) =>
        (AlignmentX)control.GetValue(ChevronPlacementProperty);

    public static void SetChevronPlacement(Control control, AlignmentX value) =>
        control.SetValue(ChevronPlacementProperty, value);

    public static readonly DependencyProperty ChevronPlacementProperty =
        DependencyProperty.RegisterAttached(
            "ChevronPlacement",
            typeof(AlignmentX),
            typeof(ExpanderHelper),
            new(AlignmentX.Right));

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

    public static bool GetIsTransparent(Control control) =>
        (bool)control.GetValue(IsTransparentProperty);

    public static void SetIsTransparent(Control control, bool value) =>
        control.SetValue(IsTransparentProperty, value);

    public static readonly DependencyProperty IsTransparentProperty =
        DependencyProperty.RegisterAttached(
            "IsTransparent",
            typeof(bool),
            typeof(ExpanderHelper),
            null);

    public static bool GetIsAccentHeader(Control control) =>
        (bool)control.GetValue(IsAccentHeaderProperty);

    public static void SetIsAccentHeader(Control control, bool value) =>
        control.SetValue(IsAccentHeaderProperty, value);

    public static readonly DependencyProperty IsAccentHeaderProperty =
        DependencyProperty.RegisterAttached(
            "IsAccentHeader",
            typeof(bool),
            typeof(ExpanderHelper),
            null);

    public static object GetHeaderTooltip(Control control) =>
        control.GetValue(HeaderTooltipProperty);

    public static void SetHeaderTooltip(Control control, object value) =>
        control.SetValue(HeaderTooltipProperty, value);

    public static readonly DependencyProperty HeaderTooltipProperty =
        DependencyProperty.RegisterAttached(
            "HeaderTooltip",
            typeof(object),
            typeof(ExpanderHelper),
            null);
}
