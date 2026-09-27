namespace ADB_Explorer.Helpers;

internal static class FocusHelper
{
    /// <summary>
    /// Takes the focus out of <paramref name="element"/> and gives it to its window, which keeps
    /// receiving keys (arrows, shortcuts) that WPF drops when nothing at all has focus. The logical
    /// focus moves too, or WPF restores it once the window is active again.
    /// </summary>
    internal static void ClearFocus(UIElement element)
    {
        if (!element.IsKeyboardFocusWithin)
            return;

        FocusManager.SetFocusedElement(FocusManager.GetFocusScope(element), null);

        if (Window.GetWindow(element) is { } window)
            window.Focus();
        else
            Keyboard.ClearFocus();
    }
}
