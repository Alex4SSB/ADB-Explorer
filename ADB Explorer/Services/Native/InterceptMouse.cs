namespace ADB_Explorer.Services;

public static partial class NativeMethods
{
    public sealed partial class InterceptMouse : IDisposable
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

        private static readonly LowLevelMouseProc _mouseProc = HookCallback;
        private static readonly LowLevelKeyboardProc _keyboardProc = KeyboardHookCallback;
        private static HANDLE _mouseHookID = IntPtr.Zero;
        private static HANDLE _keyboardHookID = IntPtr.Zero;
        private static bool _isHooked;
        private static Action<POINT>? _mouseMoveAction;
        private static Action? _rButtonAction;
        private static Action<int>? _mouseWheelAction;
        private static Action? _escapeAction;
        public static POINT MousePosition { get; private set; }

        public static HANDLE WindowUnderMouse { get; private set; }

        private delegate HANDLE LowLevelMouseProc(int nCode, MouseMessages wParam, HANDLE lParam);

        private delegate HANDLE LowLevelKeyboardProc(int nCode, HANDLE wParam, HANDLE lParam);

        private const int WM_KEYDOWN = 0x0100;
        private const int VK_ESCAPE = 0x1B;

        /// <summary>
        /// Installs the low-level mouse hook. The hook is only needed while a drag is in progress,
        /// since a system-wide <see cref="WinHooks.WH_MOUSE_LL"/> hook adds latency to every mouse
        /// message and causes stutter while the UI thread is busy. Safe to call repeatedly.
        /// <paramref name="escapeAction"/> adds a keyboard hook, for an Esc pressed while another app has the focus.
        /// </summary>
        public static void Init(Action<POINT> mouseMoveAction, Action rButtonAction, Action<int>? mouseWheelAction = null, Action? escapeAction = null)
        {
            _mouseMoveAction = mouseMoveAction;
            _rButtonAction = rButtonAction;
            _mouseWheelAction = mouseWheelAction;
            _escapeAction = escapeAction;

            if (_isHooked)
                return;

            _mouseHookID = SetHook(WinHooks.WH_MOUSE_LL, Marshal.GetFunctionPointerForDelegate(_mouseProc));
            if (escapeAction is not null)
                _keyboardHookID = SetHook(WinHooks.WH_KEYBOARD_LL, Marshal.GetFunctionPointerForDelegate(_keyboardProc));

            _isHooked = true;
        }

        public static void Close()
        {
            if (!_isHooked)
                return;

            UnhookWindowsHookEx(_mouseHookID);
            _mouseHookID = IntPtr.Zero;

            if (_keyboardHookID != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_keyboardHookID);
                _keyboardHookID = IntPtr.Zero;
            }

            _isHooked = false;
        }

        /// <summary>
        /// Returns the current cursor position without relying on the hook, and refreshes
        /// <see cref="MousePosition"/>. Used to place the drag window before the hook is installed.
        /// </summary>
        public static POINT GetCursorPosition()
        {
            if (GetCursorPos(out var point))
                MousePosition = point;

            return MousePosition;
        }

        public void Dispose() => Close();

        private static HANDLE SetHook(WinHooks hook, HANDLE proc)
        {
            using Process curProcess = Process.GetCurrentProcess();
            using ProcessModule curModule = curProcess.MainModule;

            return SetWindowsHookEx(hook, proc, GetModuleHandle(curModule.ModuleName), 0);
        }

        private static HANDLE KeyboardHookCallback(int nCode, HANDLE wParam, HANDLE lParam)
        {
            // KBDLLHOOKSTRUCT starts with the virtual key code
            if (nCode >= 0 && wParam == WM_KEYDOWN && Marshal.ReadInt32(lParam) == VK_ESCAPE)
                _escapeAction?.Invoke();

            return CallNextHookEx(_keyboardHookID, nCode, wParam, lParam);
        }

        private static HANDLE HookCallback(int nCode, MouseMessages wParam, HANDLE lParam)
        {
            if (nCode < 0
                || wParam is not MouseMessages.WM_MOUSEMOVE
                    and not MouseMessages.WM_RBUTTONDOWN
                    and not MouseMessages.WM_MOUSEWHEEL)
            {
                return CallNextHookEx(_mouseHookID, nCode, (HANDLE)wParam, lParam);
            }

            var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);

            if (wParam is MouseMessages.WM_RBUTTONDOWN)
            {
                _rButtonAction?.Invoke();
            }
            else if (wParam is MouseMessages.WM_MOUSEWHEEL)
            {
                _mouseWheelAction?.Invoke((short)(hookStruct.mouseData >> 16));
            }
            else
            {
                POINT newPoint = hookStruct.pt;
                if (newPoint != MousePosition)
                {
                    // Update before invoking so the callback (and GetWindowUnderMouse) sees the
                    // fresh position. This matters when the cursor moves quickly out of the app.
                    MousePosition = newPoint;
                    _mouseMoveAction?.Invoke(newPoint);
                }
            }

            return CallNextHookEx(_mouseHookID, nCode, (HANDLE)wParam, lParam);
        }

        public static HANDLE GetWindowUnderMouse()
        {
            WindowUnderMouse = GetAncestor(WindowFromPoint(MousePosition), GaFlags.GA_ROOT);
            return WindowUnderMouse;
        }

        [LibraryImport("User32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
        private static partial HANDLE SetWindowsHookEx(WinHooks idHook,
            HANDLE lpfn, HANDLE hMod, uint dwThreadId);

        [LibraryImport("User32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool UnhookWindowsHookEx(HANDLE hhk);

        [LibraryImport("User32.dll", SetLastError = true)]
        private static partial HANDLE CallNextHookEx(HANDLE hhk, int nCode,
            HANDLE wParam, HANDLE lParam);

        [LibraryImport("Kernel32.dll", EntryPoint = "GetModuleHandleW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        private static partial HANDLE GetModuleHandle(string lpModuleName);

        [LibraryImport("User32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetCursorPos(out POINT lpPoint);

        [LibraryImport("User32.dll")]
        private static partial HANDLE WindowFromPoint(POINT Point);

        [LibraryImport("User32.dll")]
        private static partial HANDLE GetAncestor(HANDLE hwnd, GaFlags gaFlags);
    }
}
