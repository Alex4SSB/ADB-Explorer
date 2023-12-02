namespace ADB_Explorer.Services;

internal class ProcessHandling
{
    public static void KillProcess(Process process, bool recursive = true) =>
        KillProcess(process.Id, recursive);

    public static void KillProcess(int parentProcessId, bool recursive = true)
    {
        if (recursive)
        {
            ManagementObjectSearcher searcher = new(
            "SELECT * " +
            "FROM Win32_Process " +
            "WHERE ParentProcessId=" + parentProcessId);

            foreach (var item in searcher.Get())
            {
                int childProcessId = (int)(uint)item["ProcessId"];
                KillProcess(childProcessId);
            }
        }

        try
        {
            Process.GetProcessById(parentProcessId).Kill();
        }
        catch { }
    }
}
