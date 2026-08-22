using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;
using ADB_Explorer.Services.AppInfra;
using ADB_Explorer.ViewModels;
using ADB_Explorer.ViewModels.Pages;
using ADB_Explorer.Controls.Pages;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
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
        Loaded += NavigationPane_Loaded;
    }

    private NavigationTreeViewModel? TreeVm
        => App.Services.GetService<ExplorerViewModel>()?.Tree;

    private void NavigationPane_Loaded(object sender, RoutedEventArgs e)
    {
        if (TreeVm is not { } tree)
            return;

        tree.NodeEditStarted -= Tree_NodeEditStarted;
        tree.NodeEditStarted += Tree_NodeEditStarted;
        Tree.PreviewKeyDown -= Tree_PreviewKeyDown;
        Tree.PreviewKeyDown += Tree_PreviewKeyDown;
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
    private Point _treeDragStart;
    private NavigationTreeNode? _treeDragNode;
    private TreeViewItem? _treeDragItem;
    private bool _treeDragPending;
    private bool _treeDidDrag;
    private NavigationTreeNode? _treeDropHighlight;
    private ScrollViewer? TreeScrollViewer => field ??= StyleHelper.FindDescendant<ScrollViewer>(Tree);

    private void TreeViewItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Data.CopyPaste.WasDragging = false;

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
        if (Data.CopyPaste.WasDragging)
        {
            e.Handled = true;
            return;
        }

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
            if (TreeVm?.HasQueuedEdit == true)
                TreeVm.StartQueuedEdit();
        }, DispatcherPriority.Loaded);
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
        TreeVm?.SetContextTarget(node);
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

        if (item.DataContext is not NavigationTreeNode node)
            return;

        if (node.IsInEditMode)
        {
            e.Handled = true;
            return;
        }

        if (IsExpanderSource(source))
        {
            e.Handled = true;
            return;
        }

        if (CanStartTreeDrag(node))
        {
            _treeDragPending = true;
            _treeDidDrag = false;
            _treeDragNode = node;
            _treeDragItem = item;
            _treeDragStart = e.GetPosition(null);
            item.CaptureMouse();
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

    private void Tree_NodeEditStarted(object? sender, NavigationTreeNode node)
        => Dispatcher.BeginInvoke(() => ShowTreeRename(node), DispatcherPriority.Loaded);

    private void ShowTreeRename(NavigationTreeNode node, int attempt = 0)
    {
        var item = FindTreeViewItem(Tree, node);
        if (item is not null)
            ScrollTreeItemIntoView(item);

        var textBox = item is null ? null : StyleHelper.FindDescendant<TextBox>(item);
        if (textBox is null || !textBox.IsVisible || textBox.ActualHeight <= 0)
        {
            if (attempt < 8)
                Dispatcher.BeginInvoke(() => ShowTreeRename(node, attempt + 1), DispatcherPriority.Loaded);
            return;
        }

        TreeVm?.PrepareRenameTextBox(textBox);
        FindHeader()?.ShowRenameTooltip(textBox, node);
        Dispatcher.BeginInvoke(() => FocusTreeRenameBox(textBox, node), DispatcherPriority.Input);
    }

    private static void FocusTreeRenameBox(TextBox textBox, NavigationTreeNode node)
    {
        if (!node.IsInEditMode || !textBox.IsVisible)
            return;

        Keyboard.Focus(textBox);
        textBox.Focus();
        textBox.SelectAll();
    }

    private ExplorerPageHeader? FindHeader()
    {
        DependencyObject current = this;
        while (current is not null)
        {
            if (current is ExplorerPageHeader header)
                return header;

            current = current is Visual
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return null;
    }

    private static TreeViewItem? FindTreeViewItem(ItemsControl parent, NavigationTreeNode node)
    {
        foreach (var item in parent.Items)
        {
            if (parent.ItemContainerGenerator.ContainerFromItem(item) is not TreeViewItem container)
                continue;

            if (ReferenceEquals(container.DataContext, node))
                return container;

            var nested = FindTreeViewItem(container, node);
            if (nested is not null)
                return nested;
        }

        return null;
    }

    private void TreeNameEdit_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true || sender is not TextBox textBox)
            return;

        if (textBox.DataContext is not NavigationTreeNode node || !node.IsInEditMode)
            return;

        TreeVm?.PrepareRenameTextBox(textBox);
        Dispatcher.BeginInvoke(() => FocusTreeRenameBox(textBox, node), DispatcherPriority.Input);
    }

    private void Tree_PreviewKeyDown(object sender, KeyEventArgs e)
        => TryFinishTreeEditFromKey(e);

    private void TreeNameEdit_PreviewKeyDown(object sender, KeyEventArgs e)
        => TryFinishTreeEditFromKey(e, sender as TextBox);

    private void TreeNameEdit_KeyDown(object sender, KeyEventArgs e)
        => TryFinishTreeEditFromKey(e, sender as TextBox);

    private void TryFinishTreeEditFromKey(KeyEventArgs e, TextBox? textBox = null)
    {
        if (TreeVm is not { } tree || tree.EditingNode is null)
            return;

        if (e.Key is not Key.Enter and not Key.Escape and not Key.F2)
            return;

        textBox ??= Keyboard.FocusedElement as TextBox;
        if (textBox is null)
            return;

        e.Handled = true;
        if (e.Key is Key.Enter)
            tree.CommitEdit(textBox);
        else
            tree.EscapeEdit(textBox);

        FindHeader()?.FocusActiveListing();
    }

    private bool IsFocusInTree()
    {
        var focus = Keyboard.FocusedElement as DependencyObject;
        while (focus is not null)
        {
            if (ReferenceEquals(focus, Tree))
                return true;

            focus = focus is Visual
                ? VisualTreeHelper.GetParent(focus)
                : LogicalTreeHelper.GetParent(focus);
        }

        return false;
    }

    private void TreeNameEdit_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox textBox || TreeVm is not { } tree)
            return;

        if (textBox.DataContext is NavigationTreeNode node && !node.IsInEditMode)
            return;

        var restorePrevious = !IsFocusInTree();
        tree.CommitEdit(textBox, restorePrevious);
    }

    private void TreeNameEdit_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox textBox)
            return;

        TreeVm?.UpdateRenameLegality(textBox);
    }

    private bool CanStartTreeDrag(NavigationTreeNode node)
    {
        if (TreeVm?.IsTreeDragBlocked != false)
            return false;

        if (node.IsTemp || node.IsInEditMode)
            return false;

        return node.IsLogicalDriveNode || node.IsFolderNode;
    }

    private void Tree_PreviewMouseMove(object sender, MouseEventArgs e)
        => TryStartTreeDrag(e);

    private void TreeViewItem_MouseMove(object sender, MouseEventArgs e)
        => TryStartTreeDrag(e);

    private void TryStartTreeDrag(MouseEventArgs e)
    {
        if (!_treeDragPending || e.LeftButton is not MouseButtonState.Pressed)
            return;

        var delta = e.GetPosition(null) - _treeDragStart;
        if (delta.LengthSquared < 25)
            return;

        var node = _treeDragNode;
        var item = _treeDragItem;
        _treeDragPending = false;
        _treeDidDrag = true;
        ReleaseTreeDragCapture();

        if (node is null || item is null || !CanStartTreeDrag(node))
            return;

        InitiateTreeDrag(item, node);
    }

    private void Tree_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        => FinishPendingTreeClick();

    private void TreeViewItem_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        => FinishPendingTreeClick();

    private void FinishPendingTreeClick()
    {
        if (!_treeDragPending)
        {
            ReleaseTreeDragCapture();
            return;
        }

        var item = _treeDragItem;
        _treeDragPending = false;
        ReleaseTreeDragCapture();

        if (_treeDidDrag || item is null)
            return;

        item.IsSelected = true;
    }

    private void ReleaseTreeDragCapture()
    {
        if (_treeDragItem?.IsMouseCaptured == true)
            _treeDragItem.ReleaseMouseCapture();

        _treeDragItem = null;
        _treeDragNode = null;
    }

    private void InitiateTreeDrag(TreeViewItem item, NavigationTreeNode node)
    {
        var list = FileList.FromTreeNode(node);
        if (list?.SelectedFiles.Any() != true)
            return;

        var files = list.SelectedFiles.ToList();
        VirtualFileDataObject? vfdo;
        using (Data.Use(list))
        {
            vfdo = VirtualFileDataObject.PrepareTransfer(
                files,
                DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
            if (vfdo is null)
                return;

            Data.CopyPaste.UpdateSelfVFDO(true);
        }

        Data.CopyPaste.DragStatus = CopyPasteService.DragState.Active;
        Data.CopyPaste.WasDragging = true;
        Data.CopyPaste.DragBitmap = files[0].DragImage;
        NavigationTreeNode.SuppressUserSelectFromExpander++;
        try
        {
            vfdo.SendObjectToShell(
                VirtualFileDataObject.DataObjectMethod.DragDrop,
                item,
                vfdo.PreferredDropEffect ?? DragDropEffects.Copy);
        }
        finally
        {
            Data.CopyPaste.DragStatus = CopyPasteService.DragState.None;
            if (Data.CopyPaste.IsDrag)
                Data.CopyPaste.ClearDrag();
            ClearTreeDropHighlight();
            if (NavigationTreeNode.SuppressUserSelectFromExpander > 0)
                NavigationTreeNode.SuppressUserSelectFromExpander--;
        }
    }

    private void TreeViewItem_DragEnter(object sender, DragEventArgs e)
    {
        if (sender is not TreeViewItem item || item.DataContext is not NavigationTreeNode node)
            return;

        SetTreeDropHighlight(node, Data.CopyPaste.GetAllowedTreeDropEffects(e.Data, node));
        ApplyTreeDropEffects(e, node);
        e.Handled = true;
    }

    private void TreeViewItem_DragOver(object sender, DragEventArgs e)
    {
        if (sender is not TreeViewItem item || item.DataContext is not NavigationTreeNode node)
            return;

        ApplyTreeDropEffects(e, node);
        e.Handled = true;
    }

    private void TreeViewItem_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is TreeViewItem item && item.DataContext is NavigationTreeNode node)
            ClearTreeDropHighlight(node);

        e.Handled = true;
    }

    private void TreeViewItem_Drop(object sender, DragEventArgs e)
    {
        if (sender is TreeViewItem item && item.DataContext is NavigationTreeNode node)
        {
            ApplyTreeDropEffects(e, node);
            if (e.Effects is not DragDropEffects.None)
                Data.CopyPaste.AcceptTreeDrop(e, node);
        }

        ClearTreeDropHighlight();
        e.Handled = true;
    }

    private void ApplyTreeDropEffects(DragEventArgs e, NavigationTreeNode node)
    {
        var allowed = Data.CopyPaste.GetAllowedTreeDropEffects(e.Data, node);
        SetTreeDropHighlight(node, allowed);

        if (allowed.HasFlag(DragDropEffects.Move)
            && Data.CopyPaste.IsSelf
            && !e.KeyStates.HasFlag(DragDropKeyStates.ControlKey)
            && !e.KeyStates.HasFlag(DragDropKeyStates.AltKey))
        {
            e.Effects = DragDropEffects.Move;
        }
        else if (allowed.HasFlag(DragDropEffects.Move) && e.KeyStates.HasFlag(DragDropKeyStates.ShiftKey))
        {
            e.Effects = DragDropEffects.Move;
        }
        else if (allowed.HasFlag(DragDropEffects.Link) && e.KeyStates.HasFlag(DragDropKeyStates.AltKey))
        {
            e.Effects = DragDropEffects.Link;
        }
        else if (allowed.HasFlag(DragDropEffects.Copy))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
            e.Effects = allowed;

        if ((!allowed.HasFlag(DragDropEffects.Copy) && e.KeyStates.HasFlag(DragDropKeyStates.ControlKey))
            || (!allowed.HasFlag(DragDropEffects.Move) && e.KeyStates.HasFlag(DragDropKeyStates.ShiftKey))
            || (!allowed.HasFlag(DragDropEffects.Link) && e.KeyStates.HasFlag(DragDropKeyStates.AltKey)))
        {
            e.Effects = DragDropEffects.None;
        }

        Data.CopyPaste.DropEffect =
        Data.CopyPaste.CurrentDropEffect = e.Effects;
        Data.CopyPaste.DropTarget = node.Path;
    }

    private void SetTreeDropHighlight(NavigationTreeNode node, DragDropEffects allowed)
    {
        if (_treeDropHighlight is not null && !ReferenceEquals(_treeDropHighlight, node))
            _treeDropHighlight.IsDragOver = false;

        _treeDropHighlight = node;
        node.IsDragOver = allowed is not DragDropEffects.None;
    }

    private void ClearTreeDropHighlight(NavigationTreeNode? node = null)
    {
        if (node is not null && !ReferenceEquals(_treeDropHighlight, node))
        {
            node.IsDragOver = false;
            return;
        }

        if (_treeDropHighlight is not null)
            _treeDropHighlight.IsDragOver = false;

        _treeDropHighlight = null;
    }
}
