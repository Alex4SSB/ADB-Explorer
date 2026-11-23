namespace ADB_Explorer.Platform;

/// <summary>
/// Interaction with the host OS shell and processes.
/// </summary>
public interface IPlatformShell
{
    /// <summary>Opens <paramref name="folderPath"/> in the OS file manager.</summary>
    void OpenFolder(string folderPath);

    /// <summary>Opens the containing folder in the OS file manager and selects <paramref name="filePath"/> where supported.</summary>
    void RevealFile(string filePath);

    /// <summary>Opens <paramref name="path"/> with its default app.</summary>
    void OpenWithDefaultApp(string path);

    /// <summary>Force-kills every process named <paramref name="processName"/> (no extension).</summary>
    void KillProcesses(string processName);

    /// <summary>File name of the adb executable on this OS, e.g. <c>adb.exe</c>.</summary>
    string AdbExecutableName { get; }

    /// <summary>
    /// The OS file manager's name in the OS display language (File Explorer, Files, Dolphin),
    /// or <see langword="null"/> if the OS can't provide it.
    /// </summary>
    string? FileManagerName { get; }

    /// <summary>The language <see cref="FileManagerName"/> is in, or <see langword="null"/> if unknown.</summary>
    CultureInfo? FileManagerNameCulture { get; }
}
