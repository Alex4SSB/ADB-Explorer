using Vanara.PInvoke;
using static Vanara.PInvoke.User32;

namespace ADB_Explorer.Services;

// A device dropped onto a File Explorer window opens in a new tab of it. There's no API for a tab, so the window is asked
// for one as Ctrl+T does, and the tab that appears among the shell's windows is navigated to the mount.
public sealed partial class DeviceMount
{
    private const string EXPLORER_WINDOW_CLASS = "CabinetWClass";

    private const string EXPLORER_TAB_HOST_CLASS = "ShellTabWindowClass";

    // The command File Explorer's own new tab button sends (Windows 11)
    private const int EXPLORER_NEW_TAB_COMMAND = 0xA21B;

    private static readonly TimeSpan NEW_TAB_WAIT = TimeSpan.FromSeconds(5);

    /// <summary>Whether the top-level window is a File Explorer window.</summary>
    public static bool IsExplorerWindow(HWND window) => ClassOf(window) == EXPLORER_WINDOW_CLASS;

    private static string ClassOf(HWND window)
    {
        var name = new StringBuilder(256);

        return GetClassName(window, name, name.Capacity) > 0 ? name.ToString() : "";
    }

    /// <summary>
    /// Opens a new tab in the File Explorer window, at <paramref name="localPath"/>, and brings the window to the front.
    /// Null where the window has no tabs (Windows 10) or no tab came, for a window of its own to open instead.
    /// </summary>
    private static object? OpenInNewTab(HWND explorerWindow, string localPath)
    {
        if (OpenNewTab(explorerWindow) is not { } tab)
            return null;

        try
        {
            ((dynamic)tab).Navigate2(localPath);
        }
        catch (Exception e)
        {
#if !DEPLOY
            DebugLog.PrintLine($"Navigating the new File Explorer tab failed: {e.Message}");
#endif
            return null;
        }

        SetForegroundWindow(explorerWindow);
        WaitForLocation(tab, localPath, TimeSpan.FromSeconds(10));

        return tab;
    }

    /// <summary>A new tab of the window, asked for as its new tab button does; null if none appears.</summary>
    private static object? OpenNewTab(HWND explorerWindow)
    {
        var tabHost = FindWindowEx(explorerWindow, HWND.NULL, EXPLORER_TAB_HOST_CLASS, null);
        if (tabHost.IsNull)
            return null;

        var before = TabsOf(explorerWindow).Select(IdentityOf).ToHashSet();
        PostMessage(tabHost, (uint)WindowMessage.WM_COMMAND, EXPLORER_NEW_TAB_COMMAND, IntPtr.Zero);

        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < NEW_TAB_WAIT)
        {
            Thread.Sleep(100);

            if (TabsOf(explorerWindow).FirstOrDefault(tab => !before.Contains(IdentityOf(tab))) is { } tab)
                return tab;
        }

        return null;
    }

    /// <summary>The tabs of a File Explorer window: each is a shell window of its own, sharing the window's handle.</summary>
    private static IEnumerable<object> TabsOf(HWND explorerWindow) => ExplorerWindows().Where(window => HandleOf(window) == explorerWindow);

    private static HWND HandleOf(object window)
    {
        try
        {
            return new HWND(new IntPtr(Convert.ToInt64(((dynamic)window).HWND)));
        }
        catch (Exception)
        {
            return HWND.NULL;
        }
    }

    /// <summary>A COM object's identity, the same however often the shell hands it out.</summary>
    private static IntPtr IdentityOf(object window)
    {
        var unknown = Marshal.GetIUnknownForObject(window);
        Marshal.Release(unknown);

        return unknown;
    }

    private static void WaitForLocation(object window, string localPath, TimeSpan timeout)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < timeout && !PathsEqual(LocationOf(window), localPath))
            Thread.Sleep(100);
    }
}
