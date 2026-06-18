using ADB_Explorer.Controls;
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

    /// <summary>
    /// After the crash dialog completes, the original exception is rethrown and must not be handled again.
    /// </summary>
    private static bool s_crashDialogCompleted;

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

            services.AddSingleton<AdbSnackbarService>();
            services.AddHostedService(p => p.GetRequiredService<AdbSnackbarService>());

            services.AddHostedService<DevicePollingService>();

            services.AddHostedService<DiskUsagePollingService>();

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

            services.AddSingleton<OperationsPage>();
            services.AddSingleton<OperationsViewModel>();
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

        // Read to force it to be set to system regional format
        _ = Data.Settings.OriginalCulture;
        // Read to force it to be set to system display language
        _ = Data.Settings.OriginalUICulture;

        // Similar to %LocalAppData%\ADB Explorer (but avoids virtualization for Store versions)
        Data.AppDataPath = Path.Combine(Environment.GetEnvironmentVariable("USERPROFILE"), "AppData", "Local", AdbExplorerConst.APP_DATA_FOLDER);

        string settingsPath = "", oldPath = "";
        if (e.Args.Length > 0)
        {
            FileInfo file = new(e.Args[0]);
            DirectoryInfo parent = file.Directory;

            // Provided path must not be a directory, network location, or symlink
            // Parent directory must exist and not be a symlink (the file itself doesn't have to exist)
            if (e.Args[0].StartsWith(@"\\")
                || !parent.Exists
                || !parent.Attributes.HasFlag(FileAttributes.Directory)
                || parent.Attributes.HasFlag(FileAttributes.ReparsePoint)
                || file.Attributes.HasFlag(FileAttributes.Directory) 
                || file.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                MessageBox.Show($"{Strings.Resources.S_PATH_INVALID}\n\n{e.Args[0]}", Strings.Resources.S_CUSTOM_DATA_PATH, MessageBoxButton.OK, MessageBoxImage.Error);

                Current.Shutdown(1);
                return;
            }

            settingsPath = Path.GetFullPath(e.Args[0]);
        }
        else
        {
            settingsPath = FileHelper.ConcatPaths(Data.AppDataPath, AdbExplorerConst.APP_SETTINGS_FILE, '\\');
            oldPath = FileHelper.ConcatPaths(Data.AppDataPath, "App.txt", '\\');
        }

        var settings = Services.GetRequiredService<SettingsService>();
        settings.Load(settingsPath, oldPath);

        AppCulture.ApplyThreadCultures();

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

        Services.GetService<OperationsViewModel>()?.StoreColumns();

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
        // Stop mouse interception which causes partial mouse freeze during crashes
        NativeMethods.InterceptMouse.Close();

        if (e.Exception is COMException comException && comException.ErrorCode is (int)NativeMethods.HResult.CLIPBRD_E_CANT_OPEN)
            e.Handled = true;

        // If application shutdown has started, do not throw exceptions
        if (IsShuttingDown || App.Current is null || App.Current.Dispatcher is null)
            e.Handled = true;

        if (s_crashDialogCompleted)
        {
            e.Handled = false;
            return;
        }

        if (Data.Settings.ShowMessageOnCrash)
        {
            // Mark handled so WPF does not terminate before the queued dialog can run.
            e.Handled = true;
            ShowCrashDialog(e.Exception);
        }
    }

    private static void ShowCrashDialog(Exception exception)
    {
        SafeBeginInvoke(async () =>
        {
            if (IsShuttingDown)
                return;

            try
            {
                var message = CrashDialog.FormatMessage(exception.Message);
                var contentDialog = AdbContentDialog.StringDialog(message, DialogService.DialogIcon.Critical, copyToClipboard: true);

                Wpf.Ui.Controls.ContentDialogResult result;
                try
                {
                    result = await DialogService.ShowDialog(
                        contentDialog,
                        CrashDialog.Title,
                        primaryText: CrashReportService.IsConfigured ? CrashDialog.Send : "",
                        closeText: CrashDialog.Dismiss);
                }
                catch
                {
                    MessageBox.Show(message, CrashDialog.Title, MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (result is Wpf.Ui.Controls.ContentDialogResult.Primary)
                    await ShowSendCrashReportDialogAsync(exception);
            }
            finally
            {
                RethrowAfterCrashDialog(exception);
            }
        });
    }

    private static async Task ShowSendCrashReportDialogAsync(Exception exception)
    {
        var progress = new CrashReportSendProgress();
        var content = AdbContentDialog.CustomContentDialog(progress, DialogService.DialogIcon.Informational);

        var dialog = new Wpf.Ui.Controls.ContentDialog
        {
            Title = CrashDialog.SendingTitle,
            Content = content,
            IsFooterVisible = false,
            FlowDirection = Data.RuntimeSettings.IsRTL ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
        };

        var showTask = App.Services
            .GetRequiredService<IContentDialogService>()
            .ShowAsync(dialog, CancellationToken.None);

        progress.Start(CrashDialog.Sending);
        var sendResult = await CrashReportService.SendAsync(exception);

        var sentMessage = CrashReportService.UsesLocalCollector
            ? CrashDialog.SentDebug
            : CrashDialog.SentRelease;
        var failedMessage = CrashReportService.UsesLocalCollector
            ? CrashDialog.SendFailedDebug
            : CrashDialog.SendFailedRelease;
#if DEBUG
        if (!sendResult.Success && !string.IsNullOrWhiteSpace(sendResult.Error))
            failedMessage += $"\n\n{sendResult.Error}";
#endif

        progress.Complete(sendResult.Success ? sentMessage : failedMessage);
        content.SetDialogIcon(sendResult.Success
            ? DialogService.DialogIcon.Informational
            : DialogService.DialogIcon.Exclamation);
        dialog.Title = CrashDialog.Title;
        dialog.IsFooterVisible = true;
        dialog.CloseButtonText = CrashDialog.Dismiss;

        await showTask;
    }

    private static void RethrowAfterCrashDialog(Exception exception)
    {
        if (IsShuttingDown)
            return;

        s_crashDialogCompleted = true;

        var dispatcher = AppDispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted)
            return;

        dispatcher.BeginInvoke(() => throw exception, DispatcherPriority.Send);
    }

    // Crash report strings are not translated
    private static class CrashDialog
    {
        public const string Title = "Unhandled Exception";
        public const string Send = "Send Report";
        public const string Dismiss = "Dismiss";
        public const string SendingTitle = "Sending crash report";
        public const string Sending = "Sending crash report...";

        public const string SentDebug =
            "Thank you. Your crash report is being sent.\n\n" +
            "In Grafana Explore → Loki, run:\n" +
            "{service_name=\"ADB Explorer\", kind=\"exception\"}";

        public const string SentRelease =
            """
            Thank you. Your crash report is being sent to Grafana Cloud.
            
            Task failed successfully...
            """;

        public const string SendFailedDebug =
            "Unable to send the crash report. Make sure Grafana Alloy is running on this PC, then try again.";

        public const string SendFailedRelease =
            "Unable to send the crash report. Check your internet connection and try again.";

        public static string FormatMessage(string exceptionMessage)
        {
            var privacyUrl = ADB_Explorer.Resources.Links.ADB_EXPLORER_PRIVACY.ToString();
            var optOutHint = CrashReportService.IsConfigured
                ? "To disable this dialog, turn off \"Show crash report dialog\" in Settings → About."
                : $"To disable this message, set '\"ShowMessageOnCrash\": false' in %LocalAppData%\\AdbExplorer\\settings.json";

            if (!CrashReportService.IsConfigured)
            {
                return $"""
An unhandled exception occurred, and the application has crashed.
{optOutHint}

{exceptionMessage}
""";
            }

            return $"""
An unhandled exception occurred, and the application has crashed.

If you choose Send Report, diagnostic data is sent to Grafana Cloud (Grafana Labs) to help fix bugs. This may include the exception message, stack trace, app and Windows version, and which view was open. Sending is optional — choose Dismiss to send nothing.
Privacy Policy: {privacyUrl}

{optOutHint}

{exceptionMessage}
""";
        }
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
