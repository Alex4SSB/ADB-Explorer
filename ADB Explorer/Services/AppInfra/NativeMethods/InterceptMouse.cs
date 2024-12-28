namespace ADB_Explorer.Services;

#pragma warning disable SYSLIB1054 // Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time

public static partial class NativeMethods
{
    // https://learn.microsoft.com/en-us/archive/blogs/toub/low-level-mouse-hook-in-c

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public HANDLE dwExtraInfo;
    }

    private static LowLevelMouseProc _mouseProc = HookCallback;
    private static HANDLE _mouseHookID = IntPtr.Zero;
    private static Action<POINT> _externalMouseAction;
    private static MouseMessages _requestedMouseEvent;
    private static POINT _mousePosition;

    private delegate HANDLE LowLevelMouseProc(int nCode, MouseMessages wParam, HANDLE lParam);

    public static void InitInterceptMouse(MouseMessages mouseEvent, Action<POINT> action)
    {
        _requestedMouseEvent = mouseEvent;
        _externalMouseAction = action;

        _mouseHookID = SetHook(_mouseProc);
    }

    public static void CloseInterceptMouse()
    {
        UnhookWindowsHookEx(_mouseHookID);
    }

    private static HANDLE SetHook(LowLevelMouseProc proc)
    {
        using Process curProcess = Process.GetCurrentProcess();
        using ProcessModule curModule = curProcess.MainModule;

        return SetWindowsHookEx(WinHooks.WH_MOUSE_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
    }

    private static HANDLE HookCallback(int nCode, MouseMessages wParam, HANDLE lParam)
    {
        if (nCode < 0 || wParam != _requestedMouseEvent)
            return CallNextHookEx(_mouseHookID, nCode, wParam, lParam);

        var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);

        POINT newPoint = new(hookStruct.pt.X, hookStruct.pt.Y);
        if (newPoint != _mousePosition)
            _externalMouseAction?.Invoke(_mousePosition);

        _mousePosition = newPoint;

        return CallNextHookEx(_mouseHookID, nCode, wParam, lParam);
    }

    public static int? GetPidFromPoint()
    {
        var hWnd = WindowFromPoint(_mousePosition);

        if (hWnd == IntPtr.Zero)
            return null;

        if (GetWindowThreadProcessId(hWnd, out var pid) == 0)
            return null;

        return (int)pid;
    }

    [DllImport("User32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern HANDLE SetWindowsHookEx(WinHooks idHook,
        LowLevelMouseProc lpfn, HANDLE hMod, uint dwThreadId);

    [DllImport("User32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(HANDLE hhk);

    [DllImport("User32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern HANDLE CallNextHookEx(HANDLE hhk, int nCode,
        MouseMessages wParam, HANDLE lParam);

    [DllImport("Kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern HANDLE GetModuleHandle(string lpModuleName);

    [DllImport("User32.dll")]
    private static extern HANDLE WindowFromPoint(POINT Point);

    [DllImport("User32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(HANDLE hWnd, out uint lpdwProcessId);
}
