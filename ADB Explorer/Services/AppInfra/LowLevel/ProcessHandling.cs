using ADB_Explorer.Models;

namespace ADB_Explorer.Services;

public class ProcessHandling
{
    public static void KillProcess(Process process, bool recursive = true)
    {
        var procs = GetChildProcesses(process, recursive);

        foreach (var item in procs)
        {
            try
            {
                item.Kill();
            }
            catch
            { }
        }
    }

    public static IEnumerable<Process> GetChildProcesses(Process process, bool recursive = true)
    {
        ManagementObjectSearcher searcher;
        try
        {
            searcher = new(
                "SELECT * " +
                "FROM Win32_Process " +
                "WHERE ParentProcessId=" + process.Id);
        }
        catch
        {
            // if we can't get the pid without throwing an exception, the process is useless
            yield break;
        }

        foreach (var item in searcher.Get())
        {
            Process proc;

            try
            {
                proc = Process.GetProcessById((int)(uint)item["ProcessId"]);
            }
            catch (Exception)
            {
                continue;
            }

            if (recursive)
            {
                foreach (var subItem in GetChildProcesses(proc))
                {
                    yield return subItem;
                }
            }
            else
                yield return proc;
        }

        yield return process;
    }

    public static int GetProcessIdFromWindowHandle(HANDLE hwnd)
    {
        GetWindowThreadProcessId(hwnd, out uint processId);
        return (int)processId;
    }

    [DllImport("User32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(HANDLE hWnd, out uint lpdwProcessId);
}
