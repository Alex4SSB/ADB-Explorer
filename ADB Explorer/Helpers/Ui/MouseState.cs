namespace ADB_Explorer.Helpers;

using static Vanara.PInvoke.User32;

/// <summary>
/// The mouse buttons' physical state. WPF's own <see cref="Mouse.LeftButton"/> and its siblings miss
/// the presses and releases a drag-and-drop loop consumes, so they can't tell a cancelled drag is over.
/// </summary>
public static class MouseState
{
    public static bool IsAnyButtonDown => IsDown(VK.VK_LBUTTON) || IsDown(VK.VK_RBUTTON) || IsDown(VK.VK_MBUTTON);

    private static bool IsDown(VK virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
}
