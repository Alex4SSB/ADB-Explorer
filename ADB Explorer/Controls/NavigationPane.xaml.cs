using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;
using ADB_Explorer.Services.AppInfra;
using ADB_Explorer.ViewModels;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace ADB_Explorer.Controls;

/// <summary>
/// Interaction logic for NavigationPane.xaml
/// </summary>
public partial class NavigationPane : UserControl
{
    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    public static readonly DependencyProperty IsOpenProperty =
        DependencyProperty.Register(nameof(IsOpen), typeof(bool),
          typeof(NavigationPane), new PropertyMetadata(false, OnIsOpenChanged));

    public double PaneMinWidth
    {
        get => (double)GetValue(PaneMinWidthProperty);
        set => SetValue(PaneMinWidthProperty, value);
    }

    public static readonly DependencyProperty PaneMinWidthProperty =
        DependencyProperty.Register(nameof(PaneMinWidth), typeof(double),
          typeof(NavigationPane), new PropertyMetadata(100.0));

    public double PaneMaxWidth
    {
        get => (double)GetValue(PaneMaxWidthProperty);
        set => SetValue(PaneMaxWidthProperty, value);
    }

    public static readonly DependencyProperty PaneMaxWidthProperty =
        DependencyProperty.Register(nameof(PaneMaxWidth), typeof(double),
          typeof(NavigationPane), new PropertyMetadata(1000.0));

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
        Visibility = IsOpen ? Visibility.Visible : Visibility.Collapsed;
    }

    private void GridSplitter_DragDelta(object sender, DragDeltaEventArgs e)
    {
        double newWidth = ContentBox.ActualWidth + e.HorizontalChange;
        if (newWidth > PaneMinWidth && newWidth < PaneMaxWidth)
            ContentBox.Width = newWidth;
    }

    private static void OnIsOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not NavigationPane pane)
            return;

        pane.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;
    }

    private NavigationTreeNode? _selectionBeforeExpander;
    private NavigationTreeNode? _contextTarget;
    private IDisposable? _treeMenuScope;
    private bool _holdSelectSuppressForMenu;
    private ScrollViewer? TreeScrollViewer => field ??= StyleHelper.FindDescendant<ScrollViewer>(Tree);

    private void TreeViewItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TreeViewItem || e.OriginalSource is not DependencyObject source)
            return;

        if (!ReferenceEquals(FindOwningTreeViewItem(source), sender))
            return;

        if (!IsExpanderSource(source))
            return;

        NavigationTreeNode.SuppressUserSelectFromExpander++;
        _selectionBeforeExpander = FindSelectedNode(TreeItems);
        Dispatcher.BeginInvoke(EndExpanderInteraction, DispatcherPriority.Input);
    }

    private void TreeViewItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TreeViewItem || e.OriginalSource is not DependencyObject source)
            return;

        if (!ReferenceEquals(FindOwningTreeViewItem(source), sender))
            return;

        if (!_holdSelectSuppressForMenu)
        {
            NavigationTreeNode.SuppressUserSelectFromExpander++;
            _holdSelectSuppressForMenu = true;
        }

        _selectionBeforeExpander = FindSelectedNode(TreeItems);
        Dispatcher.BeginInvoke(RestoreSelectionKeepSuppress, DispatcherPriority.Input);
        Dispatcher.BeginInvoke(ReleaseRightClickSuppressIfNoMenu, DispatcherPriority.ApplicationIdle);
    }

    private void TreeViewItem_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not TreeViewItem item || e.OriginalSource is not DependencyObject source)
        {
            e.Handled = true;
            return;
        }

        if (!ReferenceEquals(FindOwningTreeViewItem(source), item))
            return;

        if (item.DataContext is not NavigationTreeNode node)
        {
            e.Handled = true;
            return;
        }

        EndTreeMenuScope();
        SetContextTarget(node);

        if (item.ContextMenu is not AdbContextMenu menu)
            return;

        menu.Closed -= TreeContextMenu_Closed;
        menu.Closed += TreeContextMenu_Closed;

        if (node.Device is { } device)
        {
            DeviceContextMenu.SetFor(device);
            if (DeviceContextMenu.VisibleList.Count == 0)
            {
                CancelTreeContextMenu(menu, e);
                return;
            }

            menu.Style = TryFindResource("DeviceContextMenuStyle") as Style;
            return;
        }

        var list = FileList.FromTreeNode(node);
        if (list is null)
        {
            CancelTreeContextMenu(menu, e);
            return;
        }

        _treeMenuScope = Data.Use(list);
        FileActionLogic.UpdateFileActions(list);
        ExplorerContextMenu.UpdateSeparators(showEmptyPlaceholder: false);
        if (ExplorerContextMenu.VisibleList.Count == 0)
        {
            CancelTreeContextMenu(menu, e);
            return;
        }

        menu.Style = TryFindResource("TreeContextMenuStyle") as Style;
    }

    private void CancelTreeContextMenu(AdbContextMenu menu, ContextMenuEventArgs e)
    {
        menu.Closed -= TreeContextMenu_Closed;
        RestoreSelectionKeepSuppress();
        ReleaseRightClickSuppress();
        _selectionBeforeExpander = null;
        SetContextTarget(null);
        EndTreeMenuScope();
        e.Handled = true;
    }

    private void TreeContextMenu_Closed(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu menu)
            menu.Closed -= TreeContextMenu_Closed;

        RestoreSelectionKeepSuppress();
        ReleaseRightClickSuppress();
        _selectionBeforeExpander = null;
        SetContextTarget(null);
        Dispatcher.BeginInvoke(() =>
        {
            EndTreeMenuScope();
            FileActionLogic.UpdateFileActions();
        }, DispatcherPriority.Background);
    }

    private void EndTreeMenuScope()
    {
        _treeMenuScope?.Dispose();
        _treeMenuScope = null;
    }

    private void SetContextTarget(NavigationTreeNode? node)
    {
        if (_contextTarget is not null)
            _contextTarget.IsContextTarget = false;

        _contextTarget = node;
        if (node is not null)
            node.IsContextTarget = true;
    }

    private void RestoreSelectionKeepSuppress()
    {
        _selectionBeforeExpander?.SetSelected(true);
    }

    private void EndExpanderInteraction()
    {
        RestoreSelectionKeepSuppress();
        _selectionBeforeExpander = null;

        if (NavigationTreeNode.SuppressUserSelectFromExpander > 0)
            NavigationTreeNode.SuppressUserSelectFromExpander--;
    }

    private void ReleaseRightClickSuppressIfNoMenu()
    {
        if (_contextTarget is not null)
            return;

        ReleaseRightClickSuppress();
    }

    private void ReleaseRightClickSuppress()
    {
        if (!_holdSelectSuppressForMenu)
            return;

        _holdSelectSuppressForMenu = false;
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

    private void TreeViewItem_Expanded(object sender, RoutedEventArgs e)
    {
        if (ShouldSkipTreeScroll())
            return;

        if (sender is TreeViewItem item && ReferenceEquals(e.OriginalSource, item))
            Dispatcher.BeginInvoke(() => ScrollTreeItemIntoView(item), DispatcherPriority.Loaded);
    }

    private void TreeViewItem_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        if (sender is not TreeViewItem item || !ReferenceEquals(e.OriginalSource, item))
            return;

        e.Handled = true;

        if (ShouldSkipTreeScroll())
            return;

        ScrollTreeItemIntoView(item);
    }

    private void TreeViewItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TreeViewItem item || e.OriginalSource is not DependencyObject source)
            return;

        if (!ReferenceEquals(FindOwningTreeViewItem(source), item))
            return;

        if (IsExpanderSource(source))
        {
            e.Handled = true;
            return;
        }

        item.IsSelected = true;
        e.Handled = true;
    }

    private void ScrollTreeItemIntoView(TreeViewItem item)
    {
        if (TreeScrollViewer is not ScrollViewer scrollViewer)
            return;

        item.ApplyTemplate();
        var expander = item.Template.FindName("Expander", item) as FrameworkElement;
        var header = item.Template.FindName("PART_Header", item) as FrameworkElement;
        var row = item.Template.FindName("Border", item) as FrameworkElement;
        var target = expander ?? header ?? row;
        if (target is null)
            return;

        Rect targetBounds;
        try
        {
            targetBounds = target.TransformToAncestor(scrollViewer)
                .TransformBounds(new Rect(0, 0, target.ActualWidth, target.ActualHeight));
        }
        catch (InvalidOperationException)
        {
            return;
        }

        var left = targetBounds.Left;
        var showRight = left + targetBounds.Width + 120;
        var viewportWidth = scrollViewer.ViewportWidth;
        var horizontalOffset = scrollViewer.HorizontalOffset;

        if (left < 0)
            horizontalOffset += left;
        else if (showRight > viewportWidth)
            horizontalOffset += showRight - viewportWidth;

        scrollViewer.ScrollToHorizontalOffset(Math.Max(0, horizontalOffset));

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
