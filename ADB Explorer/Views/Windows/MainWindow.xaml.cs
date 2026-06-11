using ADB_Explorer.Controls.Pages;
using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;
using ADB_Explorer.Services.AppInfra;
using ADB_Explorer.ViewModels.Pages;
using ADB_Explorer.ViewModels.Windows;
using Wpf.Ui;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace ADB_Explorer.Views.Windows;

public partial class MainWindow : INavigationWindow
{
    private readonly DragWindow dw = new();

    public MainWindowViewModel ViewModel { get; }

    public MainWindow(
        MainWindowViewModel viewModel,
        INavigationViewPageProvider navigationViewPageProvider,
        INavigationService navigationService,
        IContentDialogService contentDialogService,
        ISnackbarService snackbarService)
    {
        ViewModel = viewModel;
        DataContext = this;

        Initialize();

        InitializeComponent();
        SetPageService(navigationViewPageProvider);
        contentDialogService.SetDialogHost(RootContentDialog);
        snackbarService.SetSnackbarPresenter(RootSnackbar);

        navigationService.SetNavigationControl(RootNavigation);

        RootNavigation.Navigated += RootNavigation_Navigated;

        Data.CurrentPage.PropertyChanged += (s, e) =>
        {
            Navigate(e.NewValue);
        };

        Deactivated += (s, e) =>
        {
            Data.RaiseUnfocusSearchBox();
            Data.RaiseFocusNavigationBox(false);
        };

        StateChanged += (s, e) =>
        {
            Data.Settings.WindowMaximized = WindowState is WindowState.Maximized;
        };
    }

    private async void Initialize()
    {
        SystemThemeWatcher.Watch(this);
        AdbThemeService.SetTheme(Data.Settings.Theme);
        AdbThemeService.SetAccent(Data.Settings.UseCustomAccent ? Data.Settings.AccentColor : null);
        Data.DevicesObject = new();

        if (!await AdbHelper.CheckAdbVersion())
        {
            return;
        }

        Data.RuntimeSettings.DefaultBrowserPath = Network.GetDefaultBrowser();
        Data.FileOpQ = new();
        App.Services.GetRequiredService<AdbSnackbarService>().SubscribeQueue(Data.FileOpQ);

        NativeMethods.InterceptClipboard.Init(this,
                                              Data.CopyPaste.GetClipboardPasteItems,
                                              IpcService.AcceptIpcMessage,
                                              scale => Data.RuntimeSettings.MainWindowScalingFactor = scale);

        Data.FileOpQ.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName is (nameof(FileOperationQueue.IsActive)) or (nameof(FileOperationQueue.Progress)))
            {
                ViewModel.UpdateFileOp();
            }
        };

        DeviceHelper.UpdateWsaPkgStatus();

        dw.Show();

        App.Current.MainWindow = this;
    }

    private SettingsPageHeader SettingsPageHeader
    {
        get
        {
            field ??= new() { DataContext = App.Services.GetService<SettingsViewModel>() };
            return field;
        }
    } = null;

    private DevicesPageHeader DevicesPageHeader
    {
        get
        {
            field ??= new() { DataContext = App.Services.GetService<DevicesViewModel>() };
            return field;
        }
    } = null;

    private ExplorerPageHeader ExplorerPageHeader
    {
        get
        {
            field ??= new(App.Services.GetService<ExplorerViewModel>()); 
            return field;
        }
    } = null;

    private TerminalPageHeader TerminalPageHeader
    {
        get
        {
            field ??= new() { DataContext = App.Services.GetService<TerminalViewModel>() };
            return field;
        }
    } = null;

    private LogPageHeader LogPageHeader
    {
        get
        {
            field ??= new() { DataContext = App.Services.GetService<LogViewModel>() };
            return field;
        }
    } = null;

    private OperationsPageHeader OperationsPageHeader
    {
        get
        {
            field ??= new() { DataContext = App.Services.GetService<OperationsViewModel>() };
            return field;
        }
    } = null;

    private void RootNavigation_Navigated(NavigationView sender, NavigatedEventArgs args)
    {
        PageHeader.Content = args.Page switch
        {
            Pages.SettingsPage => SettingsPageHeader,
            Pages.DevicesPage => DevicesPageHeader,
            Pages.ExplorerPage => ExplorerPageHeader,
            Pages.TerminalPage => TerminalPageHeader,
            Pages.LogPage => LogPageHeader,
            Pages.OperationsPage => OperationsPageHeader,
            _ => null
        };
    }

    #region INavigationWindow methods

    public INavigationView GetNavigation() => RootNavigation;

    public bool Navigate(Type pageType)
    {
        try
        {
            return RootNavigation.Navigate(pageType);
        }
        catch (Exception e)
        {

            return false;
        }
    }

    public void SetPageService(INavigationViewPageProvider navigationViewPageProvider) => RootNavigation.SetPageProviderService(navigationViewPageProvider);

    public void ShowWindow() => Show();

    public void CloseWindow() => Close();

    #endregion INavigationWindow methods

    /// <summary>
    /// Raises the closed event.
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        dw.Close();

        // Make sure that closing this window will begin the process of closing the application.
        Application.Current.Shutdown();
    }

    INavigationView INavigationWindow.GetNavigation()
    {
        throw new NotImplementedException();
    }

    public void SetServiceProvider(IServiceProvider serviceProvider)
    {
        throw new NotImplementedException();
    }

    private void FluentWindow_Loaded(object sender, EventArgs e)
    {
        // the retries are to force the navigation view to show the selection. doesn't work in DEBUG

        for (int i = 0; i < 4; i++)
        {
            Task.Delay(100).ContinueWith(_ =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (ViewModel.IsNavigationEnabled)
                        Navigate(typeof(Pages.DevicesPage));
                    else
                        Navigate(typeof(Pages.SettingsPage));
                });
            });

            if (RootNavigation.SelectedItem is not null)
                break;
        }
    }

    private void RootNavigation_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is MouseButton.XButton1 or MouseButton.XButton2)
        {
            e.Handled = true;
        }
    }
}
