using Wpf.Ui;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Controls;

namespace ADB_Explorer.Views.Windows;

public partial class MainWindow : INavigationWindow
{
    private const double LaunchScreenWidthScale = 0.52;
    private const double LaunchScreenHeightScale = 0.7;

    private readonly DragWindow _dw = new();

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
            if (e.PropertyName is nameof(ExplorerTabsViewModel.FocusedPane) && ExplorerTabs.FocusedPane is { } pane)
            {
                OnPaneFocused(pane);
                return;
            }

            if (e.PropertyName is not nameof(ExplorerTabsViewModel.ActiveTab) || ExplorerTabs.ActiveTab is not { } tab)
                return;

            if (Data.CurrentPage.Value == typeof(ExplorerPage))
                ShowExplorerContent(tab);
        };

        ExplorerTabs.SplitChanged += ApplySplit;
        ExplorerTabs.PanesSwapped += SwapSplitPanes;

        TabStrip.TabDragStarted += TabStrip_TabDragStarted;
        TabStrip.TabDragEnded += TabStrip_TabDragEnded;

        // Only one pane of a split view holds a selection: the one being left is cleared.
        ExplorerTabs.PaneFocusChanging += (previous, next) =>
        {
            if (ReferenceEquals(previous.OwningTab, next.OwningTab) && _explorerContents.TryGetValue(previous, out var content))
                content.ClearSelection();
        };

        ExplorerTabs.Tabs.CollectionChanged += (_, e) =>
        {
            if (e.Action != NotifyCollectionChangedAction.Remove)
                return;

            foreach (ExplorerInstance closed in e.OldItems)
            {
                // A tab folded into a split view lives on as a pane.
                if (ExplorerTabs.MergingTabs.Contains(closed))
                    continue;

                closed.FileList.DirList?.Stop();
                _explorerContents.Remove(closed);

                if (closed.SplitInstance is { } split)
                {
                    split.FileList.DirList?.Stop();
                    _explorerContents.Remove(split);
                }
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

    private bool _navigationPaneInitialized;

    /// <summary>The tree needs the devices list, so it's wired up once that exists rather than at window creation.</summary>
    private void InitializeNavigationPane()
    {
        if (_navigationPaneInitialized || Data.DevicesObject is null)
            return;

        _navigationPaneInitialized = true;

        var explorer = App.Services.GetRequiredService<ExplorerViewModel>();
        explorer.EnsureInitialized();
        EnsureActiveContent();
        NavigationPane.SetBinding(Controls.NavigationPane.TreeItemsProperty, new Binding("Tree.TreeSource") { Source = explorer });
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
            // The thumb reports the drag in its own coordinates, which RTL already mirrors.
            var maxWidth = Math.Max(PANE_MIN_WIDTH, ActualWidth / 2);
            var width = Math.Clamp(Data.Settings.NavigationPaneWidth + e.HorizontalChange, PANE_MIN_WIDTH, maxWidth);
            Data.Settings.NavigationPaneWidth = (int)width;
        };
    }

    /// <summary>Set even when launching maximized: it is the size the window restores to, centered on the screen.</summary>
    private void ApplyLaunchWindowSize()
    {
        Width = Math.Max(SystemParameters.PrimaryScreenWidth * LaunchScreenWidthScale, MinWidth);
        Height = Math.Max(SystemParameters.PrimaryScreenHeight * LaunchScreenHeightScale, MinHeight);
    }

    private bool _adbStateHandlerAttached;

    private async void Initialize()
    {
        AdbThemeService.SetTheme(Data.Settings.Theme, this);
        AdbThemeService.SetAccent(Data.Settings.UseCustomAccent ? Data.Settings.AccentColor : null);

        AdbService.IsMdnsEnabled = Data.Settings.EnableMdns;

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

        _dw.Show();

        App.Current.MainWindow = this;
    }

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

    /// <summary>
    /// Raises the closed event.
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        _dw.Close();

        // Make sure that closing this window will begin the process of closing the application.
        Application.Current.Shutdown();
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

        if (Data.CurrentPage.Value != typeof(ExplorerPage))
            return;

        // Handled here because focused controls such as the file DataGrid consume Tab before window key bindings run.
        if (e.Key is Key.Tab && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            ExplorerTabs.CycleTab(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1);
            e.Handled = true;
            return;
        }

        GetOrCreateExplorerContent(ExplorerTabs.EnsureFocusedPane()).HandlePreviewKeyDown(e);
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
            GetOrCreateExplorerContent(ExplorerTabs.EnsureFocusedPane()).HandlePreviewKeyUp(e);
    }

    /// <summary>The mouse back / forward buttons work anywhere in the window - the explorer content handles them itself over its own area.</summary>
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

    /// <summary>A middle click on a page's item opens that page in a new tab.</summary>
    private void RootNavigation_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is not MouseButton.Middle
            || Data.DevicesObject is null
            || HitTestHelper.FindAncestor<NavigationViewItem>(e.OriginalSource as DependencyObject) is not { TargetPageType: { } pageType })
            return;

        e.Handled = true;
        ExplorerTabs.OpenPageInNewTab(pageType);
    }
}
