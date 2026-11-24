using Vanara.Windows.Shell;

namespace ADB_Explorer.Services;

public sealed partial class WindowsPlatformShell : IPlatformShell
{
    // File Explorer's AppUserModelID; its Start menu entry carries the localized name
    private const string FileExplorerAppsFolderPath = @"shell:AppsFolder\Microsoft.Windows.Explorer";

    private readonly Lazy<string?> _fileManagerName = new(ReadFileManagerName);

    private readonly Lazy<CultureInfo?> _displayCulture = new(ReadDisplayCulture);

    public string AdbExecutableName => "adb.exe";

    /// <summary>
    /// The shell names its items only in the Windows display language,
    /// so another language gets <see langword="null"/>.
    /// </summary>
    public string? GetFileManagerName(CultureInfo culture)
    {
        if (_displayCulture.Value is not { } displayCulture || !CultureHelper.IsSameOrParent(culture, displayCulture))
            return null;

        return _fileManagerName.Value;
    }

    public void OpenFolder(string folderPath) => Process.Start("explorer.exe", $"\"{folderPath}\"");

    public void RevealFile(string filePath) => Process.Start("explorer.exe", $"/select,\"{filePath}\"");

    public void OpenWithDefaultApp(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

    public void KillProcesses(string processName)
    {
        foreach (var process in Process.GetProcessesByName(processName))
        {
            ProcessHandling.KillProcess(process);
        }
    }

    /// <summary>File Explorer's name as the Start menu shows it, in the Windows display language.</summary>
    internal static string? ReadFileManagerName()
    {
        try
        {
            using var item = new ShellItem(FileExplorerAppsFolderPath);
            var name = item.Name;

            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch (Exception e) when (e is COMException or FileNotFoundException or ArgumentException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal static CultureInfo? ReadDisplayCulture()
    {
        try
        {
            return CultureInfo.GetCultureInfo(GetUserDefaultUILanguage());
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }

    [LibraryImport("kernel32.dll")]
    private static partial ushort GetUserDefaultUILanguage();
}
