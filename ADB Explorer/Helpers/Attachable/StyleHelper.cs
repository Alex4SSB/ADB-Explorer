using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace ADB_Explorer.Helpers
{
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

        public static bool GetIsMouseSelectionVisible(MenuItem control) =>
            (bool)control.GetValue(IsMouseSelectionVisibleProperty);

        public static void SetIsMouseSelectionVisible(MenuItem control, bool value) =>
            control.SetValue(IsMouseSelectionVisibleProperty, value);

        public static readonly DependencyProperty IsMouseSelectionVisibleProperty =
            DependencyProperty.RegisterAttached(
                "IsMouseSelectionVisible",
                typeof(bool),
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
    }
}
