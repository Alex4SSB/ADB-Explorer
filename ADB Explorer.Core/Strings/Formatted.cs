namespace ADB_Explorer.Strings;

/// <summary>
/// Resource strings with the OS file manager's name filled in: the OS's own name in the app's language when
/// the OS has one, else <see cref="Resources.S_FILE_EXPLORER"/>. Use these instead of the raw <see cref="Resources"/> members.
/// </summary>
public static class Formatted
{
    public static string FileManagerName
        => PlatformServices.Shell?.GetFileManagerName(Resources.Culture ?? CultureInfo.CurrentUICulture)
           ?? Resources.S_FILE_EXPLORER;

    private static string Explorer(string format) => string.Format(format, FileManagerName);

    public static string S_UPDATE_APPLY_FAILED => Explorer(Resources.S_UPDATE_APPLY_FAILED);

    public static string S_MENU_OPEN_IN_WINDOWS_EXPLORER => Explorer(Resources.S_MENU_OPEN_IN_WINDOWS_EXPLORER);

    public static string S_MOUNT_FAILED_TITLE => Explorer(Resources.S_MOUNT_FAILED_TITLE);

    public static string S_MOUNT_OPENING => Explorer(Resources.S_MOUNT_OPENING);

    public static string S_DRAG_OPEN_IN_EXPLORER => Explorer(Resources.S_DRAG_OPEN_IN_EXPLORER);
}
