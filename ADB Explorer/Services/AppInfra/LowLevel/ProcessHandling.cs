namespace ADB_Explorer.Services;

public class ProcessHandling
{
    public static void KillProcess(Process process, bool recursive = true)
    {
        var procs = GetChildProcesses(process, recursive);

        foreach (var item in procs)
        {
            item.Kill();
        }
    }

    public static IEnumerable<Process> GetChildProcesses(Process process, bool recursive = true)
    {
        ManagementObjectSearcher searcher = new(
            "SELECT * " +
            "FROM Win32_Process " +
            "WHERE ParentProcessId=" + process.Id);

        foreach (var item in searcher.Get())
        {
            var proc = Process.GetProcessById((int)(uint)item["ProcessId"]);

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
    }
}
