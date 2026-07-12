namespace ADB_Explorer.Services;

public static class DebugLog
{
    private static readonly Mutex mutex = new();

    // usable only on a dev machine, where the log dir exists
    private static readonly bool logPathUsable =
        !string.IsNullOrEmpty(Properties.AppGlobal.DragDropLogPath)
        && Directory.Exists(Path.GetDirectoryName(Properties.AppGlobal.DragDropLogPath));

    public static void PrintLine(string message)
    {
        if (!logPathUsable)
            return;

        mutex.WaitOne();
        try
        {
            File.AppendAllText(Properties.AppGlobal.DragDropLogPath, $"{DateTime.Now:HH:mm:ss:fff} | {message}\n");
        }
        catch (SystemException)
        {
            // never crash over a log write
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }
}
