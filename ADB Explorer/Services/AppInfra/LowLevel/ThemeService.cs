using Wpf.Ui.Appearance;

namespace ADB_Explorer.Services;

internal class AdbThemeService
{
    public static SystemTheme CurrentTheme { get; private set; } = SystemTheme.Unknown;

    public static void SetTheme(AppSettings.AppTheme theme)
    {
        SystemTheme actualTheme = SystemTheme.Unknown;

        switch (theme)
        {
            case AppSettings.AppTheme.Light:
                ApplicationThemeManager.Apply(ApplicationTheme.Light);
                actualTheme = SystemTheme.Light;
                break;

            case AppSettings.AppTheme.Dark:
                ApplicationThemeManager.Apply(ApplicationTheme.Dark);
                actualTheme = SystemTheme.Dark;
                break;

            case AppSettings.AppTheme.WindowsDefault:
                ApplicationThemeManager.ApplySystemTheme();
                actualTheme = ApplicationThemeManager.GetSystemTheme();
                break;
        }

        CurrentTheme = actualTheme;
        var dictionaries = Application.Current.Resources.MergedDictionaries;

        // Find the current theme dictionary
        var currentTheme = dictionaries.FirstOrDefault(d =>
            d.Source != null &&
            d.Source.OriginalString.StartsWith("/Themes/", StringComparison.OrdinalIgnoreCase));

        if (currentTheme != null)
        {
            dictionaries.Remove(currentTheme);
        }

        var themeResource = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark ? "Dark" : "Light";
        dictionaries.Insert(0, new ResourceDictionary
        {
            Source = new($"/Themes/{themeResource}.xaml", UriKind.Relative)
        });
    }

    public static void SetAccent(Color? color)
    {
        var accentColor = color ?? ApplicationAccentColorManager.GetColorizationColor();
        ApplicationAccentColorManager.Apply(accentColor, ApplicationThemeManager.GetAppTheme());

        // Force-reload the WPF UI theme dictionary so that its SolidColorBrush
        // objects (e.g. AccentButtonBackground) are recreated and resolve their
        // {DynamicResource AccentFillColorDefault} bindings from the just-updated
        // Application.Resources.  Without this, already-rendered controls keep a
        // stale reference to the old brush instance.
        ApplicationThemeManager.Apply(ApplicationThemeManager.GetAppTheme(), updateAccent: false);
    }
}
