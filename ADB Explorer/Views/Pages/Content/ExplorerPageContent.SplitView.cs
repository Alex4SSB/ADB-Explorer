using static ADB_Explorer.Models.Data;

namespace ADB_Explorer.Views.Pages;

public partial class ExplorerPageContent
{
    /// <summary>The content whose toolbar, details pane and status bar serve this one: itself, or
    /// for a split view's second pane, the tab's own content.</summary>
    private ExplorerPageContent? _chromeOwner;

    private ExplorerPageContent Chrome => _chromeOwner ?? this;

    /// <summary>The split view's second pane hosted beside this content's list, if any.</summary>
    private ExplorerPageContent? _secondaryContent;

    /// <summary>The pane the shared toolbar, sort and view selectors currently act on.</summary>
    private ExplorerInstance _chromeInstance;

    private const double MIN_PANE_WIDTH = 200;

    /// <summary>Points the shared ViewModel's refresh hook at this content (the last one built or
    /// shown wins) and re-derives the details pane, which reads app-wide state that changes per tab.</summary>
    private void ActivateContent()
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

    private readonly Dictionary<Type, UserControl> _pageContents = [];

    /// <summary>In a split view a pane showing an app page displays it in place of its list; on its own, the window shows the page.</summary>
    internal void SyncPagePane()
    {
        Type? pageType = null;

        if (Instance.IsShowingPage && Instance.OwningTab.SplitInstance is not null)
            pageType = Instance.History.Current?.PageType;

        if (pageType == _pagePaneType)
            return;

        _pagePaneType = pageType;

        UserControl? pageContent = null;
        if (pageType is not null && !_pageContents.TryGetValue(pageType, out pageContent))
        {
            pageContent = PageContentFactory.Create(pageType);

            if (pageContent is not null)
                _pageContents[pageType] = pageContent;
        }

        PagePaneHost.Content = pageContent;
        PagePane.Visibility = pageContent is null ? Visibility.Collapsed : Visibility.Visible;
        ExplorerList.Visibility = pageContent is null ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The details pane serves the explorer next to it, so it is gone while that pane shows a page.</summary>
    internal void RefreshDetailsAvailability()
    {
        if (_chromeOwner is not null)
            return;

        var adjacent = _secondaryContent?.Instance ?? Instance;
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

    /// <summary>Makes this content the second pane of a split view: only its list shows, the tab's own content supplying the rest.</summary>
    internal void SetPaneOnly(ExplorerPageContent chromeOwner)
    {
        _chromeOwner = chromeOwner;

        ToolbarRow.Visibility = Visibility.Collapsed;
        StatusBar.Visibility = Visibility.Collapsed;
        DetailsHost.Visibility = Visibility.Collapsed;

        // Its toolbar's icons are live elements the owner's toolbar shows for this pane; only one can hold them.
        MainToolBar.ItemsSource = null;

        SyncPagePane();
    }

    /// <summary>Makes content that was a split view's second pane a whole one again.</summary>
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

    /// <summary>Whether the second pane is shown below this content's list instead of beside it.</summary>
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
        var split = _secondaryContent is not null;
        var splitStacked = split && IsStacked;

        PrimaryStatus.Instance = Instance;
        PrimaryStatus.Visibility = splitStacked ? Visibility.Collapsed : Visibility.Visible;
        PrimaryStatus.MaxWidth = double.PositiveInfinity;

        SplitterStatus.Instance = splitStacked ? Instance : null;
        SplitterStatus.Visibility = splitStacked ? Visibility.Visible : Visibility.Collapsed;

        SecondaryStatus.Instance = _secondaryContent?.Instance;
        SecondaryStatus.Visibility = split ? Visibility.Visible : Visibility.Collapsed;
        SecondaryStatus.Margin = new Thickness(0);

        PositionSecondaryStatus();
    }

    /// <summary>Whether the tab's two panes are browsing different devices, each of which then gets its own device info in the status bar.</summary>
    private bool IsSplitBetweenDevices()
    {
        if (_secondaryContent?.Instance.EffectiveDevice is not { } other
            || Instance.EffectiveDevice is not { } own)
            return false;

        return own.ID != other.ID;
    }

    private static void PlaceDeviceInfo(PaneDeviceInfo info, ExplorerInstance? pane, double end)
    {
        info.Instance = pane;
        info.Visibility = pane is null ? Visibility.Collapsed : Visibility.Visible;

        var left = Math.Max(0, end - info.ActualWidth);
        if (Math.Abs(info.Margin.Left - left) > 0.5)
            info.Margin = new Thickness(left, 0, 0, 0);
    }

    /// <summary>
    /// Keeps what belongs to each pane in the status bar under it, wherever the splitter has put them: the second pane's
    /// status and - between two devices - each pane's device info at its far side.
    /// </summary>
    private void PositionSecondaryStatus()
    {
        if (_secondaryContent is null || !SecondaryPaneHost.IsVisible)
        {
            SingleDeviceInfo.Visibility = Visibility.Visible;
            PlaceDeviceInfo(PrimaryDeviceInfo, null, 0);
            PlaceDeviceInfo(SecondaryDeviceInfo, null, 0);
            SplitterDeviceInfo.Instance = null;
            SplitterDeviceInfo.Visibility = Visibility.Collapsed;

            return;
        }

        var twoDevices = IsSplitBetweenDevices();
        SingleDeviceInfo.Visibility = twoDevices ? Visibility.Collapsed : Visibility.Visible;

        // Where the explorer ends: at the window's far edge, or at the details pane's splitter when it is open.
        var secondaryEnd = SecondaryPaneHost.TranslatePoint(new Point(SecondaryPaneHost.ActualWidth, 0), StatusBar).X;
        secondaryEnd = Math.Min(secondaryEnd, StatusBar.ActualWidth - RightStatusItems.ActualWidth);

        if (IsStacked)
        {
            PlaceDeviceInfo(PrimaryDeviceInfo, null, 0);

            SplitterDeviceInfo.Instance = twoDevices ? Instance : null;
            SplitterDeviceInfo.Visibility = twoDevices ? Visibility.Visible : Visibility.Collapsed;

            PlaceDeviceInfo(SecondaryDeviceInfo, twoDevices ? _secondaryContent.Instance : null, secondaryEnd);
            SecondaryStatus.MaxWidth = twoDevices
                ? Math.Max(0, secondaryEnd - SecondaryDeviceInfo.ActualWidth)
                : double.PositiveInfinity;

            return;
        }

        SplitterDeviceInfo.Instance = null;
        SplitterDeviceInfo.Visibility = Visibility.Collapsed;

        var left = Math.Max(0, SecondaryPaneHost.TranslatePoint(new Point(0, 0), StatusBar).X);

        if (Math.Abs(SecondaryStatus.Margin.Left - left) > 0.5)
            SecondaryStatus.Margin = new Thickness(left, 0, 0, 0);

        var splitterStart = PaneSplitter.TranslatePoint(new Point(0, 0), StatusBar).X;

        PlaceDeviceInfo(PrimaryDeviceInfo, twoDevices ? Instance : null, splitterStart);
        PlaceDeviceInfo(SecondaryDeviceInfo, twoDevices ? _secondaryContent.Instance : null, secondaryEnd);

        if (twoDevices)
        {
            PrimaryStatus.MaxWidth = Math.Max(0, Math.Min(left, splitterStart - PrimaryDeviceInfo.ActualWidth));
            SecondaryStatus.MaxWidth = Math.Max(0, secondaryEnd - left - SecondaryDeviceInfo.ActualWidth);
        }
        else
        {
            PrimaryStatus.MaxWidth = left;
            SecondaryStatus.MaxWidth = double.PositiveInfinity;
        }
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

    /// <summary>Shows <paramref name="secondary"/> beside or below this content's list, with a splitter between them.</summary>
    internal void ShowSecondary(ExplorerPageContent secondary, bool stacked)
    {
        // Shown again for a change of orientation, so what was watched is let go first.
        Instance.PropertyChanged -= PaneDevice_PropertyChanged;
        _secondaryContent?.Instance.PropertyChanged -= PaneDevice_PropertyChanged;

        _secondaryContent = secondary;
        Instance.PropertyChanged += PaneDevice_PropertyChanged;
        secondary.Instance.PropertyChanged += PaneDevice_PropertyChanged;
        secondary.SetPaneOnly(this);
        SecondaryPaneHost.Content = secondary;
        ApplyPaneLayout(stacked);
        SyncPagePane();

        SetSharedPaneSizes(stacked);
        PaneSplitter.Visibility = Visibility.Visible;
        SwapPanesHost.Visibility = Visibility.Visible;
        UpdateStatusArrangement();

        RefreshDetailsAvailability();
        PanesGrid.UpdateLayout();
    }

    /// <summary>Which device a pane is on is no layout change, yet decides whether the status bar shows a device info for each pane.</summary>
    private void PaneDevice_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ExplorerInstance.EffectiveDevice))
            App.SafeBeginInvoke(PositionSecondaryStatus, DispatcherPriority.Loaded);
    }

    /// <summary>Moves the splitter by sharing the panes' current sizes out again, each keeping at least its minimum.</summary>
    private void PaneSplitter_DragDelta(object sender, DragDeltaEventArgs e)
    {
        double primary, secondary, delta, min;

        if (IsStacked)
        {
            primary = PrimaryPaneRow.ActualHeight;
            secondary = SecondaryPaneRow.ActualHeight;
            delta = e.VerticalChange;
            min = MIN_PANE_HEIGHT;
        }
        else
        {
            primary = PrimaryPaneColumn.ActualWidth;
            secondary = SecondaryPaneColumn.ActualWidth;
            delta = e.HorizontalChange;
            min = MIN_PANE_WIDTH;
        }

        if (primary + secondary < min * 2)
            return;

        delta = Math.Clamp(delta, min - primary, secondary - min);

        SetPaneSizes(new(primary + delta, GridUnitType.Star), new(secondary - delta, GridUnitType.Star));
        e.Handled = true;
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
        if (_secondaryContent is not null)
        {
            Instance.PropertyChanged -= PaneDevice_PropertyChanged;
            _secondaryContent.Instance.PropertyChanged -= PaneDevice_PropertyChanged;
        }

        SecondaryPaneHost.Content = null;
        _secondaryContent = null;

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
            toolbar = ADB_Explorer.ViewModels.MainToolBar.Build(target);
            _toolbars[target] = toolbar;
        }

        ((BindingProxy)Resources["ChromeInstanceProxy"]).Data = target;
        InstanceHelper.SetInstance(ToolbarRow, target);
        InstanceHelper.SetInstance(StatusBar, target);
        MainToolBar.ItemsSource = toolbar;
        SearchOptionsControl.SetInstance(target);
        RefreshChromeEnabled();

        ActivateContent();
    }
}
