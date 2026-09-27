namespace ADB_Explorer.Controls;

public partial class NavigationPane
{
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

        if (TreeVm is { } tree)
            RenameBox.PrepareTreeNode(textBox, tree);

        InstanceHelper.SetInstance(RenameTooltipControl, Data.ActiveExplorerInstance);
        RenameTooltipControl.Visibility = Visibility.Visible;
        RenameTooltipControl.Show(textBox, node);
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

        if (TreeVm is { } tree)
            RenameBox.PrepareTreeNode(textBox, tree);

        Dispatcher.BeginInvoke(() => FocusTreeRenameBox(textBox, node), DispatcherPriority.Input);
    }

    private void Tree_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        TryFinishTreeEditFromKey(e);
        if (e.Handled)
            return;

        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down
            && Data.ActiveDevice is null)
            e.Handled = true;
    }

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
            RenameBox.TreeNodeCommit(textBox, tree);
        else
            RenameBox.TreeNodeEscape(textBox, tree);

        (Application.Current.MainWindow as MainWindow)?.GetOrCreateExplorerContent(Data.ActiveExplorerInstance).FocusActiveListing();
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
        RenameBox.TreeNodeCommit(textBox, tree, restorePrevious);
    }

    private void TreeNameEdit_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox textBox && TreeVm is { } tree)
            RenameBox.TreeNodeTextChanged(textBox, tree);
    }
}
