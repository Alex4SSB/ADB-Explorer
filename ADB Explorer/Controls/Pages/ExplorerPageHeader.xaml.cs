using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;
using ADB_Explorer.Services.AppInfra;
using ADB_Explorer.ViewModels;
using ADB_Explorer.ViewModels.Pages;
using static ADB_Explorer.Helpers.VisibilityHelper;
using static ADB_Explorer.Models.AbstractFile;
using static ADB_Explorer.Models.AdbExplorerConst;
using static ADB_Explorer.Models.Data;

namespace ADB_Explorer.Controls.Pages;

/// <summary>
/// Interaction logic for ExplorerPageHeader.xaml
/// </summary>
public partial class ExplorerPageHeader : UserControl
{

    /// <summary>
    /// Back / Forward Navigation
    /// </summary>
    internal bool BfNavigation { get; set; }

    /// <summary>Open toolbar submenu depth (Main / Navigation / sorting / etc.).</summary>
    private int _toolbarSubmenuDepth;

    /// <summary>
    /// Coalesces <see cref="ExplorerHeader_SizeChanged"/>'s DetailsPane.MaxWidth recalculation -
    /// see that handler's own comment for why this is deferred rather than applied inline.
    /// </summary>
    private int _detailsPaneMaxWidthGeneration;

    internal int ToolbarSubmenuDepth => Chrome._toolbarSubmenuDepth;

    private bool _suppressSelectionAfterMenu;

    /// <summary>
    /// True after a toolbar submenu closed while the left button was still down -
    /// the dismiss click should not start rubber-band selection (it may still unselect).
    /// </summary>
    internal bool SuppressSelectionAfterMenu
    {
        get => Chrome._suppressSelectionAfterMenu;
        set => Chrome._suppressSelectionAfterMenu = value;
    }

    private ExplorerViewModel ViewModel { get; }

    /// <summary>The tab this header renders - all orchestration below reads/writes its state explicitly.</summary>
    internal ExplorerInstance Instance { get; }

    /// <summary>This tab's navigation row, hosted by the main window above the pane and the page.</summary>
    internal ExplorerNavBar NavBar { get; }

    private NavigationBox NavigationBox => NavBar.NavigationBox;

    private SearchBox SearchBox => NavBar.SearchBox;

    private AdbMenu NavigationToolBar => NavBar.NavigationToolBar;

    /// <summary>The header whose toolbar, details pane and status bar serve this one: itself, or
    /// for a split view's second pane, the tab's own header.</summary>
    private ExplorerPageHeader? _chromeOwner;

    private ExplorerPageHeader Chrome => _chromeOwner ?? this;

    /// <summary>The split view's second pane hosted beside this header's list, if any.</summary>
    private ExplorerPageHeader? _secondaryHeader;

    /// <summary>The pane the shared toolbar, sort and view selectors currently act on.</summary>
    private ExplorerInstance _chromeInstance;

    private readonly Dictionary<ExplorerInstance, ObservableList<IMenuItem>> _toolbars = [];

    private const double MIN_PANE_WIDTH = 200;

    internal DetailsPane DetailsPaneControl => Chrome.DetailsPane;

    /// <summary>The area of this header's list - explorer grid, drive view or icon view - in <paramref name="relativeTo"/>'s space, or null while it isn't shown.</summary>
    internal Rect? ListBounds(UIElement relativeTo)
    {
        if (!ExplorerList.IsVisible || ExplorerList.ActualWidth <= 0 || ExplorerList.ActualHeight <= 0)
            return null;

        var topLeft = ExplorerList.TranslatePoint(new Point(0, 0), relativeTo);
        var bottomRight = ExplorerList.TranslatePoint(new Point(ExplorerList.ActualWidth, ExplorerList.ActualHeight), relativeTo);

        return new Rect(topLeft, bottomRight);
    }

    private void HookToolbarMenu(AdbMenu? menu)
    {
        if (menu is null)
            return;

        menu.AddHandler(MenuItem.SubmenuOpenedEvent, new RoutedEventHandler(OnToolbarSubmenuOpened), true);
        menu.AddHandler(MenuItem.SubmenuClosedEvent, new RoutedEventHandler(OnToolbarSubmenuClosed), true);
    }

    private void OnToolbarSubmenuOpened(object sender, RoutedEventArgs e) => _toolbarSubmenuDepth++;

    private void OnToolbarSubmenuClosed(object sender, RoutedEventArgs e)
    {
        _toolbarSubmenuDepth = Math.Max(0, _toolbarSubmenuDepth - 1);
        if (_toolbarSubmenuDepth != 0)
            return;

        ExplorerList.CancelExplorerMarquee();
        _secondaryHeader?.ExplorerList.CancelExplorerMarquee();

        // Outside click dismisses with the button still down; Escape does not.
        if (Mouse.LeftButton is MouseButtonState.Pressed)
            SuppressSelectionAfterMenu = true;
    }

