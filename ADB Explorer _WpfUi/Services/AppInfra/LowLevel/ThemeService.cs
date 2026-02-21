using ADB_Explorer.Models;
using Wpf.Ui.Appearance;

namespace ADB_Explorer.Services;

internal class AdbThemeService
{
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

        Data.RuntimeSettings.ActualTheme = actualTheme;

        var dictionaries = Application.Current.Resources.MergedDictionaries;

        // Find the current theme dictionary
        var currentTheme = dictionaries.FirstOrDefault(d =>
            d.Source != null &&
            d.Source.OriginalString.StartsWith("/Themes/", StringComparison.OrdinalIgnoreCase));

        if (currentTheme != null)
        {
            dictionaries.Remove(currentTheme);
        }

        dictionaries.Insert(0, new ResourceDictionary
        {
            Source = new($"/Themes/{actualTheme}.xaml", UriKind.Relative)
        });
    }
}
