namespace ADB_Explorer.Services;

#pragma warning disable SYSLIB1054 // Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time

public static partial class NativeMethods
{
    public sealed class InterceptMouse : IDisposable
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
        private static Action<POINT> _mouseMoveAction;
        private static Action _rButtonAction;
        public static POINT MousePosition { get; private set; }
        
        public static HANDLE WindowUnderMouse { get; private set; }

        private delegate HANDLE LowLevelMouseProc(int nCode, MouseMessages wParam, HANDLE lParam);

        public static void Init(Action<POINT> mouseMoveAction, Action rButtonAction)
        {
            _mouseMoveAction = mouseMoveAction;
            _rButtonAction = rButtonAction;

            _mouseHookID = SetHook(_mouseProc);
        }

        public static void Close()
        {
            UnhookWindowsHookEx(_mouseHookID);
        }

        public void Dispose() => Close();

        private static HANDLE SetHook(LowLevelMouseProc proc)
        {
            using Process curProcess = Process.GetCurrentProcess();
            using ProcessModule curModule = curProcess.MainModule;

            return SetWindowsHookEx(WinHooks.WH_MOUSE_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
        }

        private static HANDLE HookCallback(int nCode, MouseMessages wParam, HANDLE lParam)
        {
            if (nCode < 0 || wParam is not MouseMessages.WM_MOUSEMOVE and not MouseMessages.WM_RBUTTONDOWN)
            {
                return CallNextHookEx(_mouseHookID, nCode, wParam, lParam);
            }

            var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);

            if (wParam is MouseMessages.WM_RBUTTONDOWN)
            {
                _rButtonAction?.Invoke();
            }
            else
            {
                POINT newPoint = new(hookStruct.pt.X, hookStruct.pt.Y);
                if (newPoint != MousePosition)
                    _mouseMoveAction?.Invoke(MousePosition);

                MousePosition = newPoint;
            }
            
            return CallNextHookEx(_mouseHookID, nCode, wParam, lParam);
        }

        public static HANDLE GetWindowUnderMouse()
        {
            WindowUnderMouse = GetAncestor(WindowFromPoint(MousePosition), GaFlags.GA_ROOT);
            return WindowUnderMouse;
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

        [DllImport("User32.dll", ExactSpelling = true)]
        private static extern HANDLE GetAncestor(HANDLE hwnd, GaFlags gaFlags);
    }
}
