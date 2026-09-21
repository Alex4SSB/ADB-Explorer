using System.Runtime.InteropServices;

namespace ADB_Explorer.Helpers;

/// <summary>Keeps the app to a single instance per Windows session, whichever copy of the executable is launched.</summary>
internal static class AppInstanceGuard
{
    private const string MutexName = @"Local\ADB Explorer single instance";
    private const int SW_RESTORE = 9;

    // Held for the life of the process; the mutex is released when it exits.
    private static Mutex? _mutex;

    /// <summary>Claims the single-instance mutex; false when another instance already holds it.</summary>
    public static bool TryClaim()
    {
        _mutex = new Mutex(true, MutexName, out var createdNew);
        return createdNew;
    }

    /// <summary>Brings the running instance's window to the front, restoring it if minimized.</summary>
    public static void ActivateExisting()
    {
        var others = OtherProcesses();

        try
        {
            var hwnd = others.Select(process => process.MainWindowHandle).FirstOrDefault(handle => handle != IntPtr.Zero);
            if (hwnd == IntPtr.Zero)
                return;

            if (IsIconic(hwnd))
                ShowWindow(hwnd, SW_RESTORE);

            SetForegroundWindow(hwnd);
        }
        finally
        {
            others.ForEach(process => process.Dispose());
        }
    }

    private static List<Process> OtherProcesses()
    {
        using var current = Process.GetCurrentProcess();

        return [.. Process.GetProcessesByName(current.ProcessName)
            .Where(process => process.Id != current.Id && process.SessionId == current.SessionId)];
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
