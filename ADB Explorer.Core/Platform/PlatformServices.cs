namespace ADB_Explorer.Platform;

/// <summary>
/// The current platform's implementations, set once by the app at startup.
/// Static so that static Core code (such as <c>Strings.Formatted</c>) can reach them.
/// </summary>
public static class PlatformServices
{
    public static IPlatformShell? Shell { get; set; }
}
