using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Services;

internal class ThemeService : ViewModelBase
{
    //https://medium.com/southworks/handling-dark-light-modes-in-wpf-3f89c8a4f2db

    private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string RegistryValueName = "AppsUseLightTheme";
    private const string QueryPrefix = "SELECT * FROM RegistryValueChangeEvent WHERE Hive = 'HKEY_USERS' AND KeyPath";

    private ApplicationTheme? windowsTheme = null;
    public ApplicationTheme WindowsTheme
    {
        get
        {
            if (windowsTheme is null)
                WatchTheme();

            return windowsTheme.Value;
        }
        set => Set(ref windowsTheme, value);
    }

    public void WatchTheme()
    {
        var currentUser = WindowsIdentity.GetCurrent();
        string query = $@"{QueryPrefix} = '{currentUser.User.Value}\\{RegistryKeyPath.Replace(@"\", @"\\")}' AND ValueName = '{RegistryValueName}'";

        // This can fail on Windows 7, but we do not support
        ManagementEventWatcher watcher = new(query);
        watcher.EventArrived += (sender, args) =>
        {
            WindowsTheme = GetWindowsTheme();
        };

        watcher.Start();

        WindowsTheme = GetWindowsTheme();
    }

    private static ApplicationTheme GetWindowsTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
        return (key?.GetValue(RegistryValueName)) is int and < 1 ? ApplicationTheme.Dark : ApplicationTheme.Light;
    }
}
