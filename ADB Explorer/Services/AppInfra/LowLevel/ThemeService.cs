using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Services;

internal class ThemeService : ViewModelBase
{
    //https://medium.com/southworks/handling-dark-light-modes-in-wpf-3f89c8a4f2db

    private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string RegistryValueName = "AppsUseLightTheme";
    private const string QueryPrefix = "SELECT * FROM RegistryValueChangeEvent WHERE Hive = 'HKEY_USERS' AND KeyPath";

    private AppSettings.AppTheme? windowsTheme;
    public AppSettings.AppTheme WindowsTheme
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

    private static AppSettings.AppTheme GetWindowsTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
        return key?.GetValue(RegistryValueName) is < 1 ? AppSettings.AppTheme.dark : AppSettings.AppTheme.light;
    }

    public ApplicationTheme AppThemeToActual(AppSettings.AppTheme appTheme) => appTheme switch
    {
        AppSettings.AppTheme.light => ApplicationTheme.Light,
        AppSettings.AppTheme.dark => ApplicationTheme.Dark,
        AppSettings.AppTheme.windowsDefault => AppThemeToActual(WindowsTheme),
        _ => throw new NotSupportedException(),
    };

    public void SetTheme(AppSettings.AppTheme theme) => SetTheme(AppThemeToActual(theme));

    public static void SetTheme(ApplicationTheme theme) => App.Current.Dispatcher.Invoke(() =>
    {
        ThemeManager.Current.ApplicationTheme = theme;

        Task.Run(() =>
        {
            var keys = ((ResourceDictionary)Application.Current.Resources["DynamicBrushes"]).Keys;
            string[] brushes = new string[keys.Count];
            keys.CopyTo(brushes, 0);

            Parallel.ForEach(brushes, (brush) => SetResourceColor(theme, brush));
        });
    });

    public static void SetResourceColor(ApplicationTheme theme, string resource)
    {
        App.Current.Dispatcher.Invoke(() => Application.Current.Resources[resource] = new SolidColorBrush((Color)Application.Current.Resources[$"{theme}{resource}"]));
    }
}
