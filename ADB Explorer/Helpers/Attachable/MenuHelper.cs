using Windows.UI.Popups;

namespace ADB_Explorer.Helpers;

public static class MenuHelper
{
    public static bool GetIsMouseSelectionVisible(UIElement control) =>
        (bool)control.GetValue(IsMouseSelectionVisibleProperty);

    public static void SetIsMouseSelectionVisible(UIElement control, bool value) =>
        control.SetValue(IsMouseSelectionVisibleProperty, value);

    public static readonly DependencyProperty IsMouseSelectionVisibleProperty =
        DependencyProperty.RegisterAttached(
            "IsMouseSelectionVisible",
            typeof(bool),
            typeof(MenuHelper),
            null);

    public static Brush GetCheckBackground(UIElement control) =>
        (Brush)control.GetValue(CheckBackgroundProperty);

    public static void SetCheckBackground(UIElement control, Brush value) =>
        control.SetValue(CheckBackgroundProperty, value);

    public static readonly DependencyProperty CheckBackgroundProperty =
        DependencyProperty.RegisterAttached(
            "CheckBackground",
            typeof(Brush),
            typeof(MenuHelper),
            null);

    public static Thickness GetItemPadding(UIElement control) =>
        (Thickness)control.GetValue(ItemPaddingProperty);

    public static void SetItemPadding(UIElement control, Thickness value) =>
        control.SetValue(ItemPaddingProperty, value);

    public static readonly DependencyProperty ItemPaddingProperty =
        DependencyProperty.RegisterAttached(
            "ItemPadding",
            typeof(Thickness),
            typeof(MenuHelper),
            null);

    public static Thickness GetItemMargin(UIElement control) =>
        (Thickness)control.GetValue(ItemMarginProperty);

    public static void SetItemMargin(UIElement control, Thickness value) =>
        control.SetValue(ItemMarginProperty, value);

    public static readonly DependencyProperty ItemMarginProperty =
        DependencyProperty.RegisterAttached(
            "ItemMargin",
            typeof(Thickness),
            typeof(MenuHelper),
            null);

    public static bool? GetIsButtonMenu(UIElement control) =>
        (bool?)control.GetValue(IsButtonMenuProperty);

    public static void SetIsButtonMenu(UIElement control, bool? value) =>
        control.SetValue(IsButtonMenuProperty, value);

    public static readonly DependencyProperty IsButtonMenuProperty =
        DependencyProperty.RegisterAttached(
            "IsButtonMenu",
            typeof(bool?),
            typeof(MenuHelper),
            null);

    public static PlacementMode GetDropDownPlacement(UIElement control) =>
        (PlacementMode)control.GetValue(DropDownPlacementProperty);

    public static void SetDropDownPlacement(UIElement control, PlacementMode value) =>
        control.SetValue(DropDownPlacementProperty, value);

    public static readonly DependencyProperty DropDownPlacementProperty =
        DependencyProperty.RegisterAttached(
            "DropDownPlacement",
            typeof(PlacementMode),
            typeof(MenuHelper),
            null);
}
