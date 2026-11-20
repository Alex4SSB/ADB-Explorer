namespace ADB_Explorer.Controls;

/// <summary>
/// Interaction logic for ExplorerTabStrip.xaml
/// </summary>
public partial class ExplorerTabStrip : UserControl
{
    /// <summary>How long a drag must hover over a tab before that tab is switched to.</summary>
    private static readonly TimeSpan DragHoverDelay = TimeSpan.FromMilliseconds(600);

    private readonly DispatcherTimer _dragHoverTimer = new() { Interval = DragHoverDelay };

    private ExplorerInstance? _dragHoverTab;

    /// <summary>Data format of a tab header being dragged to a new place in the strip, or out to become a split view's pane.</summary>
    private const string TabDragFormat = "ADBExplorer.TabHeader";

    /// <summary>Raised around a tab header drag, so the window can show where the tab would be dropped.</summary>
    public event Action<ExplorerInstance>? TabDragStarted;

    public event Action? TabDragEnded;

    private ExplorerInstance? _pressedTab;

    private Point _pressPoint;

    private Point _pressPointInItem;

    /// <summary>Where the dragged tab lands if dropped now, as last marked; -1 when nothing is marked.</summary>
    private int _reorderIndex = -1;

    /// <summary>
    /// What the drag over the title bar last offered to open, for the drop: by then a file drag no longer
    /// counts as active, as the button's release ends it before the drop is delivered.
    /// </summary>
    private NewTabDrop? _newTabDrop;

    /// <summary>Something dragged over the title bar that opens in a new tab, by its name, at the index it's dropped at.</summary>
    private sealed record NewTabDrop(string Name, Action<int> Open);

    public ExplorerTabStrip()
    {
        DataContext = App.Services.GetService<ExplorerTabsViewModel>();

        _dragHoverTimer.Tick += DragHoverTimer_Tick;

        InitializeComponent();

        // Deferred: the shortcut text comes from AppActions, which isn't ready while the main window is still being built.
        Loaded += (_, _) => AddTabButton.ToolTip = ((ExplorerTabsViewModel)DataContext).NewTabTooltip;
    }

    private void AddTab_Click(object sender, RoutedEventArgs e) =>
        ((ExplorerTabsViewModel)DataContext).AddTab();

    /// <summary>The button has no menu of its own, and the title bar behind it would show the system one.</summary>
    private void AddTab_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        => e.Handled = true;

    /// <summary>Opens the tab's own menu by hand: the title bar swallows the right click before WPF would, and would show the system menu.</summary>
    private void TabsList_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        // The release that cancelled a tab drag isn't a click.
        if (Data.CopyPaste.WasDragging)
        {
            e.Handled = true;
            return;
        }

        if (e.OriginalSource is not DependencyObject source
            || ItemsControl.ContainerFromElement(TabsList, source) is not ListBoxItem { DataContext: ExplorerInstance tab, ContextMenu: { } menu } item)
            return;

