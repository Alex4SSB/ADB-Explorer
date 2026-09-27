namespace ADB_Explorer.Controls;

/// <summary>
/// Interaction logic for NavigationPane.xaml
/// </summary>
public partial class NavigationPane : UserControl
{
    public IEnumerable<NavigationTreeNode> TreeItems
    {
        get => (IEnumerable<NavigationTreeNode>)GetValue(TreeItemsProperty);
        set => SetValue(TreeItemsProperty, value);
    }

    public static readonly DependencyProperty TreeItemsProperty =
        DependencyProperty.Register(nameof(TreeItems), typeof(IEnumerable<NavigationTreeNode>),
            typeof(NavigationPane), null);

    public NavigationPane()
    {
        InitializeComponent();

        // The runtime wiring needs app services and static state the XAML designer doesn't have.
        if (DesignerProperties.GetIsInDesignMode(this))
            return;

        Loaded += NavigationPane_Loaded;
    }

    private NavigationTreeViewModel? TreeVm
        => App.Services.GetService<ExplorerViewModel>()?.Tree;

    private void NavigationPane_Loaded(object sender, RoutedEventArgs e)
    {
        if (TreeVm is { } tree)
        {
            tree.NodeEditStarted -= Tree_NodeEditStarted;
            tree.NodeEditStarted += Tree_NodeEditStarted;
            Tree.PreviewKeyDown -= Tree_PreviewKeyDown;
            Tree.PreviewKeyDown += Tree_PreviewKeyDown;
        }

        DragAutoScroll.Register(TreeScrollViewer);
        Unloaded -= NavigationPane_Unloaded;
        Unloaded += NavigationPane_Unloaded;
    }

    private void NavigationPane_Unloaded(object sender, RoutedEventArgs e)
        => DragAutoScroll.Unregister(TreeScrollViewer);

    private NavigationTreeNode? _selectionBeforeExpander;
    private ScrollViewer? TreeScrollViewer => field ??= StyleHelper.FindDescendant<ScrollViewer>(Tree);

    private void TreeViewItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Data.CopyPaste.WasDragging = false;

        if (sender is not TreeViewItem item || e.OriginalSource is not DependencyObject source)
            return;

        if (!ReferenceEquals(FindOwningTreeViewItem(source), sender))
            return;

        if (IsExpanderSource(source))
        {
            NavigationTreeNode.SuppressUserSelectFromExpander++;
            _selectionBeforeExpander = FindSelectedNode(TreeItems);
            Dispatcher.BeginInvoke(EndExpanderInteraction, DispatcherPriority.Input);
            return;
        }

        if (item.DataContext is not NavigationTreeNode node)
            return;

        // Let the rename box receive the click.
        if (node.IsInEditMode)
            return;

        // Keep TreeView from selecting on mouse down so a drag can start first.
        e.Handled = true;

        if (e.ClickCount > 1)
        {
            item.IsSelected = true;
            if (item.HasItems)
                item.IsExpanded = !item.IsExpanded;
            return;
        }

        // Dismissing an open context menu must not start a drag.
        if (_contextTarget is not null || _suppressTreeDragAfterMenu)
        {
            _suppressTreeDragAfterMenu = false;
            return;
        }

        _treeDragPending = true;
        _treeDidDrag = false;
        _treeDragNode = node;
        _treeDragItem = item;
        _treeDragStart = e.GetPosition(null);
        item.CaptureMouse();
    }

    private void TreeViewItem_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is not MouseButton.Middle || sender is not TreeViewItem item || e.OriginalSource is not DependencyObject source)
            return;

        if (!ReferenceEquals(FindOwningTreeViewItem(source), item) || item.DataContext is not NavigationTreeNode node)
            return;

        e.Handled = true;
        TreeVm?.OpenNodeInNewTab(node);
    }

    private void EndExpanderInteraction()
    {
        RestoreSelectionKeepSuppress();
        _selectionBeforeExpander = null;

        if (NavigationTreeNode.SuppressUserSelectFromExpander > 0)
            NavigationTreeNode.SuppressUserSelectFromExpander--;
    }

    private bool ShouldSkipTreeScroll()
        => NavigationTreeNode.SuppressUserSelectFromExpander > 0
        || _holdSelectSuppressForMenu
        || _contextTarget is not null;

    private void TreeViewItem_Selected(object sender, RoutedEventArgs e)
    {
        if (ShouldSkipTreeScroll())
            return;

        if (sender is TreeViewItem item && ReferenceEquals(e.OriginalSource, item))
            Dispatcher.BeginInvoke(() => ScrollTreeItemIntoView(item), DispatcherPriority.Loaded);
    }

    private void TreeViewItem_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        // Child headers (long names) raise this on expand/collapse; the default
        // ScrollViewer pans horizontally to fit the full item. Always suppress.
        e.Handled = true;
    }

    private void ScrollTreeItemIntoView(TreeViewItem item)
    {
        if (TreeScrollViewer is not ScrollViewer scrollViewer)
            return;

        item.ApplyTemplate();
        var row = item.Template.FindName("Border", item) as FrameworkElement;
        if (row is null)
            return;

        Rect rowBounds;
        try
        {
            rowBounds = row.TransformToAncestor(scrollViewer)
                .TransformBounds(new Rect(0, 0, row.ActualWidth, row.ActualHeight));
        }
        catch (InvalidOperationException)
        {
            return;
        }

        var verticalOffset = scrollViewer.VerticalOffset;
        if (rowBounds.Top < 0)
            verticalOffset += rowBounds.Top;
        else if (rowBounds.Bottom > scrollViewer.ViewportHeight)
            verticalOffset += rowBounds.Bottom - scrollViewer.ViewportHeight;

        scrollViewer.ScrollToVerticalOffset(Math.Max(0, verticalOffset));
    }

    private static NavigationTreeNode? FindSelectedNode(IEnumerable<NavigationTreeNode>? nodes)
    {
        if (nodes is null)
            return null;

        foreach (var node in nodes)
        {
            if (node.IsSelected)
                return node;

            var child = FindSelectedNode(node.Children);
            if (child is not null)
                return child;
        }

        return null;
    }

    private static bool IsExpanderSource(DependencyObject source)
    {
        var current = source;
        while (current is not null && current is not TreeViewItem)
        {
            if (current is ToggleButton)
                return true;

            current = current is Visual
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return false;
    }

    private static TreeViewItem? FindOwningTreeViewItem(DependencyObject source)
    {
        var current = source;
        while (current is not null)
        {
            if (current is TreeViewItem item)
                return item;

            current = current is Visual
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return null;
    }
}
