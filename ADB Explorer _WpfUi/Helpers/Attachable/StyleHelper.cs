using ADB_Explorer.Models;

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

    public static bool GetMirrorContentInRTL(Control control) =>
        (bool)control.GetValue(MirrorContentInRTLProperty);

    public static void SetMirrorContentInRTL(Control control, bool value) =>
        control.SetValue(MirrorContentInRTLProperty, value);

    public static readonly DependencyProperty MirrorContentInRTLProperty =
        DependencyProperty.RegisterAttached(
            "MirrorContentInRTL",
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

    public static bool GetIsSelected(UIElement control) =>
        (bool)control.GetValue(IsSelectedProperty);

    public static void SetIsSelected(UIElement control, bool value) =>
        control.SetValue(IsSelectedProperty, value);

    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.RegisterAttached(
            "IsSelected",
            typeof(bool),
            typeof(StyleHelper),
            null);

    public static bool GetEnableSelection(UIElement control) =>
        (bool)control.GetValue(EnableSelectionProperty);

    public static void SetEnableSelection(UIElement control, bool value) =>
        control.SetValue(EnableSelectionProperty, value);

    public static readonly DependencyProperty EnableSelectionProperty =
        DependencyProperty.RegisterAttached(
            "EnableSelection",
            typeof(bool),
            typeof(StyleHelper),
            null);

    public static bool GetShowAltBorderWhenDeselected(UIElement control) =>
        (bool)control.GetValue(ShowAltBorderWhenDeselectedProperty);

    public static void SetShowAltBorderWhenDeselected(UIElement control, bool value) =>
        control.SetValue(ShowAltBorderWhenDeselectedProperty, value);

    public static readonly DependencyProperty ShowAltBorderWhenDeselectedProperty =
        DependencyProperty.RegisterAttached(
            "ShowAltBorderWhenDeselected",
            typeof(bool),
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
