namespace ADB_Explorer.Services;

using Vanara.PInvoke;
using static Vanara.PInvoke.Kernel32;
using static Vanara.PInvoke.User32;

public static partial class NativeMethods
{
    public sealed partial class MouseHook : IDisposable
    {
        // https://learn.microsoft.com/en-us/archive/blogs/toub/low-level-mouse-hook-in-c

        private static readonly HookProc _mouseProc = HookCallback;
        private static readonly HookProc _keyboardProc = KeyboardHookCallback;
        private static readonly Action _dispatchMove = DispatchMove;
        private static readonly object _hookLock = new();
        private static uint _hookThreadId;
        private static bool _isHooked;

        // Counts Init's threads, so one that starts after a Close (or a newer Init) knows it's outdated
        private static int _hookGeneration;
        private static Action<POINT>? _mouseMoveAction;
        private static Action? _rButtonAction;
        private static Action<int>? _mouseWheelAction;
        private static Action? _escapeAction;
        private static Action<HWND>? _windowChangedAction;

        // The position is packed into one long, as the hook thread writes it while the UI thread reads it
        private static long _mousePosition;
        private static int _isMovePending;
        private static HWND _lastWindowFromPoint;

        public static POINT MousePosition
        {
            get
            {
                var packed = Interlocked.Read(ref _mousePosition);
                return new((int)packed, (int)(packed >> 32));
            }
            private set => Interlocked.Exchange(ref _mousePosition, (uint)value.X | ((long)value.Y << 32));
        }

        /// <summary>The top-level window under the cursor, as the hook last found it.</summary>
        public static HWND WindowUnderMouse { get; private set; }

        // A window the hook thread moves with the cursor, and its offset from it, packed as the position is
        private static IntPtr _follower;
        private static long _followerOffset;

        /// <summary>
        /// Has the hook thread move <paramref name="window"/> with the cursor, keeping it <paramref name="offset"/>
        /// physical pixels away. It keeps moving while the UI thread is blocked, e.g. in a call to another app's drop target.
        /// </summary>
        public static void SetFollower(HWND window, POINT offset)
        {
            Interlocked.Exchange(ref _followerOffset, (uint)offset.X | ((long)offset.Y << 32));
            Interlocked.Exchange(ref _follower, (IntPtr)window);
        }

        public static void ClearFollower() => Interlocked.Exchange(ref _follower, IntPtr.Zero);

        private static void MoveFollower(POINT point)
        {
            var window = Interlocked.CompareExchange(ref _follower, IntPtr.Zero, IntPtr.Zero);
            if (window == IntPtr.Zero)
                return;

            var offset = Interlocked.Read(ref _followerOffset);

            // Async, so a busy UI thread that owns the window never blocks the hook
            SetWindowPos(window, HWND.NULL, point.X + (int)offset, point.Y + (int)(offset >> 32), 0, 0,
                SetWindowPosFlags.SWP_NOSIZE | SetWindowPosFlags.SWP_NOZORDER | SetWindowPosFlags.SWP_NOACTIVATE | SetWindowPosFlags.SWP_ASYNCWINDOWPOS);
        }


        /// <summary>
        /// Installs the low-level mouse hook on its own thread, so a busy UI thread never delays system-wide mouse input.
        /// The hook is only needed while a drag is in progress. Safe to call repeatedly. Actions run on the UI thread,
        /// except <paramref name="windowChangedAction"/>, which runs on the hook thread before the move reaches any window.
        /// <paramref name="escapeAction"/> adds a keyboard hook, for an Esc pressed while another app has the focus.
        /// </summary>
        public static void Init(Action<POINT> mouseMoveAction,
                                Action rButtonAction,
                                Action<int>? mouseWheelAction = null,
                                Action? escapeAction = null,
                                Action<HWND>? windowChangedAction = null)
        {
            _mouseMoveAction = mouseMoveAction;
            _rButtonAction = rButtonAction;
            _mouseWheelAction = mouseWheelAction;
            _escapeAction = escapeAction;
            _windowChangedAction = windowChangedAction;

            lock (_hookLock)
            {
                if (_isHooked)
                    return;

                _lastWindowFromPoint = HWND.NULL;
                WindowUnderMouse = HWND.NULL;

                var generation = ++_hookGeneration;
                var withKeyboard = escapeAction is not null;
                var thread = new Thread(() => HookThread(generation, withKeyboard))
                {
                    IsBackground = true,
                    Name = "Drag mouse hook",
                    Priority = ThreadPriority.AboveNormal,
                };

                _isHooked = true;
                thread.Start();
            }
        }

