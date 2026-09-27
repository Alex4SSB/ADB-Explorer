using static ADB_Explorer.Models.AdbExplorerConst;
using static ADB_Explorer.Models.Data;

namespace ADB_Explorer.Views.Pages;

/// <summary>
/// Interaction logic for ExplorerPageContent.xaml
/// </summary>
public partial class ExplorerPageContent : UserControl
{
    /// <summary>Open toolbar submenu depth (Main / Navigation / sorting / etc.).</summary>
    private int _toolbarSubmenuDepth;

    /// <summary>
    /// Coalesces <see cref="ExplorerPageContent_SizeChanged"/>'s DetailsPane.MaxWidth recalculation -
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

    /// <summary>The tab this content renders - all orchestration below reads/writes its state explicitly.</summary>
    internal ExplorerInstance Instance { get; }

    /// <summary>This tab's navigation row, hosted by the main window above the pane and the page.</summary>
    internal ExplorerNavBar NavBar { get; }

    private NavigationBox NavigationBox => NavBar.NavigationBox;

    private SearchBox SearchBox => NavBar.SearchBox;

    private AdbMenu NavigationToolBar => NavBar.NavigationToolBar;

    private readonly Dictionary<ExplorerInstance, ObservableList<IMenuItem>> _toolbars = [];

    internal DetailsPane DetailsPaneControl => Chrome.DetailsPane;

    /// <summary>The area of this content's list - explorer grid, drive view or icon view - in <paramref name="relativeTo"/>'s space, or null while it isn't shown.</summary>
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
        _secondaryContent?.ExplorerList.CancelExplorerMarquee();

        // Outside click dismisses with the button still down; Escape does not.
        if (Mouse.LeftButton is MouseButtonState.Pressed)
            SuppressSelectionAfterMenu = true;
    }

    /// <summary>
    /// Guards against a stray selection right after navigating (e.g. the second click of a
    /// double-click on a drive tile landing on the newly shown grid at the same screen position).
    /// Restarted on every navigation so back-to-back navigations don't race a stale continuation.
    /// </summary>
    private readonly DispatcherTimer _explorerLoadedTimer = new() { Interval = EXPLORER_NAV_DELAY };

    public ExplorerPageContent(ExplorerViewModel viewModel, ExplorerInstance instance)
    {
        Thread.CurrentThread.CurrentCulture = Settings.ActualFormatCulture;

        Instance = instance;
        _chromeInstance = instance;
        NavBar = new(instance);
        DataContext =
        ViewModel = viewModel;

        SubscribeRequests();
        RuntimeSettings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AppRuntimeSettings.ThumbsSize) && IsActiveInstance)
                App.SafeInvoke(ExplorerList.OnThumbsSizeChanged);
        };

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
        // cached background tab's content must ignore them - only the active tab's should react.
        Data.RunExplorerSearch += (_, _) => App.SafeInvoke(() =>
        {
            if (!IsActiveInstance || _isRestoringSearch)
                return;

            if (Settings.SearchBox is SearchBox.SearchBoxMode.AllSubfolders)
            {
                _searchDebounceTimer.Stop();
                _searchDebounceTimer.Start();
            }
        });
        Data.ExitSearchMode += (_, _) => App.SafeInvoke(() =>
        {
            if (IsActiveInstance)
                ExitSearchMode();
        });

        InstanceHelper.SetInstance(this, Instance);

        InitializeComponent();

        ((BindingProxy)Resources["InstanceProxy"]).Data = Instance;
        ((BindingProxy)Resources["ChromeInstanceProxy"]).Data = Instance;

        UpdateCloseButtons(false);

        PrimaryStatus.Instance = Instance;
        StatusBar.LayoutUpdated += (_, _) => PositionSecondaryStatus();
        PrimaryDeviceInfo.SizeChanged += (_, _) => PositionSecondaryStatus();
        SecondaryDeviceInfo.SizeChanged += (_, _) => PositionSecondaryStatus();

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

        // Built per tab, not bound to a shared static list - see MainToolBar.Build's comment.
        _toolbars[Instance] = ADB_Explorer.ViewModels.MainToolBar.Build(Instance);
        MainToolBar.ItemsSource = _toolbars[Instance];

        Loaded += (_, _) =>
        {
            DragAutoScroll.Register(ExplorerList.ExplorerScrollViewer);
            DragAutoScroll.Register(ExplorerList.IconScrollViewer);

            ActivateContent();
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

        PreviewTextInput += ExplorerPageContent_PreviewTextInput;

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

        ActivateContent();

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
                Chrome._secondaryContent?.RefreshPaneVisuals();
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

    internal void ClearSelection()
    {
        ExplorerList.ActiveUnselectAll();

        if (ExplorerList.DriveListView.SelectedIndex > -1)
            ExplorerList.DriveListView.SelectedIndex = -1;
    }

    public void ShowRenameTooltip(FrameworkElement anchor, object dataContext, bool centerHorizontally = false)
        => RenameTooltipControl.Show(anchor, dataContext, centerHorizontally);

    public void FocusActiveListing() => ExplorerList.ActiveView.Focus();

    private void ExplorerPageContent_SizeChanged(object sender, SizeChangedEventArgs e)
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
