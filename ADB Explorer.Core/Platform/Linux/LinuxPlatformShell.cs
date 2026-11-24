namespace ADB_Explorer.Platform.Linux;

/// <summary>
/// The freedesktop implementation: <c>xdg-open</c> for opening, the FileManager1 D-Bus interface for revealing,
/// and <see cref="FileManagerNameLookup"/> for the file manager's name.
/// </summary>
public sealed class LinuxPlatformShell : IPlatformShell
{
    private readonly FileManagerNameLookup _fileManagerName = FileManagerNameLookup.ForCurrentSystem();

    public string AdbExecutableName => "adb";

    public string? GetFileManagerName(CultureInfo culture) => _fileManagerName.GetName(culture);

    public void OpenFolder(string folderPath) => Run("xdg-open", folderPath);

    public void RevealFile(string filePath)
    {
        // Nautilus, Dolphin, Nemo and others select the file through FileManager1; xdg-open only opens the folder
        var uri = new Uri(filePath).AbsoluteUri;
        if (RunAndWait("dbus-send", "--session", "--print-reply", "--dest=org.freedesktop.FileManager1", "--type=method_call",
                "/org/freedesktop/FileManager1", "org.freedesktop.FileManager1.ShowItems", $"array:string:{uri}", "string:"))
            return;

        if (Path.GetDirectoryName(filePath) is { } folder)
            OpenFolder(folder);
    }

    public void OpenWithDefaultApp(string path) => Run("xdg-open", path);

    public void KillProcesses(string processName)
    {
        foreach (var process in Process.GetProcessesByName(processName))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Already exited, or not ours to kill
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    /// <summary>Starts <paramref name="fileName"/> without a shell, so paths need no quoting.</summary>
    private static void Run(string fileName, params string[] arguments) => Start(fileName, arguments)?.Dispose();

    /// <summary>Runs <paramref name="fileName"/> and reports whether it exited successfully within 2 seconds.</summary>
    private static bool RunAndWait(string fileName, params string[] arguments)
    {
        using var process = Start(fileName, arguments, redirect: true);
        if (process is null)
            return false;

        // Drain the output, or a full pipe could block the process
        _ = process.StandardOutput.ReadToEndAsync();
        _ = process.StandardError.ReadToEndAsync();

        return process.WaitForExit(2000) && process.ExitCode == 0;
    }

    private static Process? Start(string fileName, string[] arguments, bool redirect = false)
    {
        try
        {
            ProcessStartInfo info = new(fileName)
            {
                UseShellExecute = false,
                RedirectStandardOutput = redirect,
                RedirectStandardError = redirect,
            };

            foreach (var argument in arguments)
            {
                info.ArgumentList.Add(argument);
            }

            return Process.Start(info);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
