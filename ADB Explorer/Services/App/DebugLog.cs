#if !DEPLOY
using System.Reflection;
using Microsoft.Win32;

namespace ADB_Explorer.Services;

/// <summary>
/// File log for non-Deploy builds. The log file's location is kept in the gitignored
/// <c>DebugLog.path</c> next to the csproj, so each dev machine sets its own.
/// </summary>
public static class DebugLog
{
    private static readonly Lock _lock = new();

    /// <summary>Null when logging is off for this session.</summary>
    public static string? LogPath { get; private set; }

    /// <summary>Reads the log location, asking for one when it's missing or unusable. Call once at startup.</summary>
    public static void Init()
    {
        // Embedded at build time; see the AssemblyMetadata item in the csproj
        var pathFile = typeof(DebugLog).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "DebugLogPathFile")?.Value;
        if (pathFile is null)
            return;

        var logPath = File.Exists(pathFile) ? File.ReadAllText(pathFile).Trim() : "";
        if (TryCreate(logPath))
        {
            LogPath = logPath;
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Choose the debug log file (Cancel to skip logging this session)",
            FileName = "log.txt",
            Filter = "Log files|*.txt;*.log|All files|*.*",
            OverwritePrompt = false,
        };
        if (dialog.ShowDialog() is not true)
            return;

        if (!TryCreate(dialog.FileName))
        {
            System.Windows.MessageBox.Show($"Can't create {dialog.FileName}\nDebug logging is off for this session.",
                "Debug log", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        LogPath = dialog.FileName;
        File.WriteAllText(pathFile, LogPath);
    }

    private static bool TryCreate(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.AppendAllText(path, "");
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    public static void PrintLine(string message)
    {
        if (LogPath is null)
            return;

        lock (_lock)
        {
            try
            {
                File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss:fff} | {message}\n");
            }
            catch (IOException) { } // e.g. the file is open elsewhere; a lost debug line isn't worth a crash
        }
    }
}
#endif
