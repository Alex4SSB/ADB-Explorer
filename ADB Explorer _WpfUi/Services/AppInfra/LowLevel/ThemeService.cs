using Wpf.Ui.Appearance;

namespace ADB_Explorer.Services;

internal class AdbThemeService
{
    public static void SetTheme(AppSettings.AppTheme theme)
    {
        switch (theme)
        {
            case AppSettings.AppTheme.Light:
                ApplicationThemeManager.Apply(ApplicationTheme.Light);

                break;

            case AppSettings.AppTheme.Dark:
                ApplicationThemeManager.Apply(ApplicationTheme.Dark);

                break;

            case AppSettings.AppTheme.WindowsDefault:
                ApplicationThemeManager.ApplySystemTheme();

                break;
        }
    }
}

//    public static void SetTheme(ApplicationTheme theme) => App.Current.Dispatcher.Invoke(() =>
//    {
//        ThemeManager.Current.ApplicationTheme = theme;

//        Task.Run(() =>
//        {
//            var keys = ((ResourceDictionary)Application.Current.Resources["DynamicBrushes"]).Keys;
//            string[] brushes = new string[keys.Count];
//            keys.CopyTo(brushes, 0);

//            Parallel.ForEach(brushes, (brush) => SetResourceColor(theme, brush));
//        });
//    });

//    public static void SetResourceColor(ApplicationTheme theme, string resource)
//    {
//        App.Current.Dispatcher.Invoke(() => Application.Current.Resources[resource] = new SolidColorBrush((Color)Application.Current.Resources[$"{theme}{resource}"]));
//    }
//}
