using ADB_Explorer.Controls.Pages;
using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;
using ADB_Explorer.Services.AppInfra;
using ADB_Explorer.ViewModels.Pages;
using ADB_Explorer.ViewModels.Windows;
using ADB_Explorer.Views.Pages;
using Wpf.Ui;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Controls;

namespace ADB_Explorer.Views.Windows;

public partial class MainWindow : INavigationWindow
{
    private const double LaunchScreenWidthScale = 0.52;
    private const double LaunchScreenHeightScale = 0.7;

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
        ApplyLaunchWindowSize();
        AppActions.Bindings.ForEach(binding => InputBindings.Add(binding));
        SetPageService(navigationViewPageProvider);
        contentDialogService.SetDialogHost(RootContentDialog);
        snackbarService.SetSnackbarPresenter(RootSnackbar);

        navigationService.SetNavigationControl(RootNavigation);

        RootNavigation.Navigated += RootNavigation_Navigated;

        // Before the handlers below, so the first tab doesn't try to show a page before the view exists.
        ExplorerTabs.EnsureActiveTab();

        ExplorerTabs.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not nameof(ExplorerTabsViewModel.ActiveTab) || ExplorerTabs.ActiveTab is not { } tab)
                return;

            EnsureActiveHeader();
            TabPageSync.ShowTabPage(tab);

            if (Data.CurrentPage.Value == typeof(ExplorerPage))
                PageHeader.Content = GetOrCreateExplorerHeader(tab);
        };

        ExplorerTabs.Tabs.CollectionChanged += (_, e) =>
        {
            if (e.Action != NotifyCollectionChangedAction.Remove)
                return;

            foreach (ExplorerInstance closed in e.OldItems)
            {
                closed.FileList.DirList?.Stop();
                _explorerHeaders.Remove(closed);
            }
        };

        Data.CurrentPage.PropertyChanged += (s, e) =>
        {
            ViewModel.IsExplorerPage = e.NewValue == typeof(ExplorerPage);

            // The navigation view can't show anything until its template is applied; Loaded navigates then.
            if (RootNavigation.IsLoaded)
                Navigate(e.NewValue);

            TabPageSync.RecordPage(e.NewValue);
        };

        MouseUp += MainWindow_MouseUp;

        RootNavigation.Loaded += (_, _) =>
        {
            HookPaneResize();
            HookPaneHover();
        };
        Data.DevicesObjectCreated += (_, _) => App.SafeInvoke(InitializeNavigationPane);
        InitializeNavigationPane();
        ShowPlaceholderNavBar();

        UpdatePageAreaBorder();
        Data.RuntimeSettings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AppRuntimeSettings.IsHighContrast))
                UpdatePageAreaBorder();
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

    private const double PANE_MIN_WIDTH = 160;

    private void UnfocusNavigationRow()
    {
        if (ExplorerTabs.ActiveTab is { } tab && _explorerHeaders.TryGetValue(tab, out var header))
            header.PathBoxFocus(false);

        Data.RaiseUnfocusSearchBox();
    }

    private bool _navigationPaneInitialized;

    /// <summary>The tree needs the devices list, so it's wired up once that exists rather than at window creation.</summary>
    private void InitializeNavigationPane()
    {
        if (_navigationPaneInitialized || Data.DevicesObject is null)
            return;

        _navigationPaneInitialized = true;

        var explorer = App.Services.GetRequiredService<ExplorerViewModel>();
        explorer.EnsureInitialized();
        EnsureActiveHeader();
        NavigationPane.SetBinding(Controls.NavigationPane.TreeItemsProperty, new Binding("Tree.TreeSource") { Source = explorer });
    }

    /// <summary>
    /// The active tab's header listens for navigation signals, so it has to exist even while a page
    /// covers it - and its navigation row is what the window shows above the pane.
    /// </summary>
    private void EnsureActiveHeader()
    {
        if (Data.DevicesObject is null || ExplorerTabs.ActiveTab is not { } tab)
            return;

        NavBarHost.Content = GetOrCreateExplorerHeader(tab).NavBar;
    }

    /// <summary>Until ADB is valid there's no real header, so a disabled bar keeps the row - and the tab fusing into it - intact.</summary>
    private void ShowPlaceholderNavBar()
    {
        if (Data.DevicesObject is not null || NavBarHost.Content is not null || ExplorerTabs.ActiveTab is not { } tab)
            return;

        NavBarHost.Content = new Controls.ExplorerNavBar(tab) { IsEnabled = false };
    }

    private void HookPaneHover()
    {
        if (RootNavigation.Template.FindName("PART_FooterMenuItemsItemsControl", RootNavigation) is not ItemsControl footer)
            return;

        footer.MouseEnter += (_, _) => ViewModel.IsFooterHovered = true;
        footer.MouseLeave += (_, _) => ViewModel.IsFooterHovered = false;
    }

    private void HookPaneResize()
    {
        if (RootNavigation.Template.FindName("PART_PaneResizeThumb", RootNavigation) is not Thumb thumb)
            return;

        thumb.DragDelta += (_, e) =>
        {
            var delta = Data.RuntimeSettings.IsRTL ? -e.HorizontalChange : e.HorizontalChange;
            var maxWidth = Math.Max(PANE_MIN_WIDTH, ActualWidth / 2);
            var width = Math.Clamp(Data.Settings.NavigationPaneWidth + delta, PANE_MIN_WIDTH, maxWidth);
            Data.Settings.NavigationPaneWidth = (int)width;
        };
    }

    private void ApplyLaunchWindowSize()
    {
        if (Data.Settings.WindowMaximized)
            return;

        Width = Math.Max(SystemParameters.PrimaryScreenWidth * LaunchScreenWidthScale, MinWidth);
        Height = Math.Max(SystemParameters.PrimaryScreenHeight * LaunchScreenHeightScale, MinHeight);
    }

    private bool _adbStateHandlerAttached;

    private async void Initialize()
    {
        AdbThemeService.SetTheme(Data.Settings.Theme, this);
        AdbThemeService.SetAccent(Data.Settings.UseCustomAccent ? Data.Settings.AccentColor : null);

        ADBService.IsMdnsEnabled = Data.Settings.EnableMdns;

        AttachAdbStateHandler();

        if (!await AdbHelper.CheckAdbVersion())
            return;

        TryCompleteAdbDependentInitialization();
    }

    private void AttachAdbStateHandler()
    {
        if (_adbStateHandlerAttached)
            return;

        _adbStateHandlerAttached = true;
        AdbHelper.CurrentAdbState.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AdbState.Status)
                && AdbHelper.CurrentAdbState.Status is AdbHelper.AdbStatus.Valid)
            {
                TryCompleteAdbDependentInitialization();
            }
        };
    }

    internal void TryCompleteAdbDependentInitialization() =>
        App.SafeInvoke(CompleteAdbDependentInitializationCore);

    private void CompleteAdbDependentInitializationCore()
    {
        if (Data.DevicesObject is not null)
            return;

        if (AdbHelper.CurrentAdbState.Status is not AdbHelper.AdbStatus.Valid)
            return;

        Data.DevicesObject = new();
        Data.RaiseDevicesObjectCreated();

        if (Data.Settings.EnableMdns)
            Data.MdnsService.Enable();

        Data.RuntimeSettings.DefaultBrowserPath = Network.GetDefaultBrowser();
        Data.FileOpQ = new();
        App.Services.GetRequiredService<AdbSnackbarService>().SubscribeQueue(Data.FileOpQ);

        NativeMethods.InterceptClipboard.Init(this,
                                              Data.CopyPaste.GetClipboardPasteItems,
                                              IpcService.AcceptIpcMessage,
                                              scale => Data.RuntimeSettings.MainWindowScalingFactor = scale,
                                              UnfocusNavigationRow);

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

    private ExplorerTabsViewModel ExplorerTabs => App.Services.GetService<ExplorerTabsViewModel>();

    /// <summary>The page area's outline would run under the fused active tab, so it's removed -
    /// except in high contrast, where that outline is what separates the areas.</summary>
    private void UpdatePageAreaBorder()
    {
        const string key = "NavigationViewContentGridBorderBrush";

        if (Data.RuntimeSettings.IsHighContrast)
            Resources.Remove(key);
        else
            Resources[key] = Brushes.Transparent;
    }

    private readonly Dictionary<ExplorerInstance, ExplorerPageHeader> _explorerHeaders = [];

    /// <summary>One header UserControl per tab, created lazily and cached for the tab's lifetime.</summary>
    internal ExplorerPageHeader GetOrCreateExplorerHeader(ExplorerInstance instance)
    {
        if (!_explorerHeaders.TryGetValue(instance, out var header))
        {
            header = new(App.Services.GetService<ExplorerViewModel>(), instance);
            _explorerHeaders[instance] = header;
        }

        return header;
    }

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
            Pages.ExplorerPage => GetOrCreateExplorerHeader(ExplorerTabs.EnsureActiveTab()),
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
        catch (Exception)
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

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DiskUsagePollingService.ServerUnresponsive)
            return;

        if (Data.CurrentPage.Value == typeof(ExplorerPage))
            GetOrCreateExplorerHeader(ExplorerTabs.EnsureActiveTab()).HandlePreviewKeyDown(e);
    }

    private void MainWindow_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        // Releasing Alt on its own would otherwise activate keyboard navigation and move focus around the window.
        if (e.Key is Key.System && e.SystemKey is Key.LeftAlt or Key.RightAlt)
        {
            e.Handled = true;
            return;
        }

        if (DiskUsagePollingService.ServerUnresponsive)
            return;

        if (Data.CurrentPage.Value == typeof(ExplorerPage))
            GetOrCreateExplorerHeader(ExplorerTabs.EnsureActiveTab()).HandlePreviewKeyUp(e);
    }

    /// <summary>The mouse back / forward buttons work anywhere in the window - the explorer header handles them itself over its own area.</summary>
    private void MainWindow_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is not (MouseButton.XButton1 or MouseButton.XButton2) || Data.ActiveExplorerInstance.FileList.Actions.ListingInProgress)
            return;

        var direction = e.ChangedButton is MouseButton.XButton1
            ? Navigation.SpecialLocation.Back
            : Navigation.SpecialLocation.Forward;

        e.Handled = NavHistory.NavigateBF(direction);
    }

    private void RootNavigation_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is MouseButton.XButton1 or MouseButton.XButton2)
        {
            e.Handled = true;
        }
    }
}
