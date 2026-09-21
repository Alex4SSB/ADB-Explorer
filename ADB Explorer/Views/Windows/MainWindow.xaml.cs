using ADB_Explorer.Controls.Pages;
using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;
using ADB_Explorer.Services.AppInfra;
using ADB_Explorer.ViewModels.Pages;
using ADB_Explorer.ViewModels.Windows;
using ADB_Explorer.Views.Pages;
using System.Windows.Media.Animation;
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
            if (e.PropertyName is nameof(ExplorerTabsViewModel.FocusedPane) && ExplorerTabs.FocusedPane is { } pane)
            {
                OnPaneFocused(pane);
                return;
            }

            if (e.PropertyName is not nameof(ExplorerTabsViewModel.ActiveTab) || ExplorerTabs.ActiveTab is not { } tab)
                return;

            if (Data.CurrentPage.Value == typeof(ExplorerPage))
                PageHeader.Content = GetOrCreateExplorerHeader(tab);
        };

        ExplorerTabs.SplitChanged += ApplySplit;
        ExplorerTabs.PanesSwapped += SwapSplitPanes;

        TabStrip.TabDragStarted += TabStrip_TabDragStarted;
        TabStrip.TabDragEnded += TabStrip_TabDragEnded;

        // Only one pane of a split view holds a selection: the one being left is cleared.
        ExplorerTabs.PaneFocusChanging += (previous, next) =>
        {
            if (ReferenceEquals(previous.OwningTab, next.OwningTab) && _explorerHeaders.TryGetValue(previous, out var header))
                header.ClearSelection();
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
                _explorerHeaders.Remove(closed);

                if (closed.SplitInstance is { } split)
                {
                    split.FileList.DirList?.Stop();
                    _explorerHeaders.Remove(split);
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

    private void UnfocusNavigationRow()
    {
        if (_explorerHeaders.TryGetValue(Data.ActiveExplorerInstance, out var header))
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
        if (Data.DevicesObject is null)
            return;

        NavBarHost.Content = GetOrCreateExplorerHeader(Data.ActiveExplorerInstance).NavBar;
    }

    /// <summary>The focused pane's navigation row replaces the shown one, and the tab's shared toolbar switches to it.</summary>
    private void OnPaneFocused(ExplorerInstance pane)
    {
        // The stray-selection guard is app-wide, so it follows the pane now in use, not the last one navigated.
        Data.RuntimeSettings.IsExplorerLoaded = pane.FileList.Actions.IsExplorerVisible;

        EnsureActiveHeader();
        TabPageSync.ShowTabPage(pane);

        if (Data.DevicesObject is not null)
            GetOrCreateExplorerHeader(pane.OwningTab).SetChromeInstance(pane);

        RefreshPaneVisuals(pane.OwningTab);
    }

    private void RefreshPaneVisuals(ExplorerInstance tab)
    {
        if (Data.DevicesObject is null)
            return;

        GetOrCreateExplorerHeader(tab).RefreshPaneVisuals();

        if (tab.SplitInstance is { } split)
            GetOrCreateExplorerHeader(split).RefreshPaneVisuals();
    }

    private ExplorerInstance? _draggedTab;

    private void TabStrip_TabDragStarted(ExplorerInstance tab)
    {
        _draggedTab = tab;
        // Laid out first, as the list's place is measured against the overlay.
        TabDropOverlay.Visibility = Visibility.Visible;
        TabDropOverlay.UpdateLayout();
        TabDropRegion.Margin = DropRegionMargin();
    }

    /// <summary>In the explorer, the list itself - not its toolbar, status bar or details pane; on a page, what is beside the navigation and details panes.</summary>
    private Thickness DropRegionMargin()
    {
        if (Data.CurrentPage.Value == typeof(ExplorerPage)
            && ExplorerTabs.ActiveTab is { } tab
            && _explorerHeaders.TryGetValue(tab, out var header)
            && header.ListBounds(TabDropOverlay) is { } list)
        {
            return new Thickness(
                Math.Max(0, list.Left),
                Math.Max(0, list.Top),
                Math.Max(0, TabDropOverlay.ActualWidth - list.Right),
                Math.Max(0, TabDropOverlay.ActualHeight - list.Bottom));
        }

        return new Thickness(Data.Settings.NavigationPaneWidth + 8, 0, OpenDetailsPaneWidth(), 0);
    }

    /// <summary>The details pane is not part of the area a tab can be dropped on.</summary>
    private double OpenDetailsPaneWidth()
    {
        if (Data.CurrentPage.Value != typeof(ExplorerPage)
            || !Data.Settings.IsDetailsPaneOpen
            || ExplorerTabs.ActiveTab is not { } tab
            || !_explorerHeaders.TryGetValue(tab, out var header))
            return 0;

        return header.DetailsPaneControl.IsVisible ? header.DetailsPaneControl.ActualWidth : 0;
    }

    private void TabStrip_TabDragEnded()
    {
        _draggedTab = null;
        TabDropOverlay.Visibility = Visibility.Collapsed;
        HideTabDropHints();
    }

    /// <summary>The zone last hinted, kept while the pointer is in the region's center.</summary>
    private SplitSide? _lastDropSide;

    /// <summary>Fraction of the drop region's width and height, centered, where the zone doesn't follow the pointer.</summary>
    private const double DropDeadZoneFraction = 0.1;

    private void HideTabDropHints()
    {
        _lastDropSide = null;
        _hintedSide = null;

        DropHint.Visibility = Visibility.Collapsed;
        ClearDropHintAnimations();
    }

    private static readonly DependencyProperty[] DropHintProperties =
    [
        Canvas.LeftProperty,
        Canvas.TopProperty,
        FrameworkElement.WidthProperty,
        FrameworkElement.HeightProperty,
    ];

    private void ClearDropHintAnimations()
    {
        foreach (var property in DropHintProperties)
            DropHint.BeginAnimation(property, null);
    }

    private static readonly TimeSpan DropHintSlideDuration = TimeSpan.FromMilliseconds(180);

    private const double DropHintInset = 6;

    /// <summary>The zone the hint currently marks, null while it is hidden.</summary>
    private SplitSide? _hintedSide;

    /// <summary>Shows the hint over the half of the page area <paramref name="side"/> names; it slides there from wherever it was.</summary>
    private void ShowDropHint(SplitSide side)
    {
        if (_hintedSide == side)
            return;

        var width = TabDropRegion.ActualWidth;
        var height = TabDropRegion.ActualHeight;

        var zone = side switch
        {
            SplitSide.Left => new Rect(0, 0, width / 2, height),
            SplitSide.Right => new Rect(width / 2, 0, width / 2, height),
            SplitSide.Top => new Rect(0, 0, width, height / 2),
            _ => new Rect(0, height / 2, width, height / 2),
        };

        zone.Inflate(-DropHintInset, -DropHintInset);
        double[] targets = [zone.X, zone.Y, zone.Width, zone.Height];

        // Appearing, it is placed straight away; already shown, it eases from its current place.
        var slide = DropHint.Visibility is Visibility.Visible;
        DropHint.Visibility = Visibility.Visible;
        _hintedSide = side;

        for (var i = 0; i < targets.Length; i++)
        {
            if (!slide)
            {
                DropHint.BeginAnimation(DropHintProperties[i], null);
                DropHint.SetValue(DropHintProperties[i], targets[i]);
                continue;
            }

            DropHint.BeginAnimation(DropHintProperties[i], new DoubleAnimation(targets[i], DropHintSlideDuration)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
        }
    }

    /// <summary>Whether the dragged tab can be dropped here, and against which edge of the page area - the one the pointer is nearest to.</summary>
    private bool TryGetTabDropSide(DragEventArgs e, out SplitSide side)
    {
        side = SplitSide.Right;

        if (_draggedTab is not { } dragged
            || ExplorerTabs.ActiveTab is not { } target
            || !ExplorerTabs.CanMerge(target, dragged))
            return false;

        var width = TabDropRegion.ActualWidth;
        var height = TabDropRegion.ActualHeight;
        var point = e.GetPosition(TabDropRegion);
        if (point.X < 0 || point.X > width || point.Y < 0 || point.Y > height || width <= 0 || height <= 0)
            return false;

        var inCenter = Math.Abs(point.X / width - 0.5) <= DropDeadZoneFraction / 2
            && Math.Abs(point.Y / height - 0.5) <= DropDeadZoneFraction / 2;

        if (inCenter)
        {
            // With no zone hinted yet, the center offers no drop.
            if (_lastDropSide is not { } previous)
                return false;

            side = previous;
            return true;
        }

        // Distances are relative to the region's size, so the four zones are the triangles cut by its diagonals.
        var distances = new (SplitSide Side, double Distance)[]
        {
            (SplitSide.Left, point.X / width),
            (SplitSide.Right, 1 - point.X / width),
            (SplitSide.Top, point.Y / height),
            (SplitSide.Bottom, 1 - point.Y / height),
        };

        side = distances.MinBy(d => d.Distance).Side;
        _lastDropSide = side;

        return true;
    }

    private void TabDropOverlay_DragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;

        if (!TryGetTabDropSide(e, out var side))
        {
            e.Effects = DragDropEffects.None;
            Data.CopyPaste.CurrentDropEffect = DragDropEffects.None;
            HideTabDropHints();
            return;
        }

        e.Effects = DragDropEffects.Move;

        if (side.IsStacked())
            Data.CopyPaste.DragTabTooltip = Strings.Resources.S_SPLIT_VERTICALLY;
        else
            Data.CopyPaste.DragTabTooltip = Strings.Resources.S_SPLIT_HORIZONTALLY;

        Data.CopyPaste.CurrentDropEffect = DragDropEffects.Move;
        ShowDropHint(side);
    }

    private void TabDropOverlay_DragLeave(object sender, DragEventArgs e)
    {
        Data.CopyPaste.CurrentDropEffect = DragDropEffects.None;
        HideTabDropHints();
    }

    private void TabDropOverlay_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;

        // The zone is read first: hiding the hints forgets the last one, which the center relies on.
        var canDrop = TryGetTabDropSide(e, out var side);
        HideTabDropHints();

        if (!canDrop || _draggedTab is not { } dragged || ExplorerTabs.ActiveTab is not { } target)
            return;

        // Dropped onto its own tab, it splits that tab instead.
        if (ReferenceEquals(target, dragged))
            ExplorerTabs.OpenSplit(target, side);
        else
            ExplorerTabs.MergeTab(target, dragged, side);
    }

    /// <summary>The split view's second pane becomes the tab's own header, and the old one is shown beside it instead.</summary>
    private void SwapSplitPanes(ExplorerInstance oldPrimary, ExplorerInstance newPrimary)
    {
        var oldHeader = GetOrCreateExplorerHeader(oldPrimary);
        var newHeader = GetOrCreateExplorerHeader(newPrimary);
        var (primarySize, secondarySize) = oldHeader.PaneSizes;
        var stacked = oldHeader.IsStacked;

        // A header can only sit in one place, so each is freed before it is put in the other's.
        oldHeader.HideSecondary();
        PageHeader.Content = newHeader;
        newHeader.ClearPaneOnly();
        newHeader.ShowSecondary(oldHeader, stacked);
        newHeader.SetPaneSizes(secondarySize, primarySize);

        newHeader.SetChromeInstance(Data.ActiveExplorerInstance);
        RefreshPaneVisuals(newPrimary);
    }

    /// <summary>Puts a tab's split view's second pane beside its own, or takes it away again.</summary>
    private void ApplySplit(ExplorerInstance tab, ExplorerInstance? removed)
    {
        var header = GetOrCreateExplorerHeader(tab);

        if (tab.SplitInstance is { } split)
        {
            header.ShowSecondary(GetOrCreateExplorerHeader(split), tab.IsSplitStacked);
            RefreshPaneVisuals(tab);
            TabPageSync.ShowTabPage(Data.ActiveExplorerInstance);
            return;
        }

        header.HideSecondary();
        header.RefreshPaneVisuals();

        if (removed is not null)
            _explorerHeaders.Remove(removed);

        // Out of a split view, a tab showing a page is shown by the window again.
        TabPageSync.ShowTabPage(Data.ActiveExplorerInstance);
        ShowCurrentPage();
    }

    /// <summary>
    /// Shows the page the active tab is on, and highlights its item. A split view leaves the navigation
    /// view sitting on a page its pane showed, where navigating to it again does nothing, so the window follows by hand.
    /// </summary>
    private void ShowCurrentPage()
    {
        if (!RootNavigation.IsLoaded || Data.CurrentPage.Value is not { } page)
            return;

        if (!Navigate(page))
            RootNavigation.ReplaceContent(page);

        PageHeader.Content = HeaderForPage(page);
    }

    private object? HeaderForPage(Type page)
    {
        if (page == typeof(Pages.ExplorerPage))
            return GetOrCreateExplorerHeader(ExplorerTabs.EnsureActiveTab());

        if (page == typeof(Pages.SettingsPage))
            return SettingsPageHeader;

        if (page == typeof(Pages.DevicesPage))
            return DevicesPageHeader;

        if (page == typeof(Pages.TerminalPage))
            return TerminalPageHeader;

        if (page == typeof(Pages.LogPage))
            return LogPageHeader;

        if (page == typeof(Pages.OperationsPage))
            return OperationsPageHeader;

        return null;
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

        if (Data.CurrentPage.Value != typeof(ExplorerPage))
            return;

        // Handled here because focused controls such as the file DataGrid consume Tab before window key bindings run.
        if (e.Key is Key.Tab && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            ExplorerTabs.CycleTab(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1);
            e.Handled = true;
            return;
        }

        GetOrCreateExplorerHeader(ExplorerTabs.EnsureFocusedPane()).HandlePreviewKeyDown(e);
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
            GetOrCreateExplorerHeader(ExplorerTabs.EnsureFocusedPane()).HandlePreviewKeyUp(e);
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