    private readonly DispatcherTimer _searchDebounceTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };

    /// <summary>
    /// Guards against a stray selection right after navigating (e.g. the second click of a
    /// double-click on a drive tile landing on the newly shown grid at the same screen position).
    /// Restarted on every navigation so back-to-back navigations don't race a stale continuation.
    /// </summary>
    private readonly DispatcherTimer _explorerLoadedTimer = new() { Interval = EXPLORER_NAV_DELAY };

    public ExplorerPageHeader(ExplorerViewModel viewModel, ExplorerInstance instance)
    {
        Thread.CurrentThread.CurrentCulture = Settings.ActualFormatCulture;

        Instance = instance;
        _chromeInstance = instance;
        NavBar = new(instance);
        DataContext =
        ViewModel = viewModel;

        RuntimeSettings.PropertyChanged += RuntimeSettings_PropertyChanged;

        Instance.FileList.Actions.PropertyChanged += (_, e) => App.SafeInvoke(() =>
        {
            if (e.PropertyName is nameof(FileActionsEnable.ExplorerFilter)
                && !string.IsNullOrEmpty(Instance.FileList.Actions.ExplorerFilter))
            {
                ExplorerList.ClearSelectionForSearch();
            }

            if (e.PropertyName is nameof(FileActionsEnable.IsAppDriveThumbsLocked))
                ApplyLocationThumbSize();
        });

        // RunExplorerSearch/ExitSearchMode are app-wide events (one source for every tab), so a
        // cached background header must ignore them - only the active tab's header should react.
        Data.RunExplorerSearch += (_, _) => App.SafeInvoke(() =>
        {
            if (!ReferenceEquals(Instance, Data.ActiveExplorerInstance))
                return;

            if (Settings.SearchBox is SearchBox.SearchBoxMode.AllSubfolders)
            {
                _searchDebounceTimer.Stop();
                _searchDebounceTimer.Start();
            }
        });
        Data.ExitSearchMode += (_, _) => App.SafeInvoke(() =>
        {
            if (ReferenceEquals(Instance, Data.ActiveExplorerInstance))
                ExitSearchMode();
        });

        InstanceHelper.SetInstance(this, Instance);

        InitializeComponent();

        ((BindingProxy)Resources["InstanceProxy"]).Data = Instance;
        ((BindingProxy)Resources["ChromeInstanceProxy"]).Data = Instance;

        UpdateCloseButtons(false);

        PrimaryStatus.Instance = Instance;
        StatusBar.LayoutUpdated += (_, _) => PositionSecondaryStatus();

        ExplorerList.Initialize(this);
        ExplorerList.PreviewMouseDown += (_, _) => FocusOwnPane();
        ExplorerList.GotKeyboardFocus += (_, _) => FocusOwnPane();
        SearchOptionsControl.Initialize(this);

        SyncNavigationBoxWithHistory();

        NavBar.PointerEntered += (_, _) => ExplorerList.ClearMouseDownPointIfIdle();
        NavBar.BackgroundClicked += (_, _) =>
        {
            PathBoxFocus(false);
            RaiseUnfocusSearchBox();
        };

        // Built per header, not bound to a shared static list - see MainToolBar.Build's comment.
        _toolbars[Instance] = ADB_Explorer.Services.MainToolBar.Build(Instance);
        MainToolBar.ItemsSource = _toolbars[Instance];

        Loaded += (_, _) =>
        {
            DragAutoScroll.Register(ExplorerList.ExplorerScrollViewer);
            DragAutoScroll.Register(ExplorerList.IconScrollViewer);

            ActivateHeader();
        };
        Unloaded += (_, _) =>
        {
            DragAutoScroll.Unregister(ExplorerList.ExplorerScrollViewer);
            DragAutoScroll.Unregister(ExplorerList.IconScrollViewer);
        };

        HookToolbarMenu(MainToolBar);
        HookToolbarMenu(NavigationToolBar);
        HookToolbarMenu(StyleHelper.FindDescendant<AdbMenu>(SortingSelector));
        HookToolbarMenu(StyleHelper.FindDescendant<AdbMenu>(ThumbsSizeSelector));
        HookToolbarMenu(StyleHelper.FindDescendant<AdbMenu>(SearchOptionsControl));
        HookToolbarMenu(StyleHelper.FindDescendant<AdbMenu>(DetailsControl));

        PreviewTextInput += ExplorerPageHeader_PreviewTextInput;

        _searchDebounceTimer.Tick += (_, _) =>
        {
            _searchDebounceTimer.Stop();
            RunExplorerSearch();
        };
        _explorerLoadedTimer.Tick += (_, _) =>
        {
            _explorerLoadedTimer.Stop();
            RuntimeSettings.IsExplorerLoaded = true;
        };

        ActivateHeader();

        ItemToSelect.PropertyChanged += (s, e) =>
        {
            if (!ReferenceEquals(Instance, Data.ActiveExplorerInstance))
                return;

            ExplorerList.ActiveView.SelectedItem = ItemToSelect.Value;
            if (ItemToSelect is not null)
                ExplorerList.ActiveScrollIntoView(ItemToSelect.Value);
        };

        Instance.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ExplorerInstance.IsShowingPage))
            {
                SyncNavigationBoxWithHistory();
                SyncPagePane();
                Chrome.RefreshChromeEnabled();
                Chrome.RefreshDetailsAvailability();
                Chrome.RefreshPaneVisuals();
                Chrome._secondaryHeader?.RefreshPaneVisuals();
            }

            if (e.PropertyName is nameof(ExplorerInstance.IsIconView)
                or nameof(ExplorerInstance.IsContentView)
                or nameof(ExplorerInstance.ExplorerItemsSource)
                or nameof(ExplorerInstance.ExplorerSource))
            {
                ExplorerList.ScheduleApkIconPriorityUpdate();
            }
        };
    }

    /// <summary>Points the shared ViewModel's refresh hook at this header (the last one built or
    /// shown wins) and re-derives the details pane, which reads app-wide state that changes per tab.</summary>
    private void ActivateHeader()
    {
        var chrome = Chrome;

        ViewModel.RequestModeRefresh = () =>
        {
            chrome.DetailsPane.RequestModeRefresh?.Invoke();
            chrome.DetailsControl.RequestModeRefresh?.Invoke();
        };

        if (chrome.DetailsPane.IsOpen && ReferenceEquals(chrome._chromeInstance, Data.ActiveExplorerInstance))
            chrome.DetailsPane.RefreshSelection();
    }

    internal void ClearSelection()
    {
        ExplorerList.ActiveUnselectAll();

        if (ExplorerList.DriveListView.SelectedIndex > -1)
            ExplorerList.DriveListView.SelectedIndex = -1;
    }

    /// <summary>In a split view the focused pane is tinted and the other shadowed at its edges; a single pane shows neither.</summary>
    internal void RefreshPaneVisuals()
    {
        // With a page in either pane there is no pair of explorers to tell apart.
        var isSplit = Instance.OwningTab.SplitInstance is { } split
            && !Instance.OwningTab.IsShowingPage
            && !split.IsShowingPage
            && !RuntimeSettings.IsHighContrast;
        var isActive = ReferenceEquals(Instance, Data.ActiveExplorerInstance);

        ActivePaneTint.Visibility = isSplit && isActive ? Visibility.Visible : Visibility.Collapsed;
        InactivePaneShadow.Visibility = isSplit && !isActive ? Visibility.Visible : Visibility.Collapsed;
    }

    private Type? _pagePaneType;

    private readonly Dictionary<Type, UserControl> _pageHeaders = [];

    /// <summary>In a split view a pane showing an app page displays it in place of its list; on its own, the window shows the page.</summary>
    internal void SyncPagePane()
    {
        Type? pageType = null;

        if (Instance.IsShowingPage && Instance.OwningTab.SplitInstance is not null)
            pageType = Instance.History.Current?.PageType;

        if (pageType == _pagePaneType)
            return;

        _pagePaneType = pageType;

        UserControl? pageHeader = null;
        if (pageType is not null && !_pageHeaders.TryGetValue(pageType, out pageHeader))
        {
            pageHeader = PageHeaderFactory.Create(pageType);

            if (pageHeader is not null)
                _pageHeaders[pageType] = pageHeader;
        }

        PagePaneHost.Content = pageHeader;
        PagePane.Visibility = pageHeader is null ? Visibility.Collapsed : Visibility.Visible;
        ExplorerList.Visibility = pageHeader is null ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The details pane serves the explorer next to it, so it is gone while that pane shows a page.</summary>
    internal void RefreshDetailsAvailability()
    {
        if (_chromeOwner is not null)
            return;

        var adjacent = _secondaryHeader?.Instance ?? Instance;
        var available = !adjacent.IsShowingPage;

        DetailsHost.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
        DetailsControl.IsEnabled = available;
    }

    /// <summary>The toolbar acts on a pane's listing, so it is off while the focused pane shows a page instead.</summary>
    internal void RefreshChromeEnabled()
        => ToolbarRow.IsEnabled = !(_chromeInstance.IsShowingPage && Instance.OwningTab.SplitInstance is not null);

    private void FocusOwnPane()
    {
        if (!ReferenceEquals(Instance, Data.ActiveExplorerInstance))
            App.Services.GetService<ExplorerTabsViewModel>()?.FocusPane(Instance);

        // Keyboard focus follows, or a closing menu would return it to the other pane and take the focus back there.
        if (!ExplorerList.IsKeyboardFocusWithin && !Instance.IsShowingPage)
            FocusActiveListing();
    }

    /// <summary>Makes this header the second pane of a split view: only its list shows, the tab's own header supplying the rest.</summary>
    internal void SetPaneOnly(ExplorerPageHeader chromeOwner)
    {
        _chromeOwner = chromeOwner;

        ToolbarRow.Visibility = Visibility.Collapsed;
        StatusBar.Visibility = Visibility.Collapsed;
        DetailsHost.Visibility = Visibility.Collapsed;

        // Its toolbar's icons are live elements the owner's toolbar shows for this pane; only one can hold them.
        MainToolBar.ItemsSource = null;

        SyncPagePane();
    }

    /// <summary>Makes a header that was a split view's second pane a whole one again.</summary>
    internal void ClearPaneOnly()
    {
        _chromeOwner = null;

        ToolbarRow.Visibility = Visibility.Visible;
        StatusBar.Visibility = Visibility.Visible;
        MainToolBar.ItemsSource = _toolbars[Instance];

        SyncPagePane();
        RefreshDetailsAvailability();
        RefreshChromeEnabled();
    }

    private const double MIN_PANE_HEIGHT = 120;

    /// <summary>Whether the second pane is shown below this header's list instead of beside it.</summary>
    internal bool IsStacked { get; private set; }

    /// <summary>The primary and second panes' widths - heights when stacked - so a swap can keep the splitter where the user put it.</summary>
    internal (GridLength Primary, GridLength Secondary) PaneSizes
    {
        get
        {
            if (IsStacked)
                return (PrimaryPaneRow.Height, SecondaryPaneRow.Height);

            return (PrimaryPaneColumn.Width, SecondaryPaneColumn.Width);
        }
    }

    internal void SetPaneSizes(GridLength primary, GridLength secondary)
    {
        if (IsStacked)
        {
            PrimaryPaneRow.Height = primary;
            SecondaryPaneRow.Height = secondary;
        }
        else
        {
            PrimaryPaneColumn.Width = primary;
            SecondaryPaneColumn.Width = secondary;
        }
    }

    /// <summary>Side by side, the first pane is on the left except in right-to-left layouts; stacked, the icons turn to point up and down.</summary>
    private void UpdateCloseButtons(bool stacked)
    {
        if (stacked)
        {
            ClosePrimaryPaneButton.ToolTip = Strings.Resources.S_CLOSE_TOP_PANE;
            CloseSecondaryPaneButton.ToolTip = Strings.Resources.S_CLOSE_BOTTOM_PANE;

            return;
        }

        var isRtl = RuntimeSettings.IsRTL;
        ClosePrimaryPaneButton.ToolTip = isRtl ? Strings.Resources.S_CLOSE_RIGHT_PANE : Strings.Resources.S_CLOSE_LEFT_PANE;
        CloseSecondaryPaneButton.ToolTip = isRtl ? Strings.Resources.S_CLOSE_LEFT_PANE : Strings.Resources.S_CLOSE_RIGHT_PANE;
    }

    private static void PlaceInGrid(UIElement element, int row, int rowSpan, int column, int columnSpan)
    {
        Grid.SetRow(element, row);
        Grid.SetRowSpan(element, rowSpan);
        Grid.SetColumn(element, column);
        Grid.SetColumnSpan(element, columnSpan);
    }

    /// <summary>Lays the two panes out side by side, or one above the other, along with their splitter and buttons.</summary>
    private void ApplyPaneLayout(bool stacked)
    {
        IsStacked = stacked;

        foreach (var layer in new UIElement[] { ExplorerList, PagePane, ActivePaneTint, InactivePaneShadow })
        {
            if (stacked)
                PlaceInGrid(layer, 0, 1, 0, 3);
            else
                PlaceInGrid(layer, 0, 3, 0, 1);
        }

        if (stacked)
        {
            PlaceInGrid(SecondaryPaneHost, 2, 1, 0, 3);
            PlaceInGrid(PaneSplitter, 1, 1, 0, 3);
            PlaceInGrid(SwapPanesHost, 1, 1, 0, 3);
        }
        else
        {
            PlaceInGrid(SecondaryPaneHost, 0, 3, 2, 1);
            PlaceInGrid(PaneSplitter, 0, 3, 1, 1);
            PlaceInGrid(SwapPanesHost, 0, 3, 1, 1);
        }

        PaneSplitter.Width = stacked ? double.NaN : 20;
        PaneSplitter.Cursor = stacked ? Cursors.SizeNS : Cursors.SizeWE;
        PaneSplitter.ResizeDirection = stacked ? GridResizeDirection.Rows : GridResizeDirection.Columns;

        // The buttons sit in the middle of the splitter, along it.
        SwapPanesHost.Width = stacked ? double.NaN : 20;
        SwapPanesHost.HorizontalAlignment = stacked ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        SwapPanesButtons.Orientation = stacked ? Orientation.Horizontal : Orientation.Vertical;

        SwapPanesButton.Width = stacked ? 24 : double.NaN;
        SwitchOrientationButton.Width = stacked ? 24 : double.NaN;
        SwapPanesButtons.Height = stacked ? HANDLE_STRIP_HEIGHT : double.NaN;

        ArrangeCloseHandles(stacked);

        // The arrows point along the panes' direction.
        SwapPanesIcon.LayoutTransform = stacked ? new RotateTransform(90) : Transform.Identity;

        // The switch shows the layout it changes to.
        SwitchOrientationIcon.Data = stacked ? FluentPathGeometries.LayoutColumnTwo : FluentPathGeometries.LayoutRowTwo;
        SwitchOrientationButton.ToolTip = stacked ? Strings.Resources.S_SPLIT_HORIZONTALLY : Strings.Resources.S_SPLIT_VERTICALLY;

        UpdateCloseButtons(stacked);
    }

    private const double HANDLE_DEPTH = 16;
    private const double HANDLE_LENGTH = 24;
    private const double HANDLE_FLARE = 6;

    /// <summary>The stacked splitter's strip: room for a handle on each of its edges, with a gap between.</summary>
    private const double HANDLE_STRIP_HEIGHT = 28;

    /// <summary>Puts each close button on its own pane's side of the splitter - as a handle growing out of that pane, with the buttons between them.</summary>
    private void ArrangeCloseHandles(bool stacked)
    {
        var gap = HANDLE_FLARE + 2;

        ClosePrimaryFlare.AttachedEdge = stacked ? Dock.Top : Dock.Left;
        CloseSecondaryFlare.AttachedEdge = stacked ? Dock.Bottom : Dock.Right;

        foreach (var handle in new[] { ClosePrimaryHandle, CloseSecondaryHandle })
        {
            handle.Width = stacked ? HANDLE_LENGTH : HANDLE_DEPTH;
            handle.Height = stacked ? HANDLE_DEPTH : HANDLE_LENGTH;
        }

        foreach (var button in new[] { ClosePrimaryPaneButton, CloseSecondaryPaneButton })
        {
            button.Width = stacked ? HANDLE_LENGTH : HANDLE_DEPTH;
            button.Height = stacked ? HANDLE_DEPTH : HANDLE_LENGTH;
        }

        if (stacked)
        {
            ClosePrimaryHandle.VerticalAlignment = VerticalAlignment.Top;
            CloseSecondaryHandle.VerticalAlignment = VerticalAlignment.Bottom;
            ClosePrimaryHandle.HorizontalAlignment = HorizontalAlignment.Left;
            CloseSecondaryHandle.HorizontalAlignment = HorizontalAlignment.Left;
        }
        else
        {
            ClosePrimaryHandle.VerticalAlignment = VerticalAlignment.Top;
            CloseSecondaryHandle.VerticalAlignment = VerticalAlignment.Top;
            ClosePrimaryHandle.HorizontalAlignment = HorizontalAlignment.Left;
            CloseSecondaryHandle.HorizontalAlignment = HorizontalAlignment.Right;
        }

        SwapPanesButton.VerticalAlignment = VerticalAlignment.Center;
        SwitchOrientationButton.VerticalAlignment = VerticalAlignment.Center;
    }

    /// <summary>Each pane's status sits with it: side by side, the second one's starts where its pane does; stacked, the first pane's is in the splitter's strip.</summary>
    private void UpdateStatusArrangement()
    {
        var split = _secondaryHeader is not null;
        var splitStacked = split && IsStacked;

        PrimaryStatus.Instance = Instance;
        PrimaryStatus.Visibility = splitStacked ? Visibility.Collapsed : Visibility.Visible;
        PrimaryStatus.MaxWidth = double.PositiveInfinity;

        SplitterStatus.Instance = splitStacked ? Instance : null;
        SplitterStatus.Visibility = splitStacked ? Visibility.Visible : Visibility.Collapsed;

        SecondaryStatus.Instance = _secondaryHeader?.Instance;
        SecondaryStatus.Visibility = split ? Visibility.Visible : Visibility.Collapsed;
        SecondaryStatus.Margin = new Thickness(0);

        PositionSecondaryStatus();
    }

    /// <summary>Side by side, keeps the second pane's status under that pane, wherever the splitter has put it.</summary>
    private void PositionSecondaryStatus()
    {
        if (_secondaryHeader is null || IsStacked || !SecondaryPaneHost.IsVisible)
            return;

        var left = Math.Max(0, SecondaryPaneHost.TranslatePoint(new Point(0, 0), StatusBar).X);

        if (Math.Abs(SecondaryStatus.Margin.Left - left) > 0.5)
            SecondaryStatus.Margin = new Thickness(left, 0, 0, 0);

        PrimaryStatus.MaxWidth = left;
    }

    /// <summary>The panes' and splitter's rows and columns, sized to share the space equally.</summary>
    private void SetSharedPaneSizes(bool stacked)
    {
        PrimaryPaneColumn.MinWidth = stacked ? 0 : MIN_PANE_WIDTH;
        SecondaryPaneColumn.MinWidth = stacked ? 0 : MIN_PANE_WIDTH;
        PrimaryPaneColumn.Width = new(1, GridUnitType.Star);
        SecondaryPaneColumn.Width = stacked ? new(0) : new(1, GridUnitType.Star);

        PrimaryPaneRow.MinHeight = stacked ? MIN_PANE_HEIGHT : 0;
        SecondaryPaneRow.MinHeight = stacked ? MIN_PANE_HEIGHT : 0;
        PrimaryPaneRow.Height = new(1, GridUnitType.Star);
        SplitterRow.Height = stacked ? GridLength.Auto : new(0);
        SecondaryPaneRow.Height = stacked ? new(1, GridUnitType.Star) : new(0);
    }

    private void ClosePrimaryPane_Click(object sender, RoutedEventArgs e)
        => App.Services.GetService<ExplorerTabsViewModel>()?.ClosePane(Instance.OwningTab, closeFirst: true);

    private void CloseSecondaryPane_Click(object sender, RoutedEventArgs e)
        => App.Services.GetService<ExplorerTabsViewModel>()?.ClosePane(Instance.OwningTab, closeFirst: false);

    private void SwapPanes_Click(object sender, RoutedEventArgs e)
        => App.Services.GetService<ExplorerTabsViewModel>()?.SwapPanes(Instance.OwningTab);

    /// <summary>Shows <paramref name="secondary"/> beside or below this header's list, with a splitter between them.</summary>
    internal void ShowSecondary(ExplorerPageHeader secondary, bool stacked)
    {
        _secondaryHeader = secondary;
        secondary.SetPaneOnly(this);
        SecondaryPaneHost.Content = secondary;
        ApplyPaneLayout(stacked);
        SyncPagePane();

        SetSharedPaneSizes(stacked);
        PaneSplitter.Visibility = Visibility.Visible;
        SwapPanesHost.Visibility = Visibility.Visible;
        UpdateStatusArrangement();

        RefreshDetailsAvailability();
    }

    /// <summary>Resets the splitter to give both panes half of the space each.</summary>
    private void PaneSplitter_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        SetSharedPaneSizes(IsStacked);
        e.Handled = true;
    }

    private void SwitchOrientation_Click(object sender, RoutedEventArgs e)
        => App.Services.GetService<ExplorerTabsViewModel>()?.SwitchSplitOrientation(Instance.OwningTab);

    internal void HideSecondary()
    {
        SecondaryPaneHost.Content = null;
        _secondaryHeader = null;

        PaneSplitter.Visibility = Visibility.Collapsed;
        SwapPanesHost.Visibility = Visibility.Collapsed;
        ApplyPaneLayout(false);
        SyncPagePane();

        SetSharedPaneSizes(false);
        PrimaryPaneColumn.MinWidth = 0;
        SecondaryPaneColumn.MinWidth = 0;
        SecondaryPaneColumn.Width = new(0);
        UpdateStatusArrangement();

        // The details pane was off while the second pane, now gone, showed a page beside it.
        RefreshDetailsAvailability();

        SetChromeInstance(Instance);
    }

    /// <summary>Points the toolbar, sort / view selectors and status bar at <paramref name="target"/>, the focused pane.</summary>
    internal void SetChromeInstance(ExplorerInstance target)
    {
        if (ReferenceEquals(_chromeInstance, target))
            return;

        _chromeInstance = target;

        if (!_toolbars.TryGetValue(target, out var toolbar))
        {
            toolbar = ADB_Explorer.Services.MainToolBar.Build(target);
            _toolbars[target] = toolbar;
        }

        ((BindingProxy)Resources["ChromeInstanceProxy"]).Data = target;
        InstanceHelper.SetInstance(ToolbarRow, target);
        InstanceHelper.SetInstance(StatusBar, target);
        MainToolBar.ItemsSource = toolbar;
        SearchOptionsControl.SetInstance(target);
        RefreshChromeEnabled();

        ActivateHeader();
    }

    private void ExplorerPageHeader_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        // Alt/Ctrl combos arrive with empty or control-char Text, which would match every item.
        if (string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0]))
            return;

        if (SearchBox.IsFocused || SearchBox.IsKeyboardFocusWithin
            || DetailsPaneControl.IsEditorFocused
            || NavigationBox.Mode is NavigationBox.ViewMode.Path
            || Instance.FileList.Actions.IsExplorerEditing)
            return;

        var selected = ExplorerList.ActiveSelectedItems.Count;
        var selectedIndex = ExplorerList.ActiveView.SelectedIndex;
        IBrowserItem? nextItem = null;

        for (int i = 0; i < ExplorerList.ActiveView.Items.Count; i++)
        {
            var item = (IBrowserItem)ExplorerList.ActiveView.Items[i];
            var name = item.ToString();

            if (name.StartsWith(e.Text, StringComparison.OrdinalIgnoreCase))
            {
                if (selected != 1 || selectedIndex < i)
                {
                    ItemToSelect.Value = item;
                    break;
                }
                else
                    nextItem ??= item;
            }
        }

        if (selectedIndex == ExplorerList.ActiveView.SelectedIndex && nextItem is not null)
            ItemToSelect.Value = nextItem;
    }

    private void OnButtonKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)
            || SearchBox.IsKeyboardFocusWithin
            || NavigationBox.IsKeyboardFocusWithin
            || DetailsPaneControl.IsEditorFocused
            || Instance.FileList.Actions.IsExplorerEditing)
            return;

        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down
            && Instance.EffectiveDevice is null)
        {
            e.Handled = true;
            return;
        }

        bool handle = false;

        if (e.Key is Key.A && Keyboard.Modifiers is ModifierKeys.Control)
        {
            ExplorerList.ToggleSelectAll();
            e.Handled = true;

            return;
        }
        
        if (e.Key is Key.Delete && Instance.FileList.Actions.DeleteEnabled)
        {
            FileActionLogic.DeleteFiles();
            e.Handled = true;
            return;
        }

        if (!NAVIGATION_KEYS.Contains(e.Key))
            return;

        if (Instance.FileList.Actions.IsExplorerVisible)
        {
            handle |= ExplorerList.ExplorerGridKeyNavigation(e.Key);
        }
        else if (Instance.FileList.Actions.IsDriveViewVisible)
        {
            handle |= ExplorerList.DriveViewKeyNavigation(e.Key);
        }

        e.Handled = handle;
    }

    private void RuntimeSettings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // RuntimeSettings' navigation/action signals are app-wide (set by whatever the user just
        // clicked on their current tab) - a cached background header must not act on them too.
        if (!ReferenceEquals(Instance, Data.ActiveExplorerInstance))
            return;

        App.SafeInvoke(() =>
        {
            switch (e.PropertyName)
            {
                case nameof(AppRuntimeSettings.BrowseDrive) when RuntimeSettings.BrowseDrive:
                    InitNavigation(RuntimeSettings.BrowseDrive!.Path);
                    break;

                case nameof(AppRuntimeSettings.DriveViewNav):
                    if (!DiskUsagePollingService.ServerUnresponsive)
                        DriveViewNav();
                    break;

                case nameof(AppRuntimeSettings.InitLister):
                    InitLister();
                    break;

                case nameof(AppRuntimeSettings.PathBoxNavigation):
                    if (RuntimeSettings.PathBoxNavigation == "-")
                    {
                        BfNavigation = true;
                        NavigateHistoryEntry(Instance.History.GoBack());
                    }
                    else
                    {
                        if (Instance.FileList.Actions.IsExplorerVisible)
                            NavigateToLocation(new(RuntimeSettings.PathBoxNavigation));
                        else
                        {
                            if (!InitNavigation(RuntimeSettings.PathBoxNavigation))
                            {
                                DriveViewNav();
                            }
                        }
                    }
                    break;

                case nameof(AppRuntimeSettings.LocationToNavigate):
                    if (RuntimeSettings.LocationToNavigate is null)
                        return;

                    var requested = RuntimeSettings.LocationToNavigate;

                    // Pages have no listing to stop, and the explorer under them stays as it is.
                    if (requested.IsPage)
                    {
                        BfNavigation = false;
                        NavigateToLocation(requested);
                        break;
                    }

                    switch (requested.Location)
                    {
                        case Navigation.SpecialLocation.Back:
                            BfNavigation = true;
                            NavigateHistoryEntry(Instance.History.GoBack());
                            break;
                        case Navigation.SpecialLocation.Forward:
                            BfNavigation = true;
                            NavigateHistoryEntry(Instance.History.GoForward());
                            break;
                        case Navigation.SpecialLocation.Up:
                            StopListing();
                            BfNavigation = false;
                            if (Instance.FileList.Actions.IsSearchMode)
                                ExitSearchMode();
                            else
                                NavigateToPath(ParentPath);
                            break;
                        default:
                            BfNavigation = false;
                            if (!IsRevealingHiddenExplorer(requested))
                                StopListing();

                            if (Instance.FileList.Actions.IsDriveViewVisible && requested.Location is Navigation.SpecialLocation.DriveView && !Instance.IsShowingPage)
                                FileActionLogic.RefreshDrives(true, DeviceCts.Token, Instance.EffectiveDevice);
                            else
                                NavigateToLocation(requested);
                            break;
                    }
                    break;

                case nameof(AppRuntimeSettings.FilterActions):
                    Task.Run(() =>
                    {
                        if (Instance.FileList.Actions.IsAppDrive || Instance.FileList.Actions.IsRecycleBin || Instance.EffectiveDevice is null)
                            FilterFileActions();
                    });
                    Task.Run(() => ExplorerContextMenu.UpdateSeparators());
                    break;

                case nameof(AppRuntimeSettings.NewFolder):
                    NewItem(true);
                    break;

                case nameof(AppRuntimeSettings.NewFile):
                    NewItem(false);
                    break;

                case nameof(AppRuntimeSettings.CompressToExtension):
                    NewCompressItem();
                    break;

                case nameof(AppRuntimeSettings.PasteClipboardImage):
                    NewImagePasteItem();
                    break;

                case nameof(AppRuntimeSettings.Rename):
                    if (Instance.FileList.Actions.RenameEnabled)
                        ExplorerList.IsInEditMode ^= true;
                    break;

                case nameof(AppRuntimeSettings.SelectAll):
                    ExplorerList.ToggleSelectAll();
                    break;

                case nameof(AppRuntimeSettings.ThumbsSize):
                    ExplorerList.OnThumbsSizeChanged();
                    break;

                case nameof(AppRuntimeSettings.IsSearchBoxFocused):
                    // The RuntimeSettings flag is only a toggle signal (its shortcut just flips it) -
                    // this tab's own expanded state is what actually drives the UI.
                    Instance.IsSearchExpanded ^= true;
                    if (Instance.IsSearchExpanded)
                        ExplorerList.ClearSelectionForSearch();
                    break;

                default:
                    break;
            }
        });
    }

    internal void PathBoxFocus(bool isFocused)
    {
        if (isFocused)
            _focusPathBox();
        else
            _unfocusPathBox();

        void _focusPathBox()
        {
            NavigationBox.Mode = NavigationBox.ViewMode.Path;
        }

        void _unfocusPathBox()
        {
            if (NavigationBox.Mode is NavigationBox.ViewMode.None)
                return;

            NavigationBox.Mode = NavigationBox.ViewMode.Breadcrumbs;

            FocusHelper.ClearFocus(NavigationBox.PathBox);
        }
    }

    private void FilterFileActions() => App.SafeInvoke(() => Chrome.MainToolBar.Items?.Refresh());

    private void NewItem(bool isFolder)
    {
        var fileName = FileHelper.DuplicateFile(Instance.FileList.DirList.FileList, isFolder
            ? Strings.Resources.S_NEW_FOLDER
            : Strings.Resources.S_NEW_ITEM);

        FileClass newItem = new(fileName, FileHelper.ConcatPaths(Instance.FileList.Path, fileName), isFolder ? FileType.Folder : FileType.File, isTemp: true);
        Instance.FileList.DirList.FileList.Insert(0, newItem);

        ExplorerList.ActiveScrollIntoView(newItem);
        ExplorerList.ActiveView.SelectedItem = newItem;

        ExplorerList.IsInEditMode = true;
        if (!ExplorerList.IsInEditMode) // in case the editing element was not acquired
            _ = FileActionLogic.CreateNewItem(newItem);
    }

    private void NewCompressItem()
    {
        var extension = RuntimeSettings.CompressToExtension;
        if (string.IsNullOrEmpty(extension) || !FileActionLogic.IsPendingCompress)
            return;

        var sources = FileActionLogic.GetPendingCompressSourcePaths();
        string baseName;
        if (sources.Count > 0)
        {
            var firstName = FileHelper.GetFullName(sources[0]);
            var firstExt = FileHelper.GetExtension(firstName);
            baseName = string.IsNullOrEmpty(firstExt) ? firstName : firstName[..^firstExt.Length];
        }
        else
            baseName = Strings.Resources.S_NEW_ARCHIVE;

        var fileName = FileHelper.DuplicateFile(Instance.FileList.DirList.FileList, $"{baseName}{extension}");
        FileClass newItem = new(fileName, FileHelper.ConcatPaths(Instance.FileList.Path, fileName), FileType.File, isTemp: true);
        FileActionLogic.SetPendingCompressTemp(newItem);

        Instance.FileList.DirList.FileList.Insert(0, newItem);

        ExplorerList.ActiveView.SelectedItem = newItem;
        ExplorerList.ActiveScrollViewer?.ScrollToTop();
        App.SafeBeginInvoke(() => ExplorerList.ActiveScrollViewer?.ScrollToTop(), DispatcherPriority.Loaded);

        ExplorerList.IsInEditMode = true;
        if (!ExplorerList.IsInEditMode)
            _ = FileActionLogic.CreateNewItem(newItem);
    }

    private void NewImagePasteItem()
    {
        if (!FileActionLogic.IsPendingClipboardImage)
            return;

        var fileName = FileHelper.DuplicateFile(Instance.FileList.DirList.FileList, FileActionLogic.GetClipboardImageFileName());
        FileClass newItem = new(fileName, FileHelper.ConcatPaths(Instance.FileList.Path, fileName), FileType.File, isTemp: true);
        FileActionLogic.SetPendingClipboardImageTemp(newItem);

        Instance.FileList.DirList.FileList.Insert(0, newItem);

        ExplorerList.ActiveScrollIntoView(newItem);
        ExplorerList.ActiveView.SelectedItem = newItem;

        if (Instance.IsIconView && FileActionLogic.GetPendingClipboardImageThumbnail() is { } preview)
            newItem.IconViewModel.SetImmediatePreview(preview);

        ExplorerList.IsInEditMode = true;
        if (!ExplorerList.IsInEditMode)
            _ = FileActionLogic.CreateNewItem(newItem);
    }

    private void InitLister()
    {
        var device = Instance.EffectiveDevice;
        Instance.FileList.Device = device;
        Instance.FileList.DirList = new(App.AppDispatcher, device, FileHelper.ListerFileManipulator);
        Instance.FileList.DirList.PropertyChanged += DirectoryLister_PropertyChanged;
    }

    private bool TrySelectBackNavigationItem()
    {
        if (!BfNavigation)
            return false;

        var path = Instance.History.TakePendingSelectionPath();
        if (string.IsNullOrEmpty(path))
            return false;

        if (NavHistory.FindBackNavigationItem(path) is not { } prevItem)
            return false;

        ItemToSelect.Value = prevItem;
        return true;
    }

    private void DirectoryLister_PropertyChanged(object? sender, PropertyChangedEventArgs e) => App.SafeInvoke(() =>
    {
        switch (e.PropertyName)
        {
            case nameof(DirectoryLister.CurrentLocation):
                // Empty selection shows CurrentLocation in the details pane. Refresh on location
                // changes only (preliminary + final). InProgress no longer re-triggers the same load.
                if (DetailsPaneControl.IsOpen && ExplorerList.ActiveSelectedItems.Count == 0)
                    DetailsPaneControl.RefreshSelection();
                break;

            case nameof(DirectoryLister.IsProgressVisible):
                Instance.IsListingUnfinished = Instance.FileList.DirList.IsProgressVisible;
                NavigationBox.IsLoadingProgressVisible = Instance.FileList.DirList.IsProgressVisible;
                break;

            case nameof(DirectoryLister.InProgress):
                {
                    Task.Run(() =>
                    {
                        if (!Instance.FileList.DirList.InProgress)
                            Task.Delay(EMPTY_FOLDER_NOTICE_DELAY);

                        App.SafeInvoke(() =>
                        {
                            Instance.FileList.Actions.ListingInProgress = Instance.FileList.DirList.InProgress;
                            FileActionLogic.UpdateFileActions();
                        });
                    });

                    if (Instance.FileList.DirList.InProgress)
                        return;

                    if (Instance.FileList.Actions.IsRecycleBin)
                        TrashHelper.EnableRecycleButtons();

                    break;
                }
            case nameof(DirectoryLister.IsLinkListingFinished) when !Instance.FileList.DirList.IsLinkListingFinished:
                return;

            case nameof(DirectoryLister.IsLinkListingFinished):
                {
                    ViewModel.NotifyDirectoryLinksResolved();

                    if (Instance.FileList.DirList.FileList.Count > 0)
                    {
                        SortExplorer();

                        if (!TrySelectBackNavigationItem())
                        {
                            ExplorerList.ActiveScrollIntoView(ExplorerList.ActiveView.Items[0]);

                            if (Settings.ThumbsMode is AppSettings.ThumbnailMode.OnPhotoDir
                                && !ThumbnailService.IsInitialized(Instance.EffectiveDevice.SerialNumber)
                                && FileHelper.IsPhotoDir())
                            {
                                Task.Run(() => ThumbnailService.ForceLoad(Instance.EffectiveDevice));
                            }
                        }
                    }

                    break;
                }
        }
    });

    private bool InitNavigation(string path = "")
    {
        if (path is null)
            return true;

        var realPath = FolderHelper.FolderExists(string.IsNullOrEmpty(path) ? DEFAULT_PATH : path, Instance.EffectiveDevice);
        if (realPath is null)
            return false;

        Instance.FileList.Actions.IsDriveViewVisible = false;
        Instance.FileList.Actions.IsExplorerVisible = true;
        Instance.FileList.Actions.HomeEnabled = true;
        RuntimeSettings.BrowseDrive = null;
        RuntimeSettings.SelectedDrive = null;

        // Re-arm the stray-selection guard for this navigation. Without resetting it here,
        // IsExplorerLoaded stays true after the very first navigation and never protects
        // later ones (e.g. the drive tile's second click landing on the newly shown grid).
        RuntimeSettings.IsExplorerLoaded = false;
        _explorerLoadedTimer.Stop();
        _explorerLoadedTimer.Start();

        return _navigateToPath(realPath);
    }

    private bool _navigateToPath(string realPath, FileClass? locationSource = null)
    {
        Instance.FileList.Actions.IsSearchMode = false;
        Data.SearchOriginPath = null;
        Data.SearchOriginCanWrite = false;
        Data.SearchTransferParent = null;

        DeviceCts.Cancel();
        DeviceCts.Dispose();
        DeviceCts = new();
        ApkIconService.CancelPending();

        Instance.FileList.DirList?.Stop();

        ArchivePath.InvalidateCache();

        var deviceId = Instance.EffectiveDevice?.ID;
        var isArchive = ArchivePath.IsArchivePath(realPath, deviceId);
        var devicePath = isArchive ? ArchivePath.GetArchivePath(realPath, deviceId) : realPath;

        Instance.FileList.Actions.ListingInProgress = true;

        Instance.FileList.Actions.WasInAppDrive = Instance.FileList.Actions.IsAppDrive;
        Instance.FileList.Actions.ExplorerFilter = "";
        Instance.History.Navigate(realPath);
        Instance.NotifyTabChanged();

        Instance.FirstSelectedIndex = -1;
        Instance.CurrentSelectedIndex = -1;
        ExplorerList.ActiveUnselectAll();

        if (DetailsPaneControl.IsOpen)
            DetailsPaneControl.SelectedFiles = [];

        ExplorerList.ActiveView.Focus();

        NavigationBox.Mode = NavigationBox.ViewMode.Breadcrumbs;
        SetExplorerBoxPath(realPath == RECYCLE_PATH ? AdbLocation.StringFromLocation(Navigation.SpecialLocation.RecycleBin) : realPath);
        Instance.FileList.CurrentDrive = DriveHelper.GetCurrentDrive(devicePath);
        Instance.FileList.Actions.IsRecycleBin = realPath == RECYCLE_PATH;
        Instance.FileList.Actions.IsAppDrive = realPath == AdbLocation.StringFromLocation(Navigation.SpecialLocation.PackageDrive);
        Instance.FileList.Actions.IsArchive = isArchive;
        Instance.FileList.Actions.IsTemp = realPath == TEMP_PATH;
        Instance.FileList.Actions.ParentEnabled = realPath != FileHelper.GetParentPath(realPath)
            && !Instance.FileList.Actions.IsRecycleBin && !Instance.FileList.Actions.IsAppDrive;

        if (Instance.FileList.DirList is null && Instance.EffectiveDevice is not null)
            InitLister();

        CurrentPath = realPath;

        FileActionLogic.IsPasteEnabled();

        Instance.FileList.Actions.PushPackageEnabled = Settings.EnableApk && Instance.EffectiveDevice is { Type: not DeviceType.Recovery };
        Instance.FileList.Actions.UninstallPackageEnabled = false;

        Instance.FileList.Actions.ContextPushPackagesEnabled =
        Instance.FileList.Actions.IsUninstallVisible.Value = Instance.FileList.Actions.IsAppDrive;
        Instance.FileList.Actions.IsCutPasteDeleteVisible.Value = !Instance.FileList.Actions.IsAppDrive;
        Instance.FileList.Actions.IsPullCopyVisible.Value = !Instance.FileList.Actions.IsRecycleBin;
        Instance.FileList.Actions.IsPasteVisible.Value = !Instance.FileList.Actions.IsAppDrive && !Instance.FileList.Actions.IsRecycleBin;

        Instance.FileList.Actions.CopyPathDescription.Value = Instance.FileList.Actions.IsAppDrive ? Strings.Resources.S_COPY_APK_NAME : Strings.Resources.S_COPY_PATH;

        ApplyLocationThumbSize();

        SortExplorer();

        if (Instance.FileList.Actions.IsRecycleBin)
        {
            TrashHelper.ParseIndexersAsync(DeviceCts.Token).ContinueWith(_ => Instance.FileList.DirList?.Navigate(realPath));

            Instance.FileList.Actions.DeleteDescription.Value = Strings.Resources.S_EMPTY_TRASH;
            Instance.FileList.Actions.RestoreDescription.Value = Strings.Resources.S_RESTORE_ALL;
        }
        else
        {
            if (Instance.FileList.Actions.IsAppDrive)
            {
                FileActionLogic.UpdatePackages(true, DeviceCts.Token, instance: Instance);
                FileActionLogic.UpdateFileActions();
                ExplorerList.ResetExplorerHorizontalScroll();
                return true;
            }

            if (Instance.FileList.DirList is null)
                return false;

            Instance.FileList.DirList.Navigate(realPath, locationSource);

            Instance.FileList.Actions.DeleteDescription.Value = Strings.Resources.S_DELETE_ACTION;
        }

        if (Instance.FileList.DirList is not null)
            Instance.ExplorerSource = Instance.FileList.DirList.FileList;

        FileActionLogic.UpdateFileActions();

        ExplorerList.ResetExplorerHorizontalScroll();

        return true;
    }

    private void RunExplorerSearch()
    {
        if (Settings.SearchBox is not SearchBox.SearchBoxMode.AllSubfolders
            || !Instance.FileList.Actions.IsExplorerVisible
            || Instance.FileList.Actions.IsAppDrive
            || Instance.FileList.Actions.IsRecycleBin
            || Instance.EffectiveDevice is null
            || Instance.FileList.DirList is null)
        {
            return;
        }

        var query = Instance.FileList.Actions.ExplorerFilter?.Trim();
        if (string.IsNullOrEmpty(query))
        {
            if (Instance.FileList.Actions.IsSearchMode)
                ExitSearchMode();
            return;
        }

        if (!Instance.FileList.Actions.IsSearchMode)
        {
            Data.SearchOriginPath = Instance.FileList.Path;
            var deviceId = Instance.EffectiveDevice?.ID;
            Data.SearchOriginCanWrite = Instance.FileList.DirList?.CurrentLocation is { FullPath: var locationPath, CanWriteLocation: true } location
                && locationPath == Instance.FileList.Path
                || deviceId is not null && DriveHelper.IsModificationAllowedAt(Instance.FileList.Path, deviceId);
        }

        var searchRoot = Data.SearchOriginPath;
        if (string.IsNullOrEmpty(searchRoot)
            || AdbLocation.LocationFromString(searchRoot) is not Navigation.SpecialLocation.None)
        {
            return;
        }

        DeviceCts.Cancel();
        DeviceCts.Dispose();
        DeviceCts = new();
        ApkIconService.CancelPending();

        Instance.FileList.DirList.Stop();
        DisposeFileIcons();

        Instance.FileList.Actions.ListingInProgress = true;
        Instance.FileList.Actions.IsSearchMode = true;
        Instance.FileList.Actions.IsDriveViewVisible = false;
        Instance.FileList.Actions.IsExplorerVisible = true;
        Instance.FileList.Actions.HomeEnabled = true;
        Instance.FileList.Actions.IsRecycleBin = false;
        Instance.FileList.Actions.IsAppDrive = false;
        Instance.FileList.Actions.IsArchive = false;
        Instance.FileList.Actions.IsTemp = false;
        Instance.FileList.Actions.ParentEnabled = false;

        var searchPath = AdbLocation.StringFromLocation(Navigation.SpecialLocation.SearchMode);
        CurrentPath = searchPath;
        SetExplorerBoxPath(searchPath);
        NavigationBox.Mode = NavigationBox.ViewMode.Breadcrumbs;

        Instance.FirstSelectedIndex = -1;
        Instance.CurrentSelectedIndex = -1;
        ExplorerList.ActiveUnselectAll();

        if (DetailsPaneControl.IsOpen)
            DetailsPaneControl.SelectedFiles = [];

        ApplyLocationThumbSize();

        SortExplorer();
        Instance.FileList.DirList.Search(searchRoot, query, DeviceCts.Token);
        Instance.ExplorerSource = Instance.FileList.DirList.FileList;
        FileActionLogic.UpdateFileActions();
        ExplorerList.ResetExplorerHorizontalScroll();

        if (DetailsPaneControl.IsOpen)
            DetailsPaneControl.RefreshSelection();
    }

    private void ExitSearchMode()
    {
        if (!Instance.FileList.Actions.IsSearchMode)
            return;

        var origin = Data.SearchOriginPath;
        Instance.FileList.Actions.IsSearchMode = false;
        Data.SearchOriginPath = null;
        Data.SearchOriginCanWrite = false;
        Data.SearchTransferParent = null;
        Instance.FileList.Actions.ExplorerFilter = "";

        if (!string.IsNullOrEmpty(origin))
            NavigateToPath(origin);
    }

    private void SortExplorer()
    {
        if (Settings.SortingPerLocation && Settings.LocationSorting.TryGetValue(Instance.FileList.Path, out var sort))
        {
            if (Instance.FileList.Actions.IsAppDrive
                && sort.Property is SortingSelector.SortingProperty.Date or SortingSelector.SortingProperty.Size)
            {
                ViewModel.SetSort(SortingSelector.SortingProperty.Name, sort.Direction);
            }
            else if (!Instance.FileList.Actions.IsAppDrive
                && sort.Property is SortingSelector.SortingProperty.UserId or SortingSelector.SortingProperty.Version)
            {
                ViewModel.SetSort(SortingSelector.SortingProperty.Name, sort.Direction);
            }
            else
            {
                ViewModel.SetSort(sort);
            }
        }
        else
        {
            ViewModel.SetSort(SortingSelector.SortingProperty.Name, ListSortDirection.Ascending);
        }
    }

    private void ApplyLocationThumbSize()
    {
        if (Instance.FileList.Actions.IsDriveViewVisible)
        {
            Instance.CurrentThumbsSize = ThumbnailService.ThumbnailSize.Tiles;
            return;
        }

        if (Instance.FileList.Actions.IsSearchMode)
        {
            Instance.CurrentThumbsSize = ThumbnailService.ThumbnailSize.Content;
            return;
        }

        if (string.IsNullOrEmpty(Instance.FileList.Path))
            return;

        if (Instance.FileList.Actions.IsAppDriveThumbsLocked)
        {
            Instance.CurrentThumbsSize = ThumbnailService.ThumbnailSize.Disabled;
            return;
        }

        if (Settings.ThumbSizePerLocation)
        {
            ThumbnailService.ThumbnailSize size = ThumbnailService.ThumbnailSize.Disabled;
            Settings.LocationThumbSize.TryGetValue(Instance.FileList.Path, out size);
            Instance.CurrentThumbsSize = size;
        }
        else
        {
            Instance.CurrentThumbsSize = RuntimeSettings.ThumbsSize;
        }

        if (Instance.FileList.Actions.IsAppDrive
            && Data.Packages is { Count: > 0 }
            && ApkIconService.IsEnabled)
            ApkIconService.BeginPreloadPackages(Data.Packages);
    }

    internal void InvalidateFileIcons()
    {
        if (Instance.FileList.DirList?.FileList is not { } files)
            return;

        foreach (var file in files)
            file.InvalidateIconViewModelThumbnail();
    }

    internal void DisposeFileIcons()
    {
        if (Instance.FileList.DirList?.FileList is not { } files)
            return;

        foreach (var file in files)
        {
            file.DisposeIconViewModel();
            file.CancelFolderSizeCalculation();
        }

        Task.Run(static () =>
        {
            GC.Collect(2, GCCollectionMode.Aggressive, true, true);
            GC.WaitForPendingFinalizers();
        });
    }

    private string? _explorerBoxPath;

    /// <summary>The box shows the explorer's location, unless a page is on top of it - that one is put back afterwards.</summary>
    private void SetExplorerBoxPath(string path)
    {
        _explorerBoxPath = path;
        NavigationBox.Path = path;
    }

    /// <summary>The box shows the current page's name, or the explorer's location when the tab isn't on a page.</summary>
    private void SyncNavigationBoxWithHistory()
    {
        var target = Instance.History.Current is { IsPage: true } page
            ? page.StringFromLocation()
            : _explorerBoxPath;

        if (target is null || NavigationBox.Path == target)
            return;

        NavigationBox.Mode = NavigationBox.ViewMode.Breadcrumbs;
        NavigationBox.Path = target;
    }

    private void StopListing()
    {
        Instance.FileList.DirList?.Stop();
        DisposeFileIcons();
    }

    /// <summary>True when the tab is showing a page and this is the location its explorer already sits at underneath.</summary>
    private bool IsRevealingHiddenExplorer(AdbLocation location)
        => Instance.IsShowingPage
        && !location.IsPage
        && Instance.History.Stamp(location).Equals(Instance.History.LastExplorerLocation);

    /// <summary>Back / forward landed on an entry - a page, a location on another device, or a plain folder.</summary>
    private void NavigateHistoryEntry(AdbLocation? entry)
    {
        if (entry is null)
            return;

        if (!entry.IsPage && !IsRevealingHiddenExplorer(entry))
            StopListing();

        NavigateToLocation(entry);
    }

    private void SwitchToEntryDevice(AdbLocation location)
    {
        var device = Data.DevicesObject?.LogicalDeviceViewModels?
            .FirstOrDefault(d => d.ID == location.DeviceId && d.Status is DeviceStatus.Ok);

        if (device is null)
            return;

        RuntimeSettings.PendingLocationAfterDeviceOpen = location;
        DeviceHelper.NavigateTabToDevice(Instance, device);
    }

    internal void NavigateToLocation(AdbLocation? location)
    {
        if (location is null)
            return;

        Instance.IsMenuOpen = false;

        if (location.IsPage)
        {
            PathBoxFocus(false);

            Instance.History.Navigate(location);
            Instance.NotifyTabChanged();
            return;
        }

        if (location.DeviceId is { } deviceId && deviceId != Instance.EffectiveDevice?.ID)
        {
            SwitchToEntryDevice(location);
            return;
        }

        if (IsRevealingHiddenExplorer(location))
        {
            Instance.History.Navigate(location);
            Instance.NotifyTabChanged();
            return;
        }

        if (location.Location is Navigation.SpecialLocation.DriveView)
        {
            if (DiskUsagePollingService.ServerUnresponsive || Instance.EffectiveDevice is null)
                return;

            Instance.FileList.Actions.IsRecycleBin = false;
            PathBoxFocus(false);
            RaiseUnfocusSearchBox();
            FileActionLogic.RefreshDrives(true, DeviceCts.Token, Instance.EffectiveDevice);
            DriveViewNav();

            FileActionLogic.UpdateFileActions();
        }
        else
        {
            if (location.Location is Navigation.SpecialLocation.SearchMode)
            {
                if (!string.IsNullOrEmpty(Instance.FileList.Actions.ExplorerFilter))
                    RunExplorerSearch();
                return;
            }

            var path = string.IsNullOrEmpty(location.Path)
                ? location.StringFromLocation()
                : location.Path;

            if (!Instance.FileList.Actions.IsExplorerVisible)
            {
                if (!InitNavigation(path))
                    DriveViewNav();
            }
            else
                NavigateToPath(path);
        }
    }

    public bool NavigateToPath(FileClass file)
    {
        if (file is null)
            return false;

        if (!Instance.FileList.Actions.IsAppDrive
            && Instance.EffectiveDevice is { } device
            && ArchiveHelper.CanNavigateIntoArchive(file.FullPath, file.FullName, device.ID, Instance.FileList.Actions.IsArchive))
        {
            return _navigateToPath(ArchivePath.Join(file.FullPath, ""), file);
        }

        string realPath = !string.IsNullOrEmpty(file.LinkTarget)
            ? file.LinkTarget
            : file.FullPath;

        return realPath is not null && _navigateToPath(realPath, file);
    }

    public bool NavigateToPath(string path)
    {
        if (path is null)
            return false;

        var realPath = FolderHelper.FolderExists(path, Instance.EffectiveDevice);
        if (realPath is null)
            return false;

        var locationSource = Instance.FileList.DirList?.FileList.FirstOrDefault(f => f.IsDirectory && f.FullPath == realPath);
        return _navigateToPath(realPath, locationSource);
    }

    private void DriveViewNav()
    {
        if (DiskUsagePollingService.ServerUnresponsive || Instance.EffectiveDevice is null)
            return;

        DeviceCts.Cancel();
        DeviceCts.Dispose();
        DeviceCts = new();
        ApkIconService.CancelPending();

        // Clears this pane's listing, not the focused one's, and leaves the focused pane's stray-selection guard alone.
        var wasLoaded = RuntimeSettings.IsExplorerLoaded;
        using (Data.UseInstance(Instance))
            FileActionLogic.ClearExplorer(false);

        if (!ReferenceEquals(Instance, Data.ActiveExplorerInstance))
            RuntimeSettings.IsExplorerLoaded = wasLoaded;

        Instance.FileList.Actions.IsDriveViewVisible = true;

        NavigationBox.Mode = NavigationBox.ViewMode.Breadcrumbs;
        CurrentPath = AdbLocation.StringFromLocation(Navigation.SpecialLocation.DriveView);
        SetExplorerBoxPath(CurrentPath);
        Instance.History.Navigate(Navigation.SpecialLocation.DriveView);
        Instance.NotifyTabChanged();

        // Direct call, not just relying on the IsDriveViewVisible change above to reach
        // ExplorerViewModel reactively - see UpdateDriveView(ExplorerInstance)'s own comment.
        ViewModel.UpdateDriveView(Instance);

        Instance.FileList.CurrentDrive = null;

        if (!BfNavigation)
        {
            ExplorerList.DriveListView.SelectedIndex = -1;
            RuntimeSettings.SelectedDrive = null;
        }

        if (ExplorerList.DriveListView.SelectedIndex > -1)
        {
            SelectionHelper.GetListViewItemContainer(ExplorerList.DriveListView).Focus();

            if (DetailsPaneControl.IsOpen)
                DetailsPaneControl.SelectedFiles = ExplorerList.DriveListView.SelectedItem is DriveViewModel selectedDrive ? [selectedDrive] : [];
        }
        else if (DetailsPaneControl.IsOpen)
        {
            DetailsPaneControl.SelectedFiles = [];
            DetailsPaneControl.RefreshSelection();
        }

        RuntimeSettings.SelectedDrive = ExplorerList.DriveListView.SelectedItem as DriveViewModel;
        FileActionLogic.UpdateFileActions();

        Instance.CurrentThumbsSize = ThumbnailService.ThumbnailSize.Tiles;
    }

    public void ShowRenameTooltip(FrameworkElement anchor, object dataContext, bool centerHorizontally = false)
        => RenameTooltipControl.Show(anchor, dataContext, centerHorizontally);

    public void FocusActiveListing() => ExplorerList.ActiveView.Focus();

    private void GridBackgroundBlock_MouseDown(object sender, MouseButtonEventArgs e)
    {
        PathBoxFocus(false);
        RaiseUnfocusSearchBox();
    }

    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is not MouseButton.Left)
            return;

        // Tunneling: run before UnselectAll so a nested MouseMove cannot start a rubber-band
        // from a stale down-point (item that was selected when the menu opened).
        if (ToolbarSubmenuDepth > 0 || SuppressSelectionAfterMenu)
        {
            SuppressSelectionAfterMenu = true;
            ExplorerList.CancelExplorerMarquee();
        }
    }

    private void Window_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is not MouseButton.Left)
            return;

        ExplorerList.EndExplorerMouseGesture();
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        PathBoxFocus(false);
        RaiseUnfocusSearchBox();
    }

    private void Window_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is MouseButton.Left)
        {
            ExplorerList.EndExplorerMouseGesture();

            // After the cells had their say, which the flag is kept for.
            ExplorerList.ClearWasDraggingIfIdle();
        }

        if (Instance.FileList.Actions.ListingInProgress && e.ChangedButton is MouseButton.XButton1 or MouseButton.XButton2)
        {
            e.Handled = true;
            return;
        }

        e.Handled = e.ChangedButton switch
        {
            MouseButton.XButton1 => NavHistory.NavigateBF(Navigation.SpecialLocation.Back),
            MouseButton.XButton2 => NavHistory.NavigateBF(Navigation.SpecialLocation.Forward),
            _ => false,
        };
    }

    private void MainWin_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (CopyPaste.IsDrag && CopyPaste.DragStatus is not CopyPasteService.DragState.Active && e.Key is Key.Escape)
            CopyPaste.DragBitmap = null;
        else
            OnButtonKeyDown(sender, e);
    }

    public void HandlePreviewKeyDown(KeyEventArgs e) => MainWin_PreviewKeyDown(this, e);

    public void HandlePreviewKeyUp(KeyEventArgs e) => MainWindow_OnPreviewKeyUp(this, e);

    private void MainWindow_OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.System && CopyPaste.IsDrag)
            e.Handled = true;
    }

    private void MainWindow_OnPreviewQueryContinueDrag(object sender, QueryContinueDragEventArgs e)
    {
        if (e.EscapePressed)
            CopyPaste.DragBitmap = null;
    }

    private void ExplorerHeader_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged)
            return;

        var generation = ++_detailsPaneMaxWidthGeneration;
        App.SafeBeginInvoke(() =>
        {
            if (generation != _detailsPaneMaxWidthGeneration)
                return;

            DetailsPane.MaxWidth = ActualWidth - 100;
        }, DispatcherPriority.Loaded);
    }
}
