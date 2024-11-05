namespace ADB_Explorer.Helpers;

public static class DialogHelper
{
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
