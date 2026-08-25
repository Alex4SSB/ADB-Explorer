using ADB_Explorer.Views;
using System.Windows.Media.Media3D;

namespace ADB_Explorer.Helpers;

/// <summary>
/// Visual-tree hit testing for explorer drag vs rubber-band selection.
/// </summary>
public static class HitTestHelper
{
    /// <summary>
    /// Walks the visual tree when possible; content elements (<see cref="System.Windows.Documents.Run"/>, etc.)
    /// are not Visuals, so fall back to the logical parent.
    /// </summary>
    public static DependencyObject? GetVisualOrLogicalParent(DependencyObject current)
    {
        if (current is Visual or Visual3D)
            return VisualTreeHelper.GetParent(current);

        if (current is FrameworkContentElement content)
            return content.Parent;

        return LogicalTreeHelper.GetParent(current);
    }

    public static T? FindAncestor<T>(DependencyObject? current) where T : class
    {
        while (current is not null)
        {
            if (current is T match)
                return match;

            current = GetVisualOrLogicalParent(current);
        }

        return null;
    }

    /// <summary>
    /// True when the routed event originated on a view scrollbar (including a captured thumb).
    /// MouseMove bubbles from the thumb, so a scrollbar drag would otherwise start marquee selection.
    /// </summary>
    public static bool IsInScrollBar(DependencyObject? source)
    {
        for (var dep = source; dep is not null; dep = GetVisualOrLogicalParent(dep))
        {
            if (dep is ScrollBar)
                return true;
            if (dep is ListView or DataGrid)
                break;
        }

        return false;
    }

    public static bool IsPointInElement(FrameworkElement element, Point position, UIElement relativeTo)
    {
        if (!element.IsVisible || element.ActualWidth <= 0 || element.ActualHeight <= 0)
            return false;

        try
        {
            var origin = element.TranslatePoint(new Point(), relativeTo);
            return new Rect(origin, element.RenderSize).Contains(position);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public static bool IsPointInNameText(FrameworkElement root, Point position, UIElement relativeTo)
    {
        foreach (var element in StyleHelper.EnumerateVisualChildren(root))
        {
            if (element is not TextBlock || !element.IsVisible)
                continue;

            if (FindAncestor<TextBox>(element) is not null)
                continue;

            if (IsPointInElement(element, position, relativeTo))
                return true;
        }

        return false;
    }

    public static bool IsPointInIconVisuals(FrameworkElement root, Point position, UIElement relativeTo)
    {
        foreach (var element in StyleHelper.EnumerateVisualChildren(root))
        {
            if (!element.IsVisible)
                continue;

            if (element is not Image and not TextBlock)
                continue;

            if (IsPointInElement(element, position, relativeTo))
                return true;
        }

        return false;
    }

    public static bool IsPointInColumnVisuals(
        DataGridRow row,
        DataGridColumn? column,
        Point position,
        UIElement relativeTo,
        bool nameColumn)
    {
        if (column is null || row.Item is null)
            return false;

        if (column.GetCellContent(row.Item) is not FrameworkElement content)
            return false;

        if (nameColumn)
            return IsPointInNameText(content, position, relativeTo);

        return IsPointInIconVisuals(content, position, relativeTo);
    }

    public static bool IsPointInGridNameOrIcon(
        DataGridRow row,
        Point position,
        UIElement relativeTo,
        DataGridColumn? iconColumn,
        DataGridColumn? nameColumn,
        DataGridColumn? packageNameColumn)
    {
        if (IsPointInColumnVisuals(row, iconColumn, position, relativeTo, nameColumn: false))
            return true;

        if (IsPointInColumnVisuals(row, nameColumn, position, relativeTo, nameColumn: true))
            return true;

        return IsPointInColumnVisuals(row, packageNameColumn, position, relativeTo, nameColumn: true);
    }

    public static bool IsPointInIconViewNameOrIcon(ListViewItem item, Point position, UIElement relativeTo)
    {
        var fileIconView = StyleHelper.FindDescendant<FileIconView>(item);
        if (fileIconView is null)
            return false;

        foreach (var element in StyleHelper.EnumerateVisualChildren(fileIconView))
        {
            if (!element.IsVisible)
                continue;

            if (element is Image)
            {
                if (IsPointInElement(element, position, relativeTo))
                    return true;
                continue;
            }

            if (element is not TextBlock)
                continue;

            if (FindAncestor<TextBox>(element) is not null)
                continue;

            if (IsPointInElement(element, position, relativeTo))
                return true;
        }

        return false;
    }

    public static bool IsExplorerNameOrIconHit(
        DependencyObject? originalSource,
        Point position,
        UIElement relativeTo,
        bool isIconView,
        DataGridColumn? iconColumn,
        DataGridColumn? nameColumn,
        DataGridColumn? packageNameColumn)
    {
        if (originalSource is null)
            return false;

        if (isIconView)
        {
            var item = FindAncestor<ListViewItem>(originalSource);
            return item is not null && IsPointInIconViewNameOrIcon(item, position, relativeTo);
        }

        var row = FindAncestor<DataGridRow>(originalSource);
        return row is not null
            && IsPointInGridNameOrIcon(row, position, relativeTo, iconColumn, nameColumn, packageNameColumn);
    }
}
