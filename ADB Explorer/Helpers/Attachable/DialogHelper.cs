namespace ADB_Explorer.Helpers;

public static class DialogHelper
{
    public static bool GetIsClipboardIconVisible(ContentDialog control) =>
        (bool)control.GetValue(IsClipboardIconVisibleProperty);

    public static void SetIsClipboardIconVisible(ContentDialog control, bool value) =>
        control.SetValue(IsClipboardIconVisibleProperty, value);

    public static readonly DependencyProperty IsClipboardIconVisibleProperty =
        DependencyProperty.RegisterAttached(
            "IsClipboardIconVisible",
            typeof(bool),
            typeof(DialogHelper),
            null);

    public static string GetDialogIcon(ContentDialog control) =>
        (string)control.GetValue(DialogIconProperty);

    public static void SetDialogIcon(ContentDialog control, string value) =>
        control.SetValue(DialogIconProperty, value);

    public static readonly DependencyProperty DialogIconProperty =
        DependencyProperty.RegisterAttached(
            "DialogIcon",
            typeof(string),
            typeof(DialogHelper),
            null);
}
