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
    /// <summary>
    /// Cached dispatcher reference that remains valid after <see cref="Application.Current"/> becomes null during shutdown.
    /// </summary>
    public static Dispatcher AppDispatcher { get; private set; }

    /// <summary>
    /// Indicates whether the application has begun shutting down. Check this before dispatching work
    /// that should not execute during shutdown.
    /// </summary>
    public static bool IsShuttingDown { get; private set; }

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

            services.AddHostedService<ThumbnailSnackbarService>();

            services.AddHostedService<DevicePollingService>();

            // Theme manipulation
            services.AddSingleton<IThemeService, ThemeService>();

            // TaskBar manipulation
            services.AddSingleton<ITaskBarService, TaskBarService>();

            // Service containing navigation, same as INavigationWindow... but without window
            services.AddSingleton<INavigationService, NavigationService>();

            services.AddSingleton<IContentDialogService, ContentDialogService>();

            services.AddSingleton<ISnackbarService, SnackbarService>();

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

            services.AddSingleton<TerminalPage>();
            services.AddSingleton<TerminalViewModel>();

            services.AddSingleton<LogPage>();
            services.AddSingleton<LogViewModel>();
        }).Build();

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
        AppDispatcher = Current.Dispatcher;

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

        ClearFoldersInAppData();

        await _host.StartAsync();
    }

    /// <summary>
    /// Occurs when the application is closing.
    /// </summary>
    private async void OnExit(object sender, ExitEventArgs e)
    {
        IsShuttingDown = true;

        ThumbnailService.SaveAllThumbsToCsv();

        Data.FileOpQ?.Stop();

        Services.GetService<SettingsService>().Save();

        if (Data.Settings.UnrootOnDisconnect is true)
            ADBService.Unroot(Data.DevicesObject.Current.ID);

        ClearFoldersInAppData();

        await _host.StopAsync();

        _host.Dispose();
    }

    private static void ClearFoldersInAppData()
    {
        if (Data.Settings.PersistThumbs)
            return;

        try
        {
            Directory.GetDirectories(Data.AppDataPath).ForEach(dir => Directory.Delete(dir, true));
        }
        catch
        { }
    }

    /// <summary>
    /// Occurs when an exception is thrown by an application but not handled.
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Handle error 0x800401D0 (CLIPBRD_E_CANT_OPEN) - global WPF issue
        if (e.Exception is COMException comException && comException.ErrorCode == -2147221040)
            e.Handled = true;

        // If application shutdown has started, do not throw exceptions
        if (IsShuttingDown || App.Current is null || App.Current.Dispatcher is null)
            e.Handled = true;
    }

    /// <summary>
    /// Safely invokes an action on the UI dispatcher. No-ops if the application is shutting down
    /// or the dispatcher is unavailable.
    /// </summary>
    public static void SafeInvoke(Action action)
    {
        var dispatcher = AppDispatcher;
        if (dispatcher is null || IsShuttingDown || dispatcher.HasShutdownStarted)
            return;

        if (dispatcher.CheckAccess())
            action();
        else
            dispatcher.Invoke(action);
    }

    /// <summary>
    /// Safely begins an asynchronous invoke on the UI dispatcher. No-ops if the application is
    /// shutting down or the dispatcher is unavailable.
    /// </summary>
    public static void SafeBeginInvoke(Action action, DispatcherPriority priority = DispatcherPriority.Normal)
    {
        var dispatcher = AppDispatcher;
        if (dispatcher is null || IsShuttingDown || dispatcher.HasShutdownStarted)
            return;

        dispatcher.BeginInvoke(action, priority);
    }
}
