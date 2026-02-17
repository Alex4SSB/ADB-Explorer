using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;
using ADB_Explorer.ViewModels.Pages;
using ADB_Explorer.ViewModels.Windows;
using ADB_Explorer.Views.Pages;
using ADB_Explorer.Views.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Wpf.Ui;
using Wpf.Ui.DependencyInjection;

namespace ADB_Explorer;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App
{
    // The.NET Generic Host provides dependency injection, configuration, logging, and other services.
    // https://docs.microsoft.com/dotnet/core/extensions/generic-host
    // https://docs.microsoft.com/dotnet/core/extensions/dependency-injection
    // https://docs.microsoft.com/dotnet/core/extensions/configuration
    // https://docs.microsoft.com/dotnet/core/extensions/logging
    private static readonly IHost _host = Host
        .CreateDefaultBuilder()
        .ConfigureAppConfiguration(c => { c.SetBasePath(Path.GetDirectoryName(AppContext.BaseDirectory)); })
        .ConfigureServices((context, services) =>
        {
            services.AddNavigationViewPageProvider();

            services.AddHostedService<ApplicationHostService>();

            services.AddHostedService<DevicePollingService>();

            // Theme manipulation
            services.AddSingleton<IThemeService, ThemeService>();

            // TaskBar manipulation
            services.AddSingleton<ITaskBarService, TaskBarService>();

            // Service containing navigation, same as INavigationWindow... but without window
            services.AddSingleton<INavigationService, NavigationService>();

            services.AddSingleton<IContentDialogService, ContentDialogService>();

            // Main window with navigation
            services.AddSingleton<INavigationWindow, MainWindow>();
            services.AddSingleton<MainWindowViewModel>();

            services.AddSingleton<ExplorerPage>();
            services.AddSingleton<ExplorerViewModel>();
            services.AddSingleton<DevicesPage>();
            services.AddSingleton<DevicesViewModel>();

            services.AddSingleton<SettingsService>();
            services.AddSingleton<SettingsPage>();
            services.AddSingleton<SettingsViewModel>();
        }).Build();

    //private static string SettingsFilePath;
    //private static readonly JsonSerializerSettings JsonSettings = new() { TypeNameHandling = TypeNameHandling.Objects };

    /// <summary>
    /// Gets services.
    /// </summary>
    public static IServiceProvider Services
    {
        get { return _host.Services; }
    }

    /// <summary>
    /// Occurs when the application is loading.
    /// </summary>
    private async void OnStartup(object sender, StartupEventArgs e)
    {
        // Read to force it to be set to Windows' culture
        _ = Data.Settings.OriginalCulture;

        // Similar to %LocalAppData%\ADB Explorer (but avoids virtualization for Store versions)
        Data.AppDataPath = Path.Combine(Environment.GetEnvironmentVariable("USERPROFILE"), "AppData", "Local", AdbExplorerConst.APP_DATA_FOLDER);

        var settingsPath = "";
        if (e.Args.Length > 0)
        {
            // verify that the provided path is valid - it should not exist as a directory, but its parent directory should exist
            if (!Directory.Exists(FileHelper.GetParentPath(e.Args[0])) || Directory.Exists(e.Args[0]))
            {
                MessageBox.Show($"{Strings.Resources.S_PATH_INVALID}\n\n{e.Args[0]}", Strings.Resources.S_CUSTOM_DATA_PATH, MessageBoxButton.OK, MessageBoxImage.Error);

                Current.Shutdown(1);
                return;
            }

            settingsPath = Path.GetFullPath(e.Args[0]);
        }
        else
            settingsPath = FileHelper.ConcatPaths(Data.AppDataPath, AdbExplorerConst.APP_SETTINGS_FILE, '\\');

        var settings = Services.GetRequiredService<SettingsService>();
        settings.Load(settingsPath);

        try
        {
            if (!Data.Settings.UICulture.Equals(CultureInfo.InvariantCulture))
            {
                Thread.CurrentThread.CurrentUICulture =
                Thread.CurrentThread.CurrentCulture = Data.Settings.UICulture;
            }

#if !DEPLOY
            if (!File.Exists(ADB_Explorer.Properties.AppGlobal.DragDropLogPath))
            {
                File.WriteAllText(ADB_Explorer.Properties.AppGlobal.DragDropLogPath, "");
            }
#endif

        }
        catch
        {
            // in any case of failing to read the settings, try to write them instead
            // will happen on first ever launch, or after resetting app settings

            //WriteSettings();
        }

        //void ReadSettingsFile(StreamReader reader)
        //{
        //    while (!reader.EndOfStream)
        //    {
        //        string[] keyValue = reader.ReadLine().TrimEnd(';').Split(':', 2);
        //        try
        //        {
        //            var jObj = JsonConvert.DeserializeObject(keyValue[1], JsonSettings);
        //            if (jObj is JArray jArr)
        //                Properties[keyValue[0]] = jArr.Values<string>().ToArray();
        //            else
        //                Properties[keyValue[0]] = jObj;
        //        }
        //        catch (Exception)
        //        {
        //            Properties[keyValue[0]] = keyValue[1];
        //        }
        //    }
        //}

        ClearDrag();

        await _host.StartAsync();
    }

    /// <summary>
    /// Occurs when the application is closing.
    /// </summary>
    private async void OnExit(object sender, ExitEventArgs e)
    {
        Data.FileOpQ?.Stop();
        //WriteSettings();
        Services.GetService<SettingsService>().Save();

        if (Data.Settings.UnrootOnDisconnect is true)
            ADBService.Unroot(Data.CurrentADBDevice);

        App.Current.Dispatcher.Invoke(ClearDrag);

        await _host.StopAsync();

        _host.Dispose();
    }

    private static void ClearDrag()
    {
        try
        {
            Directory.GetDirectories(Data.AppDataPath).ForEach(dir => Directory.Delete(dir, true));
        }
        catch
        { }
    }

    //private void WriteSettings()
    //{
    //    if (Data.RuntimeSettings.ResetAppSettings)
    //    {
    //        try
    //        {
    //            File.Delete(SettingsFilePath);
    //        }
    //        catch
    //        { }

    //        return;
    //    }

    //    try
    //    {
    //        using StreamWriter writer = new(SettingsFilePath);

    //        foreach (string key in from string key in Properties.Keys
    //                               orderby key
    //                               select key)
    //        {
    //            writer.WriteLine($"{key}:{JsonConvert.SerializeObject(Properties[key], JsonSettings)};");
    //        }
    //    }
    //    catch (Exception)
    //    { }
    //}

    /// <summary>
    /// Occurs when an exception is thrown by an application but not handled.
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Handle error 0x800401D0 (CLIPBRD_E_CANT_OPEN) - global WPF issue
        if (e.Exception is COMException comException && comException.ErrorCode == -2147221040)
            e.Handled = true;

        // If application shutdown has started, do not throw exceptions
        if (App.Current is null || App.Current.Dispatcher is null)
            e.Handled = true;
    }
}
