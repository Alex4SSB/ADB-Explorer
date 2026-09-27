namespace ADB_Explorer.Services;

public static class DebugLog
{
    private static readonly Mutex _mutex = new();

    public static void PrintLine(string message)
    {
        _mutex.WaitOne();
        
        if (!string.IsNullOrEmpty(Properties.AppGlobal.DragDropLogPath))
            File.AppendAllText(Properties.AppGlobal.DragDropLogPath, $"{DateTime.Now:HH:mm:ss:fff} | {message}\n");

        _mutex.ReleaseMutex();
    }
}
