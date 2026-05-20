namespace ADB_Explorer.Helpers;

internal class TreeViewItemHelper
{
    private static readonly DependencyPropertyKey IndentationPropertyKey =
            DependencyProperty.RegisterAttachedReadOnly(
                "Indentation",
                typeof(Thickness),
                typeof(TreeViewItemHelper),
                null);

    /// <summary>
    /// Identifies the Indentation dependency property.
    /// </summary>
    public static readonly DependencyProperty IndentationProperty =
        IndentationPropertyKey.DependencyProperty;

    /// <summary>
    /// Gets the amount that the item is indented.
    /// </summary>
    /// <param name="treeViewItem">The element from which to read the property value.</param>
    /// <returns>The amount that the item is indented.</returns>
    public static Thickness GetIndentation(TreeViewItem treeViewItem)
    {
        return (Thickness)treeViewItem.GetValue(IndentationProperty);
    }

    private static void SetIndentation(TreeViewItem treeViewItem, Thickness value)
    {
        treeViewItem.SetValue(IndentationPropertyKey, value);
    }

    private static void UpdateIndentation(TreeViewItem item)
    {
        SetIndentation(item, new Thickness(GetDepth(item) * 16, 0, 0, 0));
    }

    private static int GetDepth(TreeViewItem item)
    {
        int depth = 0;
        while (ItemsControl.ItemsControlFromItemContainer(item) is TreeViewItem parentItem)
        {
            depth++;
            item = parentItem;
        }
        return depth;
    }
}
