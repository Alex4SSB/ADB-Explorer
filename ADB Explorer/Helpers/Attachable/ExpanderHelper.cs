namespace ADB_Explorer.Helpers;

public static class ExpanderHelper
{
    public enum Rotation
    {
        None,
        CW,
        CCW,
        CW90,
        CCW90,
    }

    public static Rotation GetChevronRotation(Control control) =>
        (Rotation)control.GetValue(ChevronRotationProperty);

    public static void SetChevronRotation(Control control, Rotation value) =>
        control.SetValue(ChevronRotationProperty, value);

    public static readonly DependencyProperty ChevronRotationProperty =
        DependencyProperty.RegisterAttached(
            "ChevronRotation",
            typeof(Rotation),
            typeof(ExpanderHelper),
            null);

    public static ExpandDirection GetExpandDirection(Control control) =>
        (ExpandDirection)control.GetValue(ExpandDirectionProperty);

    public static void SetExpandDirection(Control control, ExpandDirection value) =>
        control.SetValue(ExpandDirectionProperty, value);

    public static readonly DependencyProperty ExpandDirectionProperty =
        DependencyProperty.RegisterAttached(
            "ExpandDirection",
            typeof(ExpandDirection),
            typeof(ExpanderHelper),
            new(ExpandDirection.Down));

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
