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
}