        public static void Close()
        {
            lock (_hookLock)
            {
                if (!_isHooked)
                    return;

                ClearFollower();

                // The hook thread unhooks once its message loop ends. One that hasn't registered yet
                // finds itself outdated when it does, and ends on its own.
                if (_hookThreadId != 0)
                    PostThreadMessage(_hookThreadId, (uint)WindowMessage.WM_QUIT);

                _hookThreadId = 0;
                _isHooked = false;
            }
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

        private static void HookThread(int generation, bool withKeyboard)
        {
            // Creates the thread's message queue, so WM_QUIT can be posted to it as soon as it's registered
            PeekMessage(out _, HWND.NULL, 0, 0);

            lock (_hookLock)
            {
                // Closed, or closed and hooked again, before this thread got here
                if (!_isHooked || generation != _hookGeneration)
                    return;

                _hookThreadId = GetCurrentThreadId();
            }

            var mouseHook = SetHook(HookType.WH_MOUSE_LL, _mouseProc);
            SafeHHOOK? keyboardHook = null;
            if (withKeyboard)
                keyboardHook = SetHook(HookType.WH_KEYBOARD_LL, _keyboardProc);

            // Low-level hooks are called through this thread's message loop
            while (GetMessage(out var msg) > 0)
            {
                TranslateMessage(msg);
                DispatchMessage(msg);
            }

            // Disposing a SafeHHOOK unhooks it
            mouseHook.Dispose();
            keyboardHook?.Dispose();
        }

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
            if (nCode >= 0 && (WindowMessage)wParam is WindowMessage.WM_KEYDOWN && (VK)Marshal.ReadInt32(lParam) is VK.VK_ESCAPE && _escapeAction is { } escapeAction)
                App.SafeBeginInvoke(escapeAction);

            return CallNextHookEx(HHOOK.NULL, nCode, wParam, lParam);
        }

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode < 0)
                return CallNextHookEx(HHOOK.NULL, nCode, wParam, lParam);

            // MSLLHOOKSTRUCT: POINT pt at 0, DWORD mouseData at 8
            switch ((WindowMessage)wParam)
            {
                case WindowMessage.WM_MOUSEMOVE:
                    var newPoint = new POINT(Marshal.ReadInt32(lParam), Marshal.ReadInt32(lParam, 4));
                    if (newPoint != MousePosition)
                    {
                        MousePosition = newPoint;
                        MoveFollower(newPoint);
                        FollowWindowUnderMouse(newPoint);

                        // Moves are coalesced: one dispatch at a time, which reads the latest position.
                        // Send, as Input ranks below Render and bindings, so moves would wait behind them.
                        if (Interlocked.Exchange(ref _isMovePending, 1) == 0)
                            App.SafeBeginInvoke(_dispatchMove, DispatcherPriority.Send);
                    }
                    break;

                case WindowMessage.WM_RBUTTONDOWN when _rButtonAction is { } rButtonAction:
                    App.SafeBeginInvoke(rButtonAction);
                    break;

                case WindowMessage.WM_MOUSEWHEEL when _mouseWheelAction is { } mouseWheelAction:
                    var delta = (short)(Marshal.ReadInt32(lParam, 8) >> 16);
                    App.SafeBeginInvoke(() => mouseWheelAction(delta), DispatcherPriority.Input);
                    break;
            }

            return CallNextHookEx(HHOOK.NULL, nCode, wParam, lParam);
        }

        private static void DispatchMove()
        {
            // Cleared before reading, so a move that arrives meanwhile schedules another dispatch
            Volatile.Write(ref _isMovePending, 0);
            _mouseMoveAction?.Invoke(MousePosition);
        }

        // Hook thread only. The root is only looked up again when the window under the cursor changes
        private static void FollowWindowUnderMouse(POINT point)
        {
            var window = WindowFromPoint(point);
            if (window == _lastWindowFromPoint)
                return;

            _lastWindowFromPoint = window;
            var root = GetAncestor(window, GetAncestorFlag.GA_ROOT);
            if (root == WindowUnderMouse)
                return;

            WindowUnderMouse = root;
            _windowChangedAction?.Invoke(root);
        }

        /// <summary>Finds the top-level window under the cursor now, for when the hook isn't installed.</summary>
        public static HWND GetWindowUnderMouse()
        {
            WindowUnderMouse = GetAncestor(WindowFromPoint(MousePosition), GetAncestorFlag.GA_ROOT);
            return WindowUnderMouse;
        }
    }
}
