namespace ADB_Explorer.Strings;

/// <summary>
/// Resource strings with the OS file manager's name filled in: the OS's own name when it's in the app's language,
/// else <see cref="Resources.S_FILE_EXPLORER"/>. Use these instead of the raw <see cref="Resources"/> members.
/// </summary>
public static class Formatted
{
    public static string FileManagerName
    {
        get
        {
            var appCulture = Resources.Culture ?? CultureInfo.CurrentUICulture;
            if (PlatformServices.Shell is { FileManagerName: { } name, FileManagerNameCulture: { } nameCulture }
                && IsSameOrParent(appCulture, nameCulture))
                return name;

            return Resources.S_FILE_EXPLORER;
        }
    }

    /// <summary>
    /// Whether <paramref name="appCulture"/> is <paramref name="nameCulture"/> or one of its parents (ru for ru-RU).
    /// Sibling regions don't match: zh-TW and zh-CN, or pt-BR and pt-PT, name the file manager differently.
    /// </summary>
    internal static bool IsSameOrParent(CultureInfo appCulture, CultureInfo nameCulture)
    {
        for (var culture = nameCulture; !culture.Equals(CultureInfo.InvariantCulture); culture = culture.Parent)
        {
            if (culture.Equals(appCulture))
                return true;
        }

        return false;
    }

    private static string Explorer(string format) => string.Format(format, FileManagerName);

    public static string S_UPDATE_APPLY_FAILED => Explorer(Resources.S_UPDATE_APPLY_FAILED);

    public static string S_MENU_OPEN_IN_WINDOWS_EXPLORER => Explorer(Resources.S_MENU_OPEN_IN_WINDOWS_EXPLORER);

    public static string S_MOUNT_FAILED_TITLE => Explorer(Resources.S_MOUNT_FAILED_TITLE);

    public static string S_MOUNT_OPENING => Explorer(Resources.S_MOUNT_OPENING);

    public static string S_DRAG_OPEN_IN_EXPLORER => Explorer(Resources.S_DRAG_OPEN_IN_EXPLORER);
}
