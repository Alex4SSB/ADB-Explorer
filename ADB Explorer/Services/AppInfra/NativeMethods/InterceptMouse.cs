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

    private static LowLevelMouseProc _proc = HookCallback;
    private static HANDLE _hookID = IntPtr.Zero;
    private static Action<int, int> _externalAction;
    private static MouseMessages _requestedEvent;

    private delegate HANDLE LowLevelMouseProc(int nCode, MouseMessages wParam, HANDLE lParam);

    public static void InitInterceptMouse(MouseMessages mouseEvent, Action<int, int> action)
    {
        _requestedEvent = mouseEvent;
        _externalAction = action;

        _hookID = SetHook(_proc);
    }

    public static void CloseInterceptMouse()
    {
        UnhookWindowsHookEx(_hookID);
    }

    private static HANDLE SetHook(LowLevelMouseProc proc)
    {
        using Process curProcess = Process.GetCurrentProcess();
        using ProcessModule curModule = curProcess.MainModule;

        return SetWindowsHookEx(WinHooks.WH_MOUSE_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
    }

    private static HANDLE HookCallback(int nCode, MouseMessages wParam, HANDLE lParam)
    {
        if (nCode >= 0 && wParam == _requestedEvent)
        {
            var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);

            _externalAction?.Invoke(hookStruct.pt.X, hookStruct.pt.Y);
        }

        return CallNextHookEx(_hookID, nCode, wParam, lParam);
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
}
