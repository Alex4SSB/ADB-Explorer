namespace ADB_Explorer.Helpers;

/// <summary>
/// The mouse buttons' physical state. WPF's own <see cref="Mouse.LeftButton"/> and its siblings miss
/// the presses and releases a drag-and-drop loop consumes, so they can't tell a cancelled drag is over.
/// </summary>
public static class MouseState
{
    private const int VK_LBUTTON = 0x01;
    private const int VK_RBUTTON = 0x02;
    private const int VK_MBUTTON = 0x04;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    public static bool IsAnyButtonDown => IsDown(VK_LBUTTON) || IsDown(VK_RBUTTON) || IsDown(VK_MBUTTON);

    private static bool IsDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
}
