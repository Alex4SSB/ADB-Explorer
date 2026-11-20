namespace ADB_Explorer.Services;

using Vanara.PInvoke;
using static Vanara.PInvoke.Kernel32;
using static Vanara.PInvoke.User32;

public static partial class NativeMethods
{
    public sealed partial class InterceptMouse : IDisposable
    {
        // https://learn.microsoft.com/en-us/archive/blogs/toub/low-level-mouse-hook-in-c

        private static readonly HookProc _mouseProc = HookCallback;
        private static readonly HookProc _keyboardProc = KeyboardHookCallback;
        private static SafeHHOOK? _mouseHook;
        private static SafeHHOOK? _keyboardHook;
        private static bool _isHooked;
        private static Action<POINT>? _mouseMoveAction;
        private static Action? _rButtonAction;
        private static Action<int>? _mouseWheelAction;
        private static Action? _escapeAction;
        public static POINT MousePosition { get; private set; }

        public static HWND WindowUnderMouse { get; private set; }


        /// <summary>
        /// Installs the low-level mouse hook. The hook is only needed while a drag is in progress,
        /// since a system-wide <see cref="HookType.WH_MOUSE_LL"/> hook adds latency to every mouse
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

            _mouseHook = SetHook(HookType.WH_MOUSE_LL, _mouseProc);
            if (escapeAction is not null)
                _keyboardHook = SetHook(HookType.WH_KEYBOARD_LL, _keyboardProc);

            _isHooked = true;
        }

        public static void Close()
        {
            if (!_isHooked)
                return;

            // Disposing a SafeHHOOK unhooks it
            _mouseHook?.Dispose();
            _mouseHook = null;
            _keyboardHook?.Dispose();
            _keyboardHook = null;

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

        private static SafeHHOOK SetHook(HookType hook, HookProc proc)
        {
            using Process curProcess = Process.GetCurrentProcess();
            using ProcessModule curModule = curProcess.MainModule;
            var module = GetModuleHandle(curModule.ModuleName);
            var safeHook = SetWindowsHookEx(hook, proc, module);

            // GetModuleHandle doesn't add a reference, so the module must not be freed with it
            module.SetHandleAsInvalid();

            return safeHook;
        }

        // CallNextHookEx ignores its hook handle
        private static IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            // KBDLLHOOKSTRUCT starts with the virtual key code
            if (nCode >= 0 && (WindowMessage)wParam is WindowMessage.WM_KEYDOWN && (VK)Marshal.ReadInt32(lParam) is VK.VK_ESCAPE)
                _escapeAction?.Invoke();

            return CallNextHookEx(HHOOK.NULL, nCode, wParam, lParam);
        }

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            var message = (WindowMessage)wParam;
            if (nCode < 0
                || message is not WindowMessage.WM_MOUSEMOVE
                    and not WindowMessage.WM_RBUTTONDOWN
                    and not WindowMessage.WM_MOUSEWHEEL)
            {
                return CallNextHookEx(HHOOK.NULL, nCode, wParam, lParam);
            }

            var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);

            if (message is WindowMessage.WM_RBUTTONDOWN)
            {
                _rButtonAction?.Invoke();
            }
            else if (message is WindowMessage.WM_MOUSEWHEEL)
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

            return CallNextHookEx(HHOOK.NULL, nCode, wParam, lParam);
        }

        public static HWND GetWindowUnderMouse()
        {
            WindowUnderMouse = GetAncestor(WindowFromPoint(MousePosition), GetAncestorFlag.GA_ROOT);
            return WindowUnderMouse;
        }
    }
}
