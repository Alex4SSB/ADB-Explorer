namespace ADB_Explorer.Services;

public static class DebugLog
{
    private static readonly Mutex mutex = new();

    public static void PrintLine(string message)
    {
        mutex.WaitOne();
        
        if (!string.IsNullOrEmpty(Properties.Resources.DragDropLogPath))
            File.AppendAllText(Properties.Resources.DragDropLogPath, $"{DateTime.Now} | {message}\n");

        mutex.ReleaseMutex();
    }
}
