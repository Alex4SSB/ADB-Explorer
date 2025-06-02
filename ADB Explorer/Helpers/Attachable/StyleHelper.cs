namespace ADB_Explorer.Helpers;

public static class StyleHelper
{
    public enum ContentAnimation
    {
        None,
        Bounce,
        RotateCW,
        RotateCCW,
        LeftMarquee,
        RightMarquee,
        UpMarquee,
        DownMarquee,
        Pulsate,
    }

    public static ContentAnimation GetContentAnimation(Control control) =>
        (ContentAnimation)control.GetValue(ContentAnimationProperty);

    public static void SetContentAnimation(Control control, ContentAnimation value) =>
        control.SetValue(ContentAnimationProperty, value);

    public static readonly DependencyProperty ContentAnimationProperty =
        DependencyProperty.RegisterAttached(
            "ContentAnimation",
            typeof(ContentAnimation),
            typeof(StyleHelper),
            null);

    public static bool GetActivateAnimation(Control control) =>
        (bool)control.GetValue(ActivateAnimationProperty);

    public static void SetActivateAnimation(Control control, bool value) =>
        control.SetValue(ActivateAnimationProperty, value);

    public static readonly DependencyProperty ActivateAnimationProperty =
        DependencyProperty.RegisterAttached(
            "ActivateAnimation",
            typeof(bool),
            typeof(StyleHelper),
            null);

    public static bool GetUseFluentStyles(Control control) =>
        (bool)control.GetValue(UseFluentStylesProperty);

    public static void SetUseFluentStyles(Control control, bool value) =>
        control.SetValue(UseFluentStylesProperty, value);

    public static readonly DependencyProperty UseFluentStylesProperty =
        DependencyProperty.RegisterAttached(
            "UseFluentStyles",
            typeof(bool),
            typeof(StyleHelper),
            null);

    public static bool GetAnimateOnClick(Control control) =>
    (bool)control.GetValue(AnimateOnClickProperty);

    public static void SetAnimateOnClick(Control control, bool value) =>
        control.SetValue(AnimateOnClickProperty, value);

    public static readonly DependencyProperty AnimateOnClickProperty =
        DependencyProperty.RegisterAttached(
            "AnimateOnClick",
            typeof(bool),
            typeof(StyleHelper),
            null);

    public static bool GetIsUnchecked(ToggleButton control) =>
        (bool)control.GetValue(IsUncheckedProperty);

    public static void SetIsUnchecked(ToggleButton control, bool value) =>
        control.SetValue(IsUncheckedProperty, value);

    public static readonly DependencyProperty IsUncheckedProperty =
        DependencyProperty.RegisterAttached(
            "IsUnchecked",
            typeof(bool),
            typeof(StyleHelper),
            null);

    public static bool GetBeginAnimation(Control control) =>
        (bool)control.GetValue(BeginAnimationProperty);

    public static void SetBeginAnimation(Control control, bool value) =>
        control.SetValue(BeginAnimationProperty, value);

    public static readonly DependencyProperty BeginAnimationProperty =
        DependencyProperty.RegisterAttached(
            "BeginAnimation",
            typeof(bool),
            typeof(StyleHelper),
            null);

    public static Brush GetPressedForeground(Control control) =>
        (Brush)control.GetValue(PressedForegroundProperty);

    public static void SetPressedForeground(Control control, Brush value) =>
        control.SetValue(PressedForegroundProperty, value);

    public static readonly DependencyProperty PressedForegroundProperty =
        DependencyProperty.RegisterAttached(
            "PressedForeground",
            typeof(Brush),
            typeof(StyleHelper),
            null);

    public static Brush GetAltBorderBrush(UIElement control) =>
        (Brush)control.GetValue(AltBorderBrushProperty);

    public static void SetAltBorderBrush(UIElement control, Brush value) =>
        control.SetValue(AltBorderBrushProperty, value);

    public static readonly DependencyProperty AltBorderBrushProperty =
        DependencyProperty.RegisterAttached(
            "AltBorderBrush",
            typeof(Brush),
            typeof(StyleHelper),
            null);

    public static string GetThreeStateGlyph(CheckBox control) =>
        (string)control.GetValue(ThreeStateGlyphProperty);

    public static void SetThreeStateGlyph(CheckBox control, string value) =>
        control.SetValue(ThreeStateGlyphProperty, value);

    public static readonly DependencyProperty ThreeStateGlyphProperty =
        DependencyProperty.RegisterAttached(
            "ThreeStateGlyph",
            typeof(string),
            typeof(StyleHelper),
            null);

    public static DependencyObject GetPopupPlacementTarget(DependencyObject control) =>
        (DependencyObject)control.GetValue(PopupPlacementTargetProperty);

    public static void SetPopupPlacementTarget(DependencyObject control, DependencyObject value) =>
        control.SetValue(PopupPlacementTargetProperty, value);

    public static readonly DependencyProperty PopupPlacementTargetProperty =
        DependencyProperty.RegisterAttached(
            "PopupPlacementTarget",
            typeof(DependencyObject),
            typeof(StyleHelper),
            null);

    public static PlacementMode GetPopupPlacement(DependencyObject control) =>
        (PlacementMode)control.GetValue(PopupPlacementProperty);

    public static void SetPopupPlacement(DependencyObject control, PlacementMode value) =>
        control.SetValue(PopupPlacementProperty, value);

    public static readonly DependencyProperty PopupPlacementProperty =
        DependencyProperty.RegisterAttached(
            "PopupPlacement",
            typeof(PlacementMode),
            typeof(StyleHelper),
            null);


    private static readonly Dictionary<string, DependencyProperty> ChildrenProperties = [];

    public static T FindDescendant<T>(DependencyObject control, bool includeSelf = false) where T : DependencyObject
    {
        if (control is null)
            return null;

        if (includeSelf && control is T tControl)
            return tControl;

        var key = $"Child{typeof(T).Name}";
        if (!ChildrenProperties.TryGetValue(key, out var requestedChild))
        {
            requestedChild = DependencyProperty.RegisterAttached(
                key,
                typeof(T),
                typeof(StyleHelper),
                null);

            ChildrenProperties.Add(key, requestedChild);
        }

        var saved = control.GetValue(requestedChild) as T;
        if (saved is not null)
            return saved;

        var descendant = _findDescendant<T>(control);
        control.SetValue(requestedChild, descendant);

        return descendant;
    }

    private static T _findDescendant<T>(DependencyObject control) where T : DependencyObject
    {
        if (control is null)
            return null;

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(control); i++)
        {
            var child = VisualTreeHelper.GetChild(control, i);
            if (child is T tChild)
                return tChild;

            var descendant = _findDescendant<T>(child);
            if (descendant is not null)
                return descendant;
        }

        return null;
    }

    public static void VerifyIcon(string icon, [CallerMemberName] string propertyName = null)
    {
        if (!IsFontIcon(icon))
            throw new ArgumentException("An icon must be one char in range E000-F8FF", propertyName);
    }

    public static bool IsFontIcon(string icon) =>
        icon is null || (icon.Length == 1 && char.GetUnicodeCategory(icon, 0) is UnicodeCategory.PrivateUse);
}
