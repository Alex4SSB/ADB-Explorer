namespace ADB_Explorer.Helpers;

/// <summary>
/// Drops the second press of a right-button double click, and its release, before any element sees them.
/// Otherwise it opens a context menu at the screen origin, or raises MouseDoubleClick (e.g. opening a drive).
/// </summary>
internal static class RightDoubleClickGuard
{
    private static bool _swallowUp;

    public static void Register()
        => InputManager.Current.PreProcessInput += InputManager_PreProcessInput;

    private static void InputManager_PreProcessInput(object sender, PreProcessInputEventArgs e)
    {
        if (e.StagingItem.Input is not MouseButtonEventArgs { ChangedButton: MouseButton.Right } args)
            return;

        // WPF counts clicks with the system double-click time and distance; odd ones (third, fifth) open menus as usual
        if (args.RoutedEvent == Mouse.PreviewMouseDownEvent && args.ClickCount % 2 == 0)
        {
            _swallowUp = true;
            e.Cancel();
        }
        else if (args.RoutedEvent == Mouse.PreviewMouseUpEvent && _swallowUp)
        {
            _swallowUp = false;
            e.Cancel();
        }
    }
}
