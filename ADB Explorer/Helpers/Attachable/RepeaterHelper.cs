namespace ADB_Explorer.Helpers;

public static class RepeaterHelper
{
    public static bool GetIsSelected(Control control) =>
        (bool)control.GetValue(IsSelectedProperty);

    public static void SetIsSelected(Control control, bool value) =>
        control.SetValue(IsSelectedProperty, value);

    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.RegisterAttached(
            "IsSelected",
            typeof(bool),
            typeof(RepeaterHelper),
            null);

    public static int GetSelectedItems(UIElement control) =>
        (int)control.GetValue(SelectedItemsProperty);

    public static void SetSelectedItems(UIElement control, int value) =>
        control.SetValue(SelectedItemsProperty, value);

    public static readonly DependencyProperty SelectedItemsProperty =
        DependencyProperty.RegisterAttached(
            "SelectedItems",
            typeof(int),
            typeof(RepeaterHelper),
            null);
}