        TabContextMenu.SetFor(tab);
        menu.PlacementTarget = item;
        menu.IsOpen = true;
        e.Handled = true;
    }

    /// <summary>A right click opens the tab's menu without switching to it, which pressing it would do.</summary>
    private void TabItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        => e.Handled = true;

    /// <summary>A tab is switched to when the mouse is released on it, so it can also be pressed and dragged away.</summary>
    private void TabItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Data.CopyPaste.WasDragging = false;

        if (HitTestHelper.FindAncestor<ButtonBase>(e.OriginalSource as DependencyObject) is not null)
            return;

        if (sender is not FrameworkElement { DataContext: ExplorerInstance tab } item)
            return;

        _pressedTab = tab;
        _pressPoint = e.GetPosition(this);
        _pressPointInItem = e.GetPosition(item);
        item.CaptureMouse();
        e.Handled = true;
    }

    private void TabItem_MouseMove(object sender, MouseEventArgs e)
    {
        if (_pressedTab is not { } tab || e.LeftButton is not MouseButtonState.Pressed || sender is not FrameworkElement item)
            return;

        var offset = e.GetPosition(this) - _pressPoint;
        if (Math.Abs(offset.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(offset.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        _pressedTab = null;
        item.ReleaseMouseCapture();

        StartTabDrag(item, tab, _pressPointInItem);
    }

    /// <summary>
    /// Marks where <paramref name="tab"/> lands if dropped at <paramref name="e"/>'s position: the gap between tabs nearest
    /// the pointer, as a DataGrid picks for a dragged column header. The pill goes on the left side of that place.
    /// </summary>
    /// <param name="tab">The dragged tab, or null for a new tab opened by the drop.</param>
    public void ShowReorderTarget(ExplorerInstance? tab, DragEventArgs e)
    {
        var tabs = ((ExplorerTabsViewModel)DataContext).Tabs;
        var from = -1;
        if (tab is not null)
            from = tabs.IndexOf(tab);

        var containers = Enumerable.Range(0, tabs.Count)
            .Select(i => TabsList.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement)
            .ToList();

        if ((tab is not null && from < 0) || containers.Count == 0 || containers.Any(c => c is null))
        {
            HideReorderTarget();
            return;
        }

        var x = e.GetPosition(TabsList).X;
        var gap = containers.Count(c => c!.TranslatePoint(new Point(c.ActualWidth / 2, 0), TabsList).X < x);

        // A new tab is inserted at the gap. A moved one leaves its own place, so the gap after it
        // leaves it in place too, and the pill stays on its own left side.
        _reorderIndex = gap;
        if (tab is not null && gap == from + 1)
        {
            gap = from;
            _reorderIndex = from;
        }
        else if (tab is not null && gap > from)
            _reorderIndex = gap - 1;

        // Centered between the tabs' margins, and on their bodies.
        var neighbor = containers[Math.Min(gap, containers.Count - 1)]!;
        var edge = neighbor.TranslatePoint(new Point(-neighbor.Margin.Left, 0), ReorderCanvas);
        if (gap == containers.Count)
            edge = neighbor.TranslatePoint(new Point(neighbor.ActualWidth + neighbor.Margin.Right, 0), ReorderCanvas);

        var left = Math.Clamp(edge.X - ReorderIndicator.Width / 2, 0, Math.Max(0, ReorderCanvas.ActualWidth - ReorderIndicator.Width));
        Canvas.SetLeft(ReorderIndicator, left);
        Canvas.SetTop(ReorderIndicator, edge.Y + (TabBodyHeight(neighbor) - ReorderIndicator.Height) / 2);
        ReorderIndicator.Visibility = Visibility.Visible;
    }

    public void HideReorderTarget()
    {
        _reorderIndex = -1;
        ReorderIndicator.Visibility = Visibility.Collapsed;
    }

    /// <summary>Moves <paramref name="tab"/> to where it was dropped and switches to it, as releasing a pressed tab does.</summary>
    public void DropReorderTarget(ExplorerInstance tab, DragEventArgs e)
    {
        ShowReorderTarget(tab, e);

        var target = _reorderIndex;
        HideReorderTarget();

        var tabs = (ExplorerTabsViewModel)DataContext;
        var from = tabs.Tabs.IndexOf(tab);
        if (target >= 0 && from >= 0 && target != from)
            tabs.Tabs.Move(from, target);

        tabs.ActiveTab = tab;
    }

    /// <summary>
    /// Over the title bar, a dragged folder or drive of our own, device or page opens in a new tab, which the pill
    /// places as it does a dragged tab. Returns the drag's effect there.
    /// </summary>
    public DragDropEffects ShowNewTabTarget(DragEventArgs e)
    {
        var effect = DragDropEffects.None;
        var drop = DraggedNewTab();
        if (drop is not null && e.AllowedEffects.HasFlag(DragDropEffects.Link))
            effect = DragDropEffects.Link;
        else if (drop is not null && e.AllowedEffects.HasFlag(DragDropEffects.Copy))
            effect = DragDropEffects.Copy;

        if (drop is null || effect is DragDropEffects.None)
        {
            HideNewTabTarget();
            return DragDropEffects.None;
        }

        _newTabDrop = drop;

        // Named before the effect is set, which redraws the drag window's tooltip
        Data.CopyPaste.NewTabDropName = drop.Name;
        Data.CopyPaste.DropEffect = Data.CopyPaste.CurrentDropEffect = effect;
        ShowReorderTarget(null, e);

        return effect;
    }

    public void HideNewTabTarget()
    {
        HideReorderTarget();
        _newTabDrop = null;
        Data.CopyPaste.NewTabDropName = null;
    }

    /// <summary>Opens what was dragged in a new tab, where the pill marked, and switches to it.</summary>
    public void DropNewTab(DragEventArgs e)
    {
        ShowReorderTarget(null, e);

        var target = _reorderIndex;
        var dropped = _newTabDrop;
        HideNewTabTarget();

        if (dropped is null)
            return;

        if (target < 0)
            target = ((ExplorerTabsViewModel)DataContext).Tabs.Count;

        dropped.Open(target);
    }

    /// <summary>What the drag holds that opens in a new tab: one folder or drive of our own, a device, or a page.</summary>
    private NewTabDrop? DraggedNewTab()
    {
        switch (Data.CopyPaste.DragContent)
        {
            case AdbLocation { PageType: { } pageType } page:
                return new(page.DisplayName, index => ((ExplorerTabsViewModel)DataContext).OpenPageInNewTab(pageType, index));

            case LogicalDeviceViewModel { Status: DeviceStatus.Ok } device:
                return new(device.Name, index => DeviceHelper.OpenDeviceInNewTab(device, dropIndex: index));

            case not null:
                return null;
        }

        if (DraggedFolder() is not { } folder)
            return null;

        return new(folder.Name, index => DeviceHelper.OpenDeviceInNewTab(folder.Device, new AdbLocation(folder.Path), index));
    }

    /// <summary>The single folder or drive our own drag holds, as the menu's Open in new tab takes one.</summary>
    private static (LogicalDeviceViewModel Device, string Path, string Name)? DraggedFolder()
    {
        if (Data.CopyPaste.DragStatus is not CopyPasteService.DragState.Active
            || VirtualFileDataObject.SelfDevice is not { Status: DeviceStatus.Ok } device
            || VirtualFileDataObject.SelfFiles?.ToList() is not [{ Type: AbstractFile.FileType.Folder } folder])
            return null;

        var path = folder.FullPath;
        if (!string.IsNullOrEmpty(folder.LinkTarget))
            path = folder.LinkTarget;

        if (FileHelper.IsInTrash(path))
            return null;

        // A drive goes by the drive's name, whether dragged as a drive or found by its root folder
        var name = folder.FullName;
        if (Data.CopyPaste.SelfDragDrive is { } draggedDrive)
            name = draggedDrive.DisplayName;
        else if (DriveHelper.GetCurrentDrive(path, device) is { } drive && NavigationTreeNode.IsDriveRootPath(path, drive))
            name = drive.DisplayName;

        return (device, path, name);
    }

    private void StartTabDrag(FrameworkElement item, ExplorerInstance tab, Point pressPointInItem)
    {
        var scale = VisualTreeHelper.GetDpi(item).DpiScaleX;

        // Held where the header was grabbed, at the size of its body.
        // In right-to-left layouts the press point is measured from the header's right edge.
        var pressX = pressPointInItem.X;
        if (item.FlowDirection is FlowDirection.RightToLeft)
            pressX = item.ActualWidth - pressX;

        var imageRect = new Rect(
            -pressX * scale,
            -pressPointInItem.Y * scale,
            item.ActualWidth * scale,
            TabBodyHeight(item) * scale);

        TabDragStarted?.Invoke(tab);

        try
        {
            ContentDrag.Run(item, tab, imageRect, new DataObject(TabDragFormat, tab), DragDropEffects.Move);
        }
        finally
        {
            TabDragEnded?.Invoke();
        }
    }

    /// <summary>The release that cancelled a tab drag doesn't open the tab's own menu either.</summary>
    private void TabItem_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (Data.CopyPaste.WasDragging)
            e.Handled = true;
    }

    /// <summary>The tab's body leaves out the strip below it that the active tab fuses through.</summary>
    private static double TabBodyHeight(FrameworkElement item)
    {
        var body = (item as Control)?.Template?.FindName("Bd", item) as Border;
        if (body is { ActualHeight: > 0 })
            return body.ActualHeight;

        return item.ActualHeight;
    }

    private void TabItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_pressedTab is not { } tab || sender is not FrameworkElement item)
            return;

        _pressedTab = null;
        item.ReleaseMouseCapture();

        if (new Rect(0, 0, item.ActualWidth, item.ActualHeight).Contains(e.GetPosition(item)))
            ((ExplorerTabsViewModel)DataContext).ActiveTab = tab;

        e.Handled = true;
    }

    /// <summary>
    /// Hovering a drag over a tab brings that tab forward. The drag goes on to the title bar,
    /// where a dragged folder opens in a new tab (<see cref="ShowNewTabTarget"/>).
    /// </summary>
    private void TabItem_DragOver(object sender, DragEventArgs e)
    {
        // A dragged tab header is only meant for the page area.
        if (e.Data.GetDataPresent(TabDragFormat))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        if (sender is FrameworkElement { DataContext: ExplorerInstance tab } && !ReferenceEquals(_dragHoverTab, tab))
        {
            _dragHoverTab = tab;
            _dragHoverTimer.Stop();
            _dragHoverTimer.Start();
        }
    }

    private void TabItem_DragLeave(object sender, DragEventArgs e) => StopDragHover();

    private void TabItem_Drop(object sender, DragEventArgs e) => StopDragHover();

    private void StopDragHover()
    {
        _dragHoverTimer.Stop();
        _dragHoverTab = null;
    }

    private void DragHoverTimer_Tick(object? sender, EventArgs e)
    {
        _dragHoverTimer.Stop();

        var tabs = (ExplorerTabsViewModel)DataContext;
        if (_dragHoverTab is { } tab && tabs.Tabs.Contains(tab) && !ReferenceEquals(tabs.ActiveTab, tab))
            tabs.ActiveTab = tab;
    }

    private void CloseTab_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not ExplorerInstance tab)
            return;

        ((ExplorerTabsViewModel)DataContext).CloseTab(tab);
        e.Handled = true;
    }
}
